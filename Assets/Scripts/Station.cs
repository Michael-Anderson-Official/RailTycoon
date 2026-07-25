using System.Collections.Generic;
using UnityEngine;

// 駅。対応両数(cars)・面数(faces)・線数(lines)を持ち、メッシュは全て子に生成する。
// ローカル座標系: 駅軸=+z、横=+x。transformのY回転で向きを決める
public class Station : MonoBehaviour
{
    public int id; // M2-C: セーブ/ロードを跨いで安定な識別子。0は未割当(preview等)
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
    readonly List<TextMesh> stationSigns = new List<TextMesh>(); // 実景用のホーム駅名標

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

    // M2-C: セーブロード復元専用(SaveLoadと同一アセンブリ内からのみ呼ばれる想定)
    internal void RestoreSpawnAccumulator(double v) => spawnAcc = v;

    // 子メッシュを(再)生成する。パラメータ変更後に呼び直せる
    public void Build()
    {
        // M2-D: 面・線構成が変わらない改築(両数のみ変更等)なら、ホーム縁のモード
        // (乗降可/乗車専用/降車専用/使用停止)を新しいlayoutへ引き継ぐ。
        // StationLayout.Compute(faces,lines)は純粋関数なので、faces/linesが
        // 同じなら新旧のedges列は必ず同じ順序・同じ内容(モードを除く)になる
        bool hadLayout = layout.platforms != null && layout.trackOffsets != null;
        int oldFaces = hadLayout ? layout.platforms.Count : -1;
        int oldLines = hadLayout ? layout.trackOffsets.Length : -1;
        var oldEdges = hadLayout ? layout.edges : null;

        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediateSafe(transform.GetChild(i).gameObject);
        stationSigns.Clear();
        layout = StationLayout.Compute(faces, lines);

        if (oldEdges != null && oldFaces == faces && oldLines == lines && oldEdges.Count == layout.edges.Count)
        {
            for (int i = 0; i < layout.edges.Count; i++)
            {
                var e = layout.edges[i];
                e.mode = oldEdges[i].mode;
                layout.edges[i] = e;
            }
        }

        occupied = new bool[layout.trackOffsets.Length];

        float H = HalfLen, T = StationLayout.ThroatLen;
        RebuildTrackVisual();   // 線路・渡り線・車止め(接続状態で頭端/貫通を切替)

        // 駅の当たり判定・番線ロジックは従来のlayoutだけを使い、以下は全て視覚層として
        // 生成する。外観の作り込みが列車停止位置やセーブ互換へ影響しないようにする。
        var platformBase = new RailKit.MeshData();
        var platformSurface = new RailKit.MeshData();
        var platformEdge = new RailKit.MeshData();
        var tactile = new RailKit.MeshData();
        var warningLine = new RailKit.MeshData();
        var drain = new RailKit.MeshData();
        var canopyRoof = new RailKit.MeshData();
        var metalwork = new RailKit.MeshData();
        var lighting = new RailKit.MeshData();
        var furniture = new RailKit.MeshData();
        var signBoard = new RailKit.MeshData();
        float platLen = cars * StationLayout.CarLength;
        for (int pi = 0; pi < layout.platforms.Count; pi++)
        {
            var p = layout.platforms[pi];
            // layout上のホーム境界をそのまま描画境界にする。旧描画は左右を各0.6m
            // 縮めていたため、レイアウト計算よりさらに1.2m広い隙間を生んでいた。
            float visualW = Mathf.Max(2.6f, p.y - 0.02f);
            const float surfaceThick = 0.10f;
            float baseTop = RailDimensions.PlatformTop - surfaceThick;

            // コンクリート躯体の上に薄い舗装層を重ね、ホーム縁だけ白い笠石を見せる。
            RailKit.AddBox(platformBase, new Vector3(p.x, baseTop * 0.5f, 0),
                new Vector3(visualW, baseTop, platLen), Quaternion.identity);
            RailKit.AddBox(platformSurface,
                new Vector3(p.x, RailDimensions.PlatformTop - surfaceThick * 0.5f, 0),
                new Vector3(visualW - 0.04f, surfaceThick, platLen - 0.35f),
                Quaternion.identity);

            // 実物のホームは終端で線路の収束に合わせて細くなる。列車が停まる範囲
            // (=platLen、編成長と同じ)は全幅のまま残し、そこから駅端(HalfLen)までの
            // 余地へ絞った端部を継ぎ足す。全幅部を削らないので停車時にホームとの
            // 隙間は広がらない
            float apronLen = HalfLen - platLen * 0.5f;
            if (apronLen > 0.5f)
            {
                float tipHalfW = Mathf.Max(1.1f, visualW * 0.34f);
                for (int endSign = -1; endSign <= 1; endSign += 2)
                {
                    float z0 = endSign * platLen * 0.5f;
                    float z1 = endSign * (HalfLen - 0.3f);
                    AddTaperedApron(platformBase, p.x, z0, z1,
                        visualW * 0.5f, tipHalfW, 0f, baseTop);
                    AddTaperedApron(platformSurface, p.x, z0, z1,
                        visualW * 0.5f - 0.02f, tipHalfW - 0.02f,
                        RailDimensions.PlatformTop - surfaceThick, RailDimensions.PlatformTop);
                }
            }

            foreach (var e in layout.edges)
            {
                if (e.platformIndex != pi) continue;
                float edgeX = p.x - e.side * visualW * 0.5f;
                // 線路側から順に、白い笠石→京王線のオレンジ警戒線→内方線付き
                // 点状ブロック→排水帯。いずれも線路側へは張り出させない。
                RailKit.AddBox(platformEdge,
                    new Vector3(edgeX + e.side * 0.14f, RailDimensions.PlatformTop - 0.08f, 0),
                    new Vector3(0.28f, 0.16f, platLen - 0.25f), Quaternion.identity);
                RailKit.AddBox(warningLine,
                    new Vector3(edgeX + e.side * 0.39f, RailDimensions.PlatformTop + 0.012f, 0),
                    new Vector3(0.09f, 0.024f, platLen - 0.55f), Quaternion.identity);
                RailKit.AddBox(tactile,
                    new Vector3(edgeX + e.side * 0.75f, RailDimensions.PlatformTop + 0.018f, 0),
                    new Vector3(0.48f, 0.055f, platLen - 1.0f), Quaternion.identity);
                RailKit.AddBox(drain,
                    new Vector3(edgeX + e.side * 1.12f, RailDimensions.PlatformTop + 0.012f, 0),
                    new Vector3(0.12f, 0.045f, platLen - 1.2f), Quaternion.identity);
            }

            // 京王線の地上駅で一般的な鋼製上屋を、緩い切妻屋根+中央柱+横梁で表現。
            float roofW = Mathf.Max(3.2f, visualW - 0.7f);
            float coveredLen = platLen * 0.78f;
            const float roofAngle = 7f;
            float halfRoofW = roofW * 0.5f;
            float rise = halfRoofW * Mathf.Tan(roofAngle * Mathf.Deg2Rad);
            float beamY = RailDimensions.PlatformTop + 2.68f;
            float roofY = beamY + 0.10f + rise * 0.5f;
            RailKit.AddBox(canopyRoof, new Vector3(p.x - roofW * 0.25f, roofY, 0),
                new Vector3(halfRoofW + 0.18f, 0.18f, coveredLen),
                Quaternion.Euler(0, 0, roofAngle));
            RailKit.AddBox(canopyRoof, new Vector3(p.x + roofW * 0.25f, roofY, 0),
                new Vector3(halfRoofW + 0.18f, 0.18f, coveredLen),
                Quaternion.Euler(0, 0, -roofAngle));

            float postMin = -coveredLen * 0.5f + 3f;
            float postMax = coveredLen * 0.5f - 3f;
            for (float z = postMin; z <= postMax + 0.01f; z += 18f)
            {
                RailKit.AddBox(metalwork,
                    new Vector3(p.x, (RailDimensions.PlatformTop + beamY) * 0.5f, z),
                    new Vector3(0.22f, beamY - RailDimensions.PlatformTop, 0.22f),
                    Quaternion.identity);
                RailKit.AddBox(metalwork, new Vector3(p.x, beamY, z),
                    new Vector3(roofW - 0.35f, 0.16f, 0.24f), Quaternion.identity);
                RailKit.AddBox(lighting, new Vector3(p.x, beamY - 0.16f, z + 4.5f),
                    new Vector3(1.25f, 0.08f, 0.34f), Quaternion.identity);
            }

            // ホーム中央のベンチ。線路側の動線を塞がないよう柱の近くへ寄せる。
            float[] benchZ = { -platLen * 0.18f, platLen * 0.18f };
            foreach (float z in benchZ)
            {
                RailKit.AddBox(furniture,
                    new Vector3(p.x + 0.75f, RailDimensions.PlatformTop + 0.40f, z),
                    new Vector3(0.62f, 0.14f, 3.2f), Quaternion.identity);
                RailKit.AddBox(furniture,
                    new Vector3(p.x + 1.02f, RailDimensions.PlatformTop + 0.77f, z),
                    new Vector3(0.12f, 0.75f, 3.2f), Quaternion.identity);
                RailKit.AddBox(metalwork,
                    new Vector3(p.x + 0.75f, RailDimensions.PlatformTop + 0.17f, z - 1.1f),
                    new Vector3(0.12f, 0.48f, 0.12f), Quaternion.identity);
                RailKit.AddBox(metalwork,
                    new Vector3(p.x + 0.75f, RailDimensions.PlatformTop + 0.17f, z + 1.1f),
                    new Vector3(0.12f, 0.48f, 0.12f), Quaternion.identity);
            }

            // ホーム端の転落防止柵。長手方向の線路側は列車に開放したままにする。
            for (int endSign = -1; endSign <= 1; endSign += 2)
            {
                float z = endSign * (platLen * 0.5f - 0.75f);
                for (int railNo = 0; railNo < 2; railNo++)
                    RailKit.AddBox(metalwork,
                        new Vector3(p.x, RailDimensions.PlatformTop + 0.52f + railNo * 0.48f, z),
                        new Vector3(visualW - 0.35f, 0.1f, 0.12f), Quaternion.identity);
                for (int post = -1; post <= 1; post++)
                    RailKit.AddBox(metalwork,
                        new Vector3(p.x + post * (visualW * 0.42f),
                            RailDimensions.PlatformTop + 0.61f, z),
                        new Vector3(0.12f, 1.25f, 0.12f), Quaternion.identity);
            }

            // 長いホームは駅名標を2箇所、短いホームは中央1箇所。両面表示にする。
            int signCount = platLen >= 160f ? 2 : 1;
            for (int si = 0; si < signCount; si++)
            {
                float signZ = signCount == 1 ? 0 : (si == 0 ? -platLen * 0.23f : platLen * 0.23f);
                float signY = RailDimensions.PlatformTop + 1.82f;
                RailKit.AddBox(signBoard, new Vector3(p.x, signY, signZ),
                    new Vector3(0.14f, 0.92f, 3.8f), Quaternion.identity);
                CreateStationSignText(pi, si, -1, new Vector3(p.x - 0.08f, signY, signZ));
                CreateStationSignText(pi, si, 1, new Vector3(p.x + 0.08f, signY, signZ));
            }
        }

        // 駅舎は壁・庇・ガラス出入口・方立を分け、単なる箱に見えないようにする。
        var house = new RailKit.MeshData();
        var glass = new RailKit.MeshData();
        float houseX = layout.totalWidth * 0.5f + 6.5f;
        RailKit.AddBox(house, new Vector3(houseX, 2.35f, 0), new Vector3(10f, 4.7f, 8.5f), Quaternion.identity);
        RailKit.AddBox(canopyRoof, new Vector3(houseX, 4.78f, 0), new Vector3(11.2f, 0.28f, 9.5f), Quaternion.identity);
        RailKit.AddBox(canopyRoof, new Vector3(houseX + 5.25f, 3.75f, 0), new Vector3(1.8f, 0.18f, 6.4f), Quaternion.identity);
        RailKit.AddBox(glass, new Vector3(houseX + 5.02f, 2.15f, 0), new Vector3(0.12f, 2.9f, 5.6f), Quaternion.identity);
        for (int mullion = -2; mullion <= 2; mullion++)
            RailKit.AddBox(metalwork, new Vector3(houseX + 5.12f, 2.15f, mullion * 1.35f),
                new Vector3(0.1f, 3.0f, 0.1f), Quaternion.identity);

        RailKit.MeshGO("PlatformBase", platformBase.ToMesh(), MatLib.Get("Platform"), transform);
        RailKit.MeshGO("PlatformSurface", platformSurface.ToMesh(), MatLib.Get("StationHouse"), transform);
        RailKit.MeshGO("PlatformEdge", platformEdge.ToMesh(), MatLib.Get("StationHouse"), transform);
        RailKit.MeshGO("TactilePaving", tactile.ToMesh(), MatLib.Get("SwitchBox"), transform);
        RailKit.MeshGO("WarningLine", warningLine.ToMesh(),
            MatLib.Tinted("TrainBase", new Color(0.95f, 0.35f, 0.05f)), transform);
        RailKit.MeshGO("Drainage", drain.ToMesh(), MatLib.Get("Switch"), transform);
        RailKit.MeshGO("CanopyRoof", canopyRoof.ToMesh(), MatLib.Get("Canopy"), transform);
        RailKit.MeshGO("Metalwork", metalwork.ToMesh(), MatLib.Get("Switch"), transform);
        RailKit.MeshGO("Lighting", lighting.ToMesh(), MatLib.Get("StationHouse"), transform);
        RailKit.MeshGO("Furniture", furniture.ToMesh(), MatLib.Get("Tie"), transform);
        RailKit.MeshGO("StationSigns", signBoard.ToMesh(), MatLib.Get("Canopy"), transform);
        RailKit.MeshGO("House", house.ToMesh(), MatLib.Get("StationHouse"), transform);
        RailKit.MeshGO("Glass", glass.ToMesh(), MatLib.Get("BuildingHigh"), transform);

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

    void CreateStationSignText(int platformIndex, int signIndex, int side, Vector3 localPosition)
    {
        var go = new GameObject("StationSignText_" + platformIndex + "_" + signIndex + "_" + side);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.Euler(0, side > 0 ? 90f : -90f, 0);
        var tm = go.AddComponent<TextMesh>();
        tm.font = MatLib.JpFont;
        tm.text = stationName;
        tm.fontSize = 64;
        tm.characterSize = 0.115f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.white;
        go.GetComponent<MeshRenderer>().sharedMaterial = MatLib.JpFont.material;
        stationSigns.Add(tm);
    }

    // 接続済みの端 [0]=sign-1, [1]=sign+1。
    // segmentsOverrideを渡した場合はTrackNetwork.segments(生きたワールド)ではなく
    // それを参照する(M2-C: セーブロードのstaging中、まだTrackNetworkへ登録していない
    // 段階で正しい接続状態を計算するために必要)
    bool[] GetConnectedEnds(IReadOnlyList<TrackSegment> segmentsOverride = null)
    {
        var segs = segmentsOverride ?? (IReadOnlyList<TrackSegment>)TrackNetwork.segments;
        var c = new bool[2];
        foreach (var seg in segs)
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
    public void RebuildTrackVisual(IReadOnlyList<TrackSegment> segmentsOverride = null)
    {
        if (layout.trackOffsets == null) return;
        var old = transform.Find("TrackWork");
        if (old != null) DestroyImmediateSafe(old.gameObject);
        var tw = new GameObject("TrackWork");
        tw.transform.SetParent(transform, false);

        var conn = GetConnectedEnds(segmentsOverride);
        float H = HalfLen, T = StationLayout.ThroatLen, L = StationLayout.LeadLen;
        var ballast = new RailKit.MeshData();
        var rail = new RailKit.MeshData();
        var tie = new RailKit.MeshData();
        var swMetal = new RailKit.MeshData();
        var swBox = new RailKit.MeshData();

        for (int i = 0; i < layout.trackOffsets.Length; i++)
            RailKit.AddTrack(ballast, rail, tie,
                RailKit.Chaikin(TrackVisualPath(i, conn[0], conn[1]), 2),
                TrackBedType.Ballast, null, RailDimensions.StationBedHalfWidth);

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

    // ホーム端の絞り(z0側が全幅halfW0、z1側が細いhalfW1)。yBottom..yTopの角柱を
    // AddBoxと同じ頂点順(bit0=x, bit1=y, bit2=z)で組む
    static void AddTaperedApron(RailKit.MeshData md, float centerX, float z0, float z1,
        float halfW0, float halfW1, float yBottom, float yTop)
    {
        var c = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            bool farEnd = (i & 4) != 0;
            float z = farEnd ? z1 : z0;
            float hw = farEnd ? halfW1 : halfW0;
            c[i] = new Vector3(
                centerX + ((i & 1) == 0 ? -hw : hw),
                (i & 2) == 0 ? yBottom : yTop,
                z);
        }
        // z0>z1(-側の端部)ではzの前後が反転し面が裏返るので、頂点順を入れ替える
        if (z1 < z0)
            for (int i = 0; i < 4; i++)
            {
                var tmp = c[i]; c[i] = c[i + 4]; c[i + 4] = tmp;
            }
        RailKit.AddHexahedron(md, c);
    }

    // ワールド座標の点が、この駅のホーム躯体の平面範囲(marginだけ広げたもの)に
    // 入っているか。駅間の線路が途中の駅のホームを貫通しないか判定するのに使う
    // (TrackSegmentは両端の駅しか見ないため、間に別の駅があっても素通しで描画される)。
    // Build()前でlayoutが未設定の場合はfalse
    public bool PlatformAreaContains(Vector3 world, float margin)
    {
        if (layout.platforms == null || layout.platforms.Count == 0) return false;
        var local = transform.InverseTransformPoint(world);
        float halfLen = cars * StationLayout.CarLength * 0.5f;
        if (Mathf.Abs(local.z) > halfLen + margin) return false;
        foreach (var p in layout.platforms)
        {
            // Build()のvisualWと同じ描画幅を使う(判定と見た目をずらさない)
            float visualW = Mathf.Max(2.6f, p.y - 0.02f);
            if (Mathf.Abs(local.x - p.x) <= visualW * 0.5f + margin) return true;
        }
        return false;
    }

    // ---- 建設時の当たり判定 ----
    // 駅が平面上で占有する矩形(構内の幅×駅長)。ホーム範囲(PlatformAreaContains)より
    // 広く、番線・ホーム全体を含む。駅どうしの重なりや、既設線路との衝突判定に使う
    public float FootprintHalfWidth => layout.trackOffsets == null ? 0f : layout.totalWidth * 0.5f;
    public float FootprintHalfLength => HalfLen;

    public bool FootprintContains(Vector3 world, float margin)
    {
        if (layout.trackOffsets == null) return false;
        var local = transform.InverseTransformPoint(world);
        return Mathf.Abs(local.x) <= FootprintHalfWidth + margin
            && Mathf.Abs(local.z) <= FootprintHalfLength + margin;
    }

    // 2駅の占有矩形(それぞれ任意の向き)が平面上で重なるか。分離軸定理で厳密に見る
    // (4隅の内包判定だけでは、十字に交差する配置を取りこぼすため)
    public static bool FootprintsOverlap(Station a, Station b, float margin)
    {
        if (a == null || b == null) return false;
        if (a.layout.trackOffsets == null || b.layout.trackOffsets == null) return false;
        Vector3 ax = a.transform.right, az = a.transform.forward;
        Vector3 bx = b.transform.right, bz = b.transform.forward;
        float ahx = a.FootprintHalfWidth + margin * 0.5f, ahz = a.FootprintHalfLength + margin * 0.5f;
        float bhx = b.FootprintHalfWidth + margin * 0.5f, bhz = b.FootprintHalfLength + margin * 0.5f;
        Vector3 d = b.transform.position - a.transform.position;
        foreach (var axis in new[] { ax, az, bx, bz })
        {
            float ra = Mathf.Abs(Vector3.Dot(ax, axis)) * ahx + Mathf.Abs(Vector3.Dot(az, axis)) * ahz;
            float rb = Mathf.Abs(Vector3.Dot(bx, axis)) * bhx + Mathf.Abs(Vector3.Dot(bz, axis)) * bhz;
            if (Mathf.Abs(Vector3.Dot(d, axis)) > ra + rb) return false;
        }
        return true;
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

    // M2-D: ホーム縁(1本の物理線の片側)一覧。1線に最大2件(例: 3面2線の各線は
    // 両側にホーム縁を持つ)。番線番号・予約・閉塞・停止位置は引き続きtrack index
    // (StopTracks/PlatformCount等)を参照し、ホーム縁は乗降可否の判定にのみ使う
    public IReadOnlyList<StationLayout.PlatformEdge> PlatformEdges => layout.edges;

    public bool SetPlatformEdgeMode(int trackIndex, int side, StationLayout.PlatformEdgeMode mode)
    {
        for (int i = 0; i < layout.edges.Count; i++)
        {
            var e = layout.edges[i];
            if (e.trackIndex != trackIndex || e.side != side) continue;
            e.mode = mode;
            layout.edges[i] = e;
            return true;
        }
        return false;
    }

    // trackIndexに対応する全ホーム縁のうち、1つでも乗車(または降車)を許せば真。
    // 複数のホーム縁を個別に処理するのではなく、この1回の判定だけで乗降処理全体の
    // 実行有無を決める(同じ旅客・運賃・輸送実績を二重に計上しないため)
    public bool CanBoardAt(int trackIndex)
    {
        foreach (var e in layout.edges)
        {
            if (e.trackIndex != trackIndex) continue;
            if (StationLayout.AllowsBoard(e.mode)) return true;
        }
        // 実装後レビューでCodex CLIが指摘: この線にホーム縁が1つも無ければ乗降不可とする。
        // 実際のゲームプレイでcurTrackは常にStopTracks(=必ず1つ以上ホーム縁を持つ)の
        // 一部であるため通常は到達しないが、不正なtrackIndexを誤って許可しないための防御
        return false;
    }

    public bool CanAlightAt(int trackIndex)
    {
        foreach (var e in layout.edges)
        {
            if (e.trackIndex != trackIndex) continue;
            if (StationLayout.AllowsAlight(e.mode)) return true;
        }
        return false;
    }
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
        foreach (var sign in stationSigns)
            if (sign != null) sign.text = stationName;
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
