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
        var ballast = new RailKit.MeshData();
        var rail = new RailKit.MeshData();
        var tie = new RailKit.MeshData();
        for (int i = 0; i < layout.trackOffsets.Length; i++)
            RailKit.AddTrack(ballast, rail, tie, RailKit.Chaikin(LocalTrackPath(i), 2)); // 平滑化して隙間をなくす

        // スロートの分岐器(転てつ機・轍叉・分岐枕木)
        var swMetal = new RailKit.MeshData();
        var swBox = new RailKit.MeshData();
        AddTurnouts(swMetal, swBox, tie, H, T);

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

        RailKit.MeshGO("Ballast", ballast.ToMesh(), MatLib.Get("Ballast"), transform);
        RailKit.MeshGO("Rail", rail.ToMesh(), MatLib.Get("Rail"), transform);
        RailKit.MeshGO("Tie", tie.ToMesh(), MatLib.Get("Tie"), transform);
        RailKit.MeshGO("Switch", swMetal.ToMesh(), MatLib.Get("Switch"), transform);
        RailKit.MeshGO("SwitchBox", swBox.ToMesh(), MatLib.Get("SwitchBox"), transform);
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

    // 線iの駅内経路(ローカル、-z端→+z端)。スロート部で駅間複線の±2.3mへ収束する
    public List<Vector3> LocalTrackPath(int i)
    {
        float off = layout.trackOffsets[i];
        float end = Mathf.Sign(off) * 2.3f;
        float H = HalfLen, T = StationLayout.ThroatLen;
        return new List<Vector3>
        {
            new Vector3(end, 0, -(H + T)),
            new Vector3((off + end) * 0.5f, 0, -(H + T * 0.5f)),
            new Vector3(off, 0, -H),
            new Vector3(off, 0, H),
            new Vector3((off + end) * 0.5f, 0, H + T * 0.5f),
            new Vector3(end, 0, H + T),
        };
    }

    // スロート(駅端の分岐部)に、左右本線(±2.3)を結ぶ渡り線をX字に交差させて描く。
    // 2本のレールが交差する轍叉(クロッシング)+トングレール+転てつ機。
    void AddTurnouts(RailKit.MeshData metal, RailKit.MeshData box, RailKit.MeshData tie, float H, float T)
    {
        const float railY = 0.46f, gauge = 0.72f, half = 2.3f;
        // 左右どちらにも停車線があるとき(=渡り線が成立するとき)だけ描く
        bool hasL = false, hasR = false;
        foreach (int idx in layout.stopTracks)
        {
            if (layout.trackOffsets[idx] < 0) hasL = true; else hasR = true;
        }
        if (!(hasL && hasR)) return;

        for (int sgn = -1; sgn <= 1; sgn += 2)
        {
            float zc = sgn * (H + T * 0.55f);   // 渡り線の中心z
            float L = 8f;                        // 交差の半分の長さ(z方向)
            var La = new Vector3(-half, 0, zc - sgn * L);  // 左線 手前
            var Lb = new Vector3(+half, 0, zc + sgn * L);  // 右線 奥  (左→右へ渡るレール)
            var Ra = new Vector3(+half, 0, zc - sgn * L);
            var Rb = new Vector3(-half, 0, zc + sgn * L);  // 右→左へ渡るレール
            AddCrossRail(metal, La, Lb, railY, gauge);
            AddCrossRail(metal, Ra, Rb, railY, gauge);
            // 交差中心の轍叉ノーズ
            RailKit.AddBox(metal, new Vector3(0, railY - 0.02f, zc), new Vector3(1.0f, 0.22f, 1.4f), Quaternion.identity);
            // 渡り線の枕木(左右本線をまたぐ長い枕木)
            for (int j = -4; j <= 4; j++)
                RailKit.AddBox(tie, new Vector3(0, 0.28f, zc + j * 1.5f), new Vector3(6.4f, 0.12f, 0.26f), Quaternion.identity);
            // トングレール(各本線の交差手前に可動レール)+転てつ機を4隅に
            AddPoint(metal, box, new Vector3(-half, 0, zc - sgn * L), sgn, +1);
            AddPoint(metal, box, new Vector3(+half, 0, zc - sgn * L), sgn, -1);
        }
    }

    // p0→p1へ渡るレール(左右2本の平行レール)を1本の渡りレールとして描く
    static void AddCrossRail(RailKit.MeshData metal, Vector3 p0, Vector3 p1, float railY, float gauge)
    {
        var dir = p1 - p0; dir.y = 0;
        float len = dir.magnitude;
        if (len < 0.1f) return;
        var rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
        var mid = (p0 + p1) * 0.5f + Vector3.up * railY;
        var perp = new Vector3(-dir.z, 0, dir.x).normalized;
        RailKit.AddBox(metal, mid + perp * gauge, new Vector3(0.13f, 0.16f, len), rot);
        RailKit.AddBox(metal, mid - perp * gauge, new Vector3(0.13f, 0.16f, len), rot);
    }

    // 本線側のトングレール(先細り風)と、脇の転てつ機(黄箱+てこ+転てつ棒)
    static void AddPoint(RailKit.MeshData metal, RailKit.MeshData box, Vector3 at, int sgn, int outX)
    {
        var fwd = new Vector3(0, 0, sgn);
        var rot = Quaternion.LookRotation(fwd, Vector3.up);
        // トングレール
        RailKit.AddBox(metal, at + Vector3.up * 0.46f - fwd * 3f, new Vector3(0.11f, 0.16f, 5f), rot);
        // 転てつ機を本線の外側に
        var mach = at + new Vector3(2.4f * outX, 0, 0);
        RailKit.AddBox(box, mach + Vector3.up * 0.42f, new Vector3(0.9f, 0.72f, 1.3f), rot);
        RailKit.AddBox(metal, mach + Vector3.up * 0.95f, new Vector3(0.14f, 0.5f, 0.14f), rot * Quaternion.Euler(0, 0, 22f));
        var rodEnd = at + new Vector3(0.9f * outX, 0, 0);
        var rdir = rodEnd - mach; rdir.y = 0;
        if (rdir.magnitude > 0.1f)
            RailKit.AddBox(metal, (mach + rodEnd) * 0.5f + Vector3.up * 0.34f,
                new Vector3(0.08f, 0.08f, rdir.magnitude), Quaternion.LookRotation(rdir.normalized, Vector3.up));
    }

    public Vector3 TrackWorldPoint(int trackIdx, float z)
        => transform.TransformPoint(new Vector3(layout.trackOffsets[trackIdx], 0, z));

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
