using System.Collections.Generic;
using UnityEngine;

// 駅。対応両数(cars)・面数(faces)・線数(lines)を持ち、メッシュは全て子に生成する。
// ローカル座標系: 駅軸=+z、横=+x。transformのY回転で向きを決める
public class Station : MonoBehaviour
{
    public int cars = 6, faces = 2, lines = 2;
    public string stationName = "駅";
    public bool preview;

    public StationLayout.Result layout;
    public float dev;
    public bool[] occupied;
    public readonly Dictionary<Station, int> waiting = new Dictionary<Station, int>();
    public int developed; // CityGridが建てた棟数
    // M2-B.2で発覚: floatのまま数千回加算し続けると丸め誤差が蓄積し、
    // 速度倍率(=1tickあたりの加算回数)によって最終的な発生人数がズレ得るためdouble化
    double spawnAcc;
    TextMesh label;
    readonly List<GameObject> platformLabels = new List<GameObject>(); // 番線選択中に各停車線へ浮かべる番号

    public int DevLevel => (int)dev;
    public float HalfLen => StationLayout.Length(cars) * 0.5f;
    public Vector3 Axis => transform.rotation * Vector3.forward;
    public Vector3 End(int sign) => transform.position + Axis * (sign * (HalfLen + StationLayout.ThroatLen));

    public int TotalWaiting
    {
        get
        {
            int n = 0;
            foreach (var kv in waiting) n += kv.Value;
            return n;
        }
    }

    public int WaitingCap => faces * cars * 60;

    // M2-B.2: ×1/×5/×20比較テスト用の読み取り専用観測プロパティ。挙動は変えない
    public double SpawnAccumulator => spawnAcc;

    // 子メッシュを(再)生成する。パラメータ変更後に呼び直せる
    public void Build()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediateSafe(transform.GetChild(i).gameObject);
        layout = StationLayout.Compute(faces, lines);
        occupied = new bool[layout.trackOffsets.Length];

        float H = HalfLen, T = StationLayout.ThroatLen;
        RebuildTrackVisual();   // 線路・渡り線・車止め(接続状態で頭端/貫通を切替)

        var plat = new RailKit.MeshData();
        var canopy = new RailKit.MeshData();
        float platLen = cars * StationLayout.CarLength;
        foreach (var p in layout.platforms)
        {
            RailKit.AddBox(plat, new Vector3(p.x, 0.55f, 0), new Vector3(p.y - 1.2f, 1.1f, platLen), Quaternion.identity);
            RailKit.AddBox(canopy, new Vector3(p.x, 4.2f, 0), new Vector3(p.y - 2f, 0.2f, platLen * 0.85f), Quaternion.identity);
            for (float z = -platLen * 0.4f; z <= platLen * 0.4f; z += 12f)
                RailKit.AddBox(canopy, new Vector3(p.x, 2.65f, z), new Vector3(0.25f, 3.1f, 0.25f), Quaternion.identity);
        }
        // 小さな駅舎を横に
        var house = new RailKit.MeshData();
        float houseX = layout.totalWidth * 0.5f + 6f;
        RailKit.AddBox(house, new Vector3(houseX, 2.2f, 0), new Vector3(9f, 4.4f, 7f), Quaternion.identity);
        RailKit.AddBox(house, new Vector3(houseX, 4.6f, 0), new Vector3(10f, 0.4f, 8f), Quaternion.identity);

        RailKit.MeshGO("Platform", plat.ToMesh(), MatLib.Get("Platform"), transform);
        RailKit.MeshGO("Canopy", canopy.ToMesh(), MatLib.Get("Canopy"), transform);
        RailKit.MeshGO("House", house.ToMesh(), MatLib.Get("StationHouse"), transform);

