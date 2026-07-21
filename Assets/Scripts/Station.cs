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
    float spawnAcc;
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

        AddStopMarkers();

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

    // 両数ごとの停止位置目標を各停車線の枕木上に据え置き型で置く。列車は中央停車なので
    // N両編成の前端は ±N*車長/2。両方向ぶん、進来方向を向けて設置
    void AddStopMarkers()
    {
        var counts = new List<int>();
        for (int n = 2; n <= cars; n += 2) counts.Add(n);
        if (counts.Count == 0 || counts[counts.Count - 1] != cars) counts.Add(cars);
        var root = new GameObject("StopMarkers");
        root.transform.SetParent(transform, false);
        var frame = new RailKit.MeshData();   // 台座・支柱(金属)
        var plate = new RailKit.MeshData();   // 黄プレート
        var digit = new RailKit.MeshData();   // 数字(立体・不透明)
        float carL = StationLayout.CarLength;
        foreach (int trk in layout.stopTracks)
        {
            float off = layout.trackOffsets[trk];
            // 最寄りホーム側の軌道端へ寄せる(列車の車体幅±1.45を避けて据え置き)
            float side = 1f;
            if (layout.platforms.Count > 0)
            {
                var pl = layout.platforms[0]; float bd = 1e9f;
                foreach (var p in layout.platforms) { float d = Mathf.Abs(p.x - off); if (d < bd) { bd = d; pl = p; } }
                side = Mathf.Sign(pl.x - off);
            }
            float mx = off + side * 1.65f;
            foreach (int n in counts)
            {
                float fz = n * carL * 0.5f;
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    float z = sgn * fz;
                    RailKit.AddBox(frame, new Vector3(mx, 0.5f, z), new Vector3(0.5f, 0.5f, 0.5f), Quaternion.identity);
                    RailKit.AddBox(frame, new Vector3(mx, 0.95f, z), new Vector3(0.1f, 0.7f, 0.1f), Quaternion.identity);
                    RailKit.AddBox(plate, new Vector3(mx, 1.35f, z), new Vector3(0.62f, 0.6f, 0.1f), Quaternion.identity);
                    // 両数の数字を7セグ風の立体で両面に(フォントは深度無視で床を透けるため)
                    AddDigits(digit, n, new Vector3(mx, 1.35f, z + 0.06f), Quaternion.identity, 0.4f);
                    AddDigits(digit, n, new Vector3(mx, 1.35f, z - 0.06f), Quaternion.Euler(0, 180f, 0), 0.4f);
                }
            }
        }
        RailKit.MeshGO("SPFrame", frame.ToMesh(), MatLib.Get("Switch"), root.transform);
        RailKit.MeshGO("SPPlate", plate.ToMesh(), MatLib.Get("SwitchBox"), root.transform);
        RailKit.MeshGO("SPDigit", digit.ToMesh(), MatLib.Get("TrainDark"), root.transform);
    }

    // 7セグメント風の立体数字。centerを中心、rotで向き、hが桁高さ
    static readonly int[] SegMask = { 0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F }; // 0-9: gfedcba
    static void AddDigits(RailKit.MeshData md, int value, Vector3 center, Quaternion rot, float h)
    {
        string s = value.ToString();
        float w = h * 0.58f, t = h * 0.15f, dep = 0.05f;
        float spacing = w + t * 1.6f;
        float x0 = -(s.Length - 1) * 0.5f * spacing;
        for (int i = 0; i < s.Length; i++)
        {
            int d = s[i] - '0';
            if (d < 0 || d > 9) continue;
            int mask = SegMask[d];
            float cx = x0 + i * spacing;
            // a(top) b(TR) c(BR) d(bottom) e(BL) f(TL) g(mid) -> bit 0..6
            AddSeg(md, mask, 0, center, rot, cx, h * 0.5f, w, t, dep, true);   // a
            AddSeg(md, mask, 1, center, rot, cx + w * 0.5f, h * 0.25f, h * 0.5f, t, dep, false); // b
            AddSeg(md, mask, 2, center, rot, cx + w * 0.5f, -h * 0.25f, h * 0.5f, t, dep, false); // c
            AddSeg(md, mask, 3, center, rot, cx, -h * 0.5f, w, t, dep, true);  // d
            AddSeg(md, mask, 4, center, rot, cx - w * 0.5f, -h * 0.25f, h * 0.5f, t, dep, false); // e
            AddSeg(md, mask, 5, center, rot, cx - w * 0.5f, h * 0.25f, h * 0.5f, t, dep, false);  // f
            AddSeg(md, mask, 6, center, rot, cx, 0, w, t, dep, true);          // g
        }
    }

    static void AddSeg(RailKit.MeshData md, int mask, int bit, Vector3 center, Quaternion rot,
        float lx, float ly, float len, float t, float dep, bool horiz)
    {
        if ((mask & (1 << bit)) == 0) return;
        var size = horiz ? new Vector3(len, t, dep) : new Vector3(t, len, dep);
        RailKit.AddBox(md, center + rot * new Vector3(lx, ly, 0), size, rot);
    }

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
        float totalW = 0;
        foreach (var s in reach) totalW += 1 + s.dev;
        for (int k = 0; k < n; k++)
        {
            float r = Random.value * totalW;
            Station dest = null;
            foreach (var s in reach)
            {
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