        var col = gameObject.GetComponent<BoxCollider>();
        if (col == null) col = gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(0, 5, 0);
        // スマホでのタップを外しにくいよう実寸よりだいぶ大きめに取る
        col.size = new Vector3(layout.totalWidth + 50f, 10f, (H + T) * 2f + 30f);

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(transform, false);
        labelGo.transform.localPosition = new Vector3(0, 20f, 0);
        label = labelGo.AddComponent<TextMesh>();
        label.font = MatLib.JpFont;
        label.fontSize = 64;
        label.characterSize = 0.9f;
        label.anchor = TextAnchor.LowerCenter;
        label.alignment = TextAlignment.Center;
        label.color = new Color(0.12f, 0.12f, 0.18f);
        labelGo.GetComponent<MeshRenderer>().sharedMaterial = MatLib.JpFont.material;
        UpdateLabel();
        // 街はCityGridがワールドグリッド上にまとめて生成する(駅の子ではない)
    }

    // 接続済みの端 [0]=sign-1, [1]=sign+1
    bool[] GetConnectedEnds()
    {
        var c = new bool[2];
        foreach (var seg in TrackNetwork.segments)
        {
            if (seg.a == this) c[seg.signA > 0 ? 1 : 0] = true;
            if (seg.b == this) c[seg.signB > 0 ? 1 : 0] = true;
        }
        return c;
    }

    // 線iの駅内経路(ローカル)。接続された端はスロート(収束→リード→駅端)まで伸ばし、
    // 未接続の端はホーム端で止める(頭端式)
    List<Vector3> TrackVisualPath(int i, bool cMinus, bool cPlus)
    {
        float off = layout.trackOffsets[i];
        float end = Mathf.Sign(off) * 2.3f;
        float H = HalfLen, T = StationLayout.ThroatLen, L = StationLayout.LeadLen;
        float cz = H + (T - L), mz = H + (T - L) * 0.5f;
        var pts = new List<Vector3>();
        if (cMinus)
        {
            pts.Add(new Vector3(end, 0, -(H + T)));
            pts.Add(new Vector3(end, 0, -cz));
            pts.Add(new Vector3((off + end) * 0.5f, 0, -mz));
        }
        pts.Add(new Vector3(off, 0, -H));   // ホーム端
        pts.Add(new Vector3(off, 0, H));
        if (cPlus)
        {
            pts.Add(new Vector3((off + end) * 0.5f, 0, mz));
            pts.Add(new Vector3(end, 0, cz));
            pts.Add(new Vector3(end, 0, H + T));
        }
        return pts;
    }

    // 線路・渡り線・車止めを接続状態に応じて再生成(TrackWork子にまとめる)。
    // 線路の接続/撤去/ロード時に呼ぶと、繋がった端は貫通・未接続の端は頭端(車止め)になる
    public void RebuildTrackVisual()
    {
        if (layout.trackOffsets == null) return;
        var old = transform.Find("TrackWork");
        if (old != null) DestroyImmediateSafe(old.gameObject);
        var tw = new GameObject("TrackWork");
        tw.transform.SetParent(transform, false);

        var conn = GetConnectedEnds();
        float H = HalfLen, T = StationLayout.ThroatLen, L = StationLayout.LeadLen;
        var ballast = new RailKit.MeshData();
        var rail = new RailKit.MeshData();
        var tie = new RailKit.MeshData();
        var swMetal = new RailKit.MeshData();
        var swBox = new RailKit.MeshData();

        for (int i = 0; i < layout.trackOffsets.Length; i++)
            RailKit.AddTrack(ballast, rail, tie, RailKit.Chaikin(TrackVisualPath(i, conn[0], conn[1]), 2));

        bool hasL = false, hasR = false;
        foreach (int si in layout.stopTracks) { if (layout.trackOffsets[si] < 0) hasL = true; else hasR = true; }
        for (int e = 0; e < 2; e++)
        {
            int sign = e == 0 ? -1 : 1;
            if (conn[e])
            {
                // 接続端: 駅前(リード)に両渡り線
                if (hasL && hasR)
                    RailKit.AddCrossover(rail, swMetal, swBox, tie, ballast,
                        new Vector3(0, 0, sign * (H + T - L * 0.5f)), new Vector3(0, 0, 1));
            }
            else
            {
                // 未接続端: 各線のホーム端に車止め(頭端式)
                foreach (float off in layout.trackOffsets)
                    AddBufferStop(swMetal, swBox, new Vector3(off, 0, sign * H), sign);
            }
        }

        RailKit.MeshGO("Ballast", ballast.ToMesh(), MatLib.Get("Ballast"), tw.transform);
        RailKit.MeshGO("Rail", rail.ToMesh(), MatLib.Get("Rail"), tw.transform);
        RailKit.MeshGO("Tie", tie.ToMesh(), MatLib.Get("Tie"), tw.transform);
        RailKit.MeshGO("Switch", swMetal.ToMesh(), MatLib.Get("Switch"), tw.transform);
        RailKit.MeshGO("SwitchBox", swBox.ToMesh(), MatLib.Get("SwitchBox"), tw.transform);
    }

    public Vector3 TrackWorldPoint(int trackIdx, float z)
        => transform.TransformPoint(new Vector3(layout.trackOffsets[trackIdx], 0, z));

    // 車止め1基。at=線路端(ローカル)、sign=どちらの端か(内向き=-sign*z)
    static void AddBufferStop(RailKit.MeshData metal, RailKit.MeshData box, Vector3 at, int sign)
    {
        float ry = RailKit.RailTop;
        float inS = -sign;   // 列車が来る内向き
        // 基台(バラスト上の台)
        RailKit.AddBox(metal, at + new Vector3(0, 0.32f, inS * 1.4f), new Vector3(2.4f, 0.34f, 3.0f), Quaternion.identity);
        // 斜めの支え2本(内側へ倒す)
        for (int sx = -1; sx <= 1; sx += 2)
        {
            var top = at + new Vector3(0.62f * sx, ry + 0.15f, 0);
            var bot = at + new Vector3(0.62f * sx, 0.4f, inS * 3.0f);
            var dir = bot - top; float len = dir.magnitude;
            RailKit.AddBox(metal, (top + bot) * 0.5f, new Vector3(0.16f, 0.16f, len),
                Quaternion.LookRotation(dir.normalized, Vector3.up));
        }
        // バンパー(緩衝面, 黄): レール頭頂で軌間をまたぐ横梁を、内向きに構える
        RailKit.AddBox(box, at + new Vector3(0, ry + 0.2f, inS * 0.1f), new Vector3(2.1f, 0.5f, 0.55f), Quaternion.identity);
    }

    // 建て替えプレビューを重ねる間、実駅のメッシュだけ隠す(コライダーは残す)
    public void SetRenderersVisible(bool v)
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = v;
    }

    // 停車可能な番線(左→右の物理順)。UIの「N番線」はこの並び順で1始まり
    public IReadOnlyList<int> StopTracks => layout.stopTracks;
    public int PlatformCount => layout.stopTracks.Count;
    public int PlatformNumberOf(int trackIdx)
    {
        int n = layout.stopTracks.IndexOf(trackIdx);
        return n < 0 ? 0 : n + 1;
    }
    public int TrackOfPlatform(int platformNo)
    {
        int i = platformNo - 1;
        return (i >= 0 && i < layout.stopTracks.Count) ? layout.stopTracks[i] : layout.stopTracks[0];
    }

    // 番線選択中、各停車線の上に「N番線」ラベルを浮かべる(UIの番線ボタンと物理対応させる)
    public void ShowPlatformNumbers()
    {
        HidePlatformNumbers();
        for (int k = 0; k < layout.stopTracks.Count; k++)
        {
            int trk = layout.stopTracks[k];
            // 停車線ごとにz位置を少しずらして重なりを避ける(番線が多いほど前後に散らす)
            float zStagger = (k - (layout.stopTracks.Count - 1) * 0.5f) * (StationLayout.CarLength * 1.1f);
            var go = new GameObject("PFNum" + (k + 1));
            go.transform.SetParent(transform, false);
            go.transform.position = TrackWorldPoint(trk, zStagger) + Vector3.up * 12f;
            var tm = go.AddComponent<TextMesh>();
            tm.font = MatLib.JpFont;
            tm.text = (k + 1) + "番線";
            tm.fontSize = 64;
            tm.characterSize = 1.3f;
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(1f, 0.74f, 0.1f);
            go.GetComponent<MeshRenderer>().sharedMaterial = MatLib.JpFont.material;
            platformLabels.Add(go);
        }
    }

    public void HidePlatformNumbers()
    {
        foreach (var g in platformLabels) if (g != null) DestroyImmediateSafe(g);
        platformLabels.Clear();
    }

    public bool TryReserve(out int trackIdx)
    {
        foreach (int i in layout.stopTracks)
        {
            if (!occupied[i])
            {
                occupied[i] = true;
                trackIdx = i;
                return true;
            }
        }
        trackIdx = -1;
        return false;
    }

    // 指定番線を確保(空いていなければfalse)。番線指定運転で使う
    public bool TryReserveSpecific(int trackIdx)
    {
        if (trackIdx < 0 || trackIdx >= occupied.Length) return false;
        if (occupied[trackIdx]) return false;
        occupied[trackIdx] = true;
        return true;
    }

    // 進行方向左側のホーム線を優先して確保(左側通行)。
    // enterSign: 進入してくる駅端の符号。駅内の進行方向はローカル-enterSign*zなので
    // 左側の線はローカルx符号がenterSignと一致する側
    public bool TryReserveFor(int enterSign, out int trackIdx)
    {
        foreach (int i in layout.stopTracks)
        {
            if (occupied[i]) continue;
            if (Mathf.Sign(layout.trackOffsets[i]) == enterSign)
            {
                occupied[i] = true;
                trackIdx = i;
                return true;
            }
        }
        return TryReserve(out trackIdx); // 左側が塞がっていれば空いている線へ
    }

    public void Release(int trackIdx)
    {
        if (trackIdx >= 0 && trackIdx < occupied.Length) occupied[trackIdx] = false;
    }

    // 乗客発生。dtMin: ゲーム内経過分
    public void Tick(float dtMin)
    {
        if (preview) return;
        var reach = TrackNetwork.Reachable(this);
        if (reach.Count == 0) return;
        if (TotalWaiting >= WaitingCap) return;
        spawnAcc += dtMin * (0.8f + 0.6f * DevLevel);
        int n = (int)spawnAcc;
        if (n <= 0) return;
        spawnAcc -= n;
        // 粗いtick(高いsimDt)では1tickでnが2以上になり得るため、WaitingCap残量で
        // 上限を切る(切らないとWaitingCapを超過し得るバグをCodexレビューで指摘された)
        n = Mathf.Min(n, WaitingCap - TotalWaiting);
        if (n <= 0) return;
        // reach(HashSet)は列挙順が保証されないため、TrackNetwork.stationsの登録順で
        // フィルタして安定させる(同一seed・同一手順で同じ行き先分布になるようにするため)
        float totalW = 0;
        foreach (var s in TrackNetwork.stations) if (reach.Contains(s)) totalW += 1 + s.dev;
        for (int k = 0; k < n; k++)
        {
            float r = GameRandom.NextFloat01() * totalW;
            Station dest = null;
            foreach (var s in TrackNetwork.stations)
            {
                if (!reach.Contains(s)) continue;
                r -= 1 + s.dev;
                dest = s;
                if (r <= 0) break;
            }
            if (dest == null) continue;
            int cur;
            waiting.TryGetValue(dest, out cur);
            waiting[dest] = cur + 1;
        }
        UpdateLabel();
    }

    public void OnDeparture(int boarded)
    {
        dev += boarded * 0.004f;
        if (!preview) CityGrid.Develop(this);
        UpdateLabel();
    }

    public void ForceDev(float d)
    {
        dev = d;
        if (!preview) CityGrid.Develop(this);
        UpdateLabel();
    }

    public void UpdateLabel()
    {
        if (label != null)
            label.text = stationName + "\n待" + TotalWaiting + " Lv" + DevLevel;
    }

    void LateUpdate()
    {
        if (Camera.main == null) return;
        var fwd = Camera.main.transform.forward;
        var rot = Quaternion.LookRotation(new Vector3(fwd.x, fwd.y, fwd.z));
        if (label != null) label.transform.rotation = rot;
        for (int i = 0; i < platformLabels.Count; i++)
            if (platformLabels[i] != null) platformLabels[i].transform.rotation = rot;
    }

    static void DestroyImmediateSafe(GameObject go)
    {
        if (Application.isPlaying) Destroy(go);
        else DestroyImmediate(go);
    }
}
