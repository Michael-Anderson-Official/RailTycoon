using System.Collections.Generic;
using UnityEngine;

// 駅。対応両数(cars)・面数(faces)・線数(lines)を持ち、メッシュは全て子に生成する。
// ローカル座標系: 駅軸=+z、横=+x。transformのY回転で向きを決める
public class Station : MonoBehaviour
{
    public int id; // M2-C: セーブ/ロードを跨いで安定な識別子。0は未割当(preview等)
    public int cars = 6, faces = 2, lines = 2;
    // 階。0=地上、1=2階、2=3階…(将来の地下は負の値)。実際の高さは
    // RailDimensions.HeightOfLevel(level)で、駅のtransform.position.yがこれになる。
    // 駅のメッシュは全てローカル座標で作るので、Yを上げるだけで駅ごと持ち上がる
    public int level;
    public string stationName = "駅";
    public bool preview;

    public float Height => RailDimensions.HeightOfLevel(level);

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

        // 階に応じた高さを常にここで確定させる。位置はBuildController/ロード側でも
        // 設定するが、建て替えや復元で取りこぼすと駅だけ地上へ落ちてしまう
        var pos = transform.position;
        transform.position = new Vector3(pos.x, Height, pos.z);

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
        var glass = new RailKit.MeshData();      // 待合室と駅舎のガラスを共用する
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
                    AddApronFence(metalwork, p.x, z0, z1, visualW * 0.5f, tipHalfW);
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

            // 折板屋根のリブと棟包み。俯瞰では上屋がホームをほぼ覆うので、
            // 屋根が単なる平板に見えないようここを作り込むと効果が大きい
            var rotL = Quaternion.Euler(0, 0, roofAngle);
            var rotR = Quaternion.Euler(0, 0, -roofAngle);
            for (int side = -1; side <= 1; side += 2)
            {
                var rot = side < 0 ? rotL : rotR;
                Vector3 slabC = new Vector3(p.x + side * roofW * 0.25f, roofY, 0);
                for (int i = -2; i <= 2; i++)
                    RailKit.AddBox(canopyRoof,
                        slabC + rot * new Vector3(i * (halfRoofW / 5f), 0.13f, 0),
                        new Vector3(0.09f, 0.10f, coveredLen), rot);
            }
            RailKit.AddBox(canopyRoof, new Vector3(p.x, roofY + rise * 0.5f + 0.06f, 0),
                new Vector3(0.46f, 0.16f, coveredLen), Quaternion.identity);

            // 上屋の妻面(端部の三角壁)と軒の雨樋。上屋が宙に浮いて見えるのを防ぐ
            for (int endSign = -1; endSign <= 1; endSign += 2)
            {
                float gz = endSign * coveredLen * 0.5f;
                RailKit.AddBox(canopyRoof, new Vector3(p.x, roofY - rise * 0.35f, gz),
                    new Vector3(roofW * 0.62f, rise * 1.5f, 0.12f), Quaternion.identity);
            }
            for (int sx = -1; sx <= 1; sx += 2)
                RailKit.AddBox(metalwork,
                    new Vector3(p.x + sx * (halfRoofW + 0.14f), roofY - rise * 0.55f, 0),
                    new Vector3(0.14f, 0.16f, coveredLen), Quaternion.identity);

            // 柱は実物と同じく細かい間隔で立てる(以前は18m間隔で骨組みが疎に見えた)。
            // 各柱に方杖(斜めの補強材)を入れて鋼製上屋らしくする
            float postMin = -coveredLen * 0.5f + 3f;
            float postMax = coveredLen * 0.5f - 3f;
            float postH = beamY - RailDimensions.PlatformTop;
            int lightEvery = 0;
            for (float z = postMin; z <= postMax + 0.01f; z += 9f)
            {
                RailKit.AddBox(metalwork,
                    new Vector3(p.x, (RailDimensions.PlatformTop + beamY) * 0.5f, z),
                    new Vector3(0.22f, postH, 0.22f), Quaternion.identity);
                RailKit.AddBox(metalwork, new Vector3(p.x, beamY, z),
                    new Vector3(roofW - 0.35f, 0.16f, 0.24f), Quaternion.identity);
                // 方杖。俯瞰ではほとんど見えないので柱1本おきに留める
                if (lightEvery % 2 == 0)
                    for (int sx = -1; sx <= 1; sx += 2)
                        RailKit.AddBox(metalwork,
                            new Vector3(p.x + sx * roofW * 0.16f, beamY - 0.42f, z),
                            new Vector3(0.1f, 1.15f, 0.1f), Quaternion.Euler(0, 0, sx * 38f));
                // 照明は柱2本おき(実物も柱ごとには付かない)
                if (lightEvery++ % 2 == 0)
                    RailKit.AddBox(lighting, new Vector3(p.x, beamY - 0.16f, z + 4.5f),
                        new Vector3(1.25f, 0.08f, 0.34f), Quaternion.identity);
            }

            // 設備を置ける横方向。線路に面している側には出さない(点字ブロック・
            // 警戒線の帯より内側に収める)。島式(両側が線路)は中央へ寄せる
            float bandInset = 1.35f;                       // 縁から帯を避けるのに要る幅
            float freeHalf = visualW * 0.5f - bandInset;
            // 片側だけが線路なら反対側へ、島式なら中央へ
            float furnX = p.x + FurnitureAwayDirection(layout, pi)
                * Mathf.Max(0f, freeHalf - 0.8f) * 0.5f;
            float top = RailDimensions.PlatformTop;

            // ベンチを等間隔に並べる(以前は中央付近に2脚だけだった)
            if (freeHalf > 0.7f)
            {
                int benchCount = Mathf.Clamp(Mathf.FloorToInt(platLen / 26f), 2, 8);
                for (int bi = 0; bi < benchCount; bi++)
                {
                    float z = Mathf.Lerp(-platLen * 0.36f, platLen * 0.36f,
                        benchCount == 1 ? 0.5f : bi / (float)(benchCount - 1));
                    RailKit.AddBox(furniture, new Vector3(furnX, top + 0.40f, z),
                        new Vector3(0.62f, 0.14f, 3.2f), Quaternion.identity);
                    RailKit.AddBox(furniture, new Vector3(furnX + 0.27f, top + 0.77f, z),
                        new Vector3(0.12f, 0.75f, 3.2f), Quaternion.identity);
                    for (int sz = -1; sz <= 1; sz += 2)
                        RailKit.AddBox(metalwork,
                            new Vector3(furnX, top + 0.17f, z + sz * 1.1f),
                            new Vector3(0.12f, 0.48f, 0.12f), Quaternion.identity);
                }
            }

            // 自動販売機とごみ箱。ホームの1/4付近へ1組ずつ
            if (freeHalf > 1.0f)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    float z = sz * platLen * 0.27f;
                    RailKit.AddBox(furniture, new Vector3(furnX, top + 0.92f, z),
                        new Vector3(0.78f, 1.84f, 1.05f), Quaternion.identity);
                    RailKit.AddBox(furniture, new Vector3(furnX, top + 0.42f, z + sz * 1.9f),
                        new Vector3(0.56f, 0.84f, 0.62f), Quaternion.identity);
                }

            // 待合室とエレベーター塔。上屋が覆わない範囲(±0.42*platLen)へ置くので
            // 俯瞰でも見え、のっぺりしたホームに立体的な変化が出る
            if (freeHalf > 1.3f && platLen > 90f)
            {
                // 待合室。中実の箱をガラスで包むと中身の壁が見えたままになるので、
                // 床・天井の帯と四隅の柱だけを実体にし、壁はガラス板で張る
                // (実装後レビューでCodex CLIが指摘)
                float wz = platLen * 0.42f;
                float ww = Mathf.Min(2.6f, freeHalf * 1.6f);
                const float wl = 4.2f, wh = 2.5f;
                RailKit.AddBox(metalwork, new Vector3(furnX, top + 0.07f, wz),
                    new Vector3(ww, 0.14f, wl), Quaternion.identity);
                RailKit.AddBox(metalwork, new Vector3(furnX, top + wh, wz),
                    new Vector3(ww, 0.16f, wl), Quaternion.identity);
                for (int cx = -1; cx <= 1; cx += 2)
                    for (int cz = -1; cz <= 1; cz += 2)
                        RailKit.AddBox(metalwork,
                            new Vector3(furnX + cx * (ww * 0.5f - 0.06f), top + wh * 0.5f,
                                wz + cz * (wl * 0.5f - 0.06f)),
                            new Vector3(0.12f, wh, 0.12f), Quaternion.identity);
                // 側面2枚と奥の妻面。手前は出入口として開けておく
                for (int cx = -1; cx <= 1; cx += 2)
                    RailKit.AddBox(glass,
                        new Vector3(furnX + cx * ww * 0.5f, top + wh * 0.5f, wz),
                        new Vector3(0.05f, wh - 0.3f, wl - 0.2f), Quaternion.identity);
                RailKit.AddBox(glass, new Vector3(furnX, top + wh * 0.5f, wz + wl * 0.5f),
                    new Vector3(ww - 0.2f, wh - 0.3f, 0.05f), Quaternion.identity);
                RailKit.AddBox(canopyRoof, new Vector3(furnX, top + wh + 0.18f, wz),
                    new Vector3(ww + 0.4f, 0.16f, wl + 0.4f), Quaternion.identity);

                // エレベーター塔。昇降路は中実のままにし、ガラスは面に張る窓とする
                float ez = -platLen * 0.42f;
                float ew = Mathf.Min(2.4f, freeHalf * 1.5f);
                RailKit.AddBox(metalwork, new Vector3(furnX, top + 2.05f, ez),
                    new Vector3(ew, 4.1f, ew), Quaternion.identity);
                for (int cz = -1; cz <= 1; cz += 2)
                    RailKit.AddBox(glass,
                        new Vector3(furnX, top + 1.9f, ez + cz * (ew * 0.5f + 0.03f)),
                        new Vector3(ew * 0.62f, 2.4f, 0.05f), Quaternion.identity);
                RailKit.AddBox(canopyRoof, new Vector3(furnX, top + 4.22f, ez),
                    new Vector3(ew + 0.35f, 0.16f, ew + 0.35f), Quaternion.identity);
            }

            // 上屋の外は暗くなるので照明ポールを立てる
            for (int sz = -1; sz <= 1; sz += 2)
            {
                float lz = sz * platLen * 0.46f;
                RailKit.AddBox(metalwork, new Vector3(p.x, top + 1.85f, lz),
                    new Vector3(0.12f, 3.7f, 0.12f), Quaternion.identity);
                RailKit.AddBox(lighting, new Vector3(p.x, top + 3.66f, lz),
                    new Vector3(0.62f, 0.1f, 0.3f), Quaternion.identity);
            }

            // 階段(コンコースへの昇降口)。上屋の下、ホーム中央付近に置く。
            // 実際に床を抜くのではなく、昇降口の壁と手すりで表現する
            if (freeHalf > 1.2f && platLen > 60f)
            {
                float stairHalfW = Mathf.Min(1.7f, freeHalf * 0.85f);
                const float stairHalfLen = 5.2f;
                float wallY = top + 0.55f;
                for (int sx = -1; sx <= 1; sx += 2)
                    RailKit.AddBox(metalwork,
                        new Vector3(furnX + sx * stairHalfW, wallY, 0),
                        new Vector3(0.16f, 1.1f, stairHalfLen * 2f), Quaternion.identity);
                RailKit.AddBox(metalwork, new Vector3(furnX, wallY, -stairHalfLen),
                    new Vector3(stairHalfW * 2f, 1.1f, 0.16f), Quaternion.identity);
                // ホーム面は不透明な一枚板なので、その下へ段板を描いても見えない
                // (実装後レビューでCodex CLIが指摘)。開口を面のすぐ上に濃い板で表し、
                // その上に段鼻を並べて「下りていく階段」に見せる
                RailKit.AddBox(metalwork, new Vector3(furnX, top + 0.012f, 0f),
                    new Vector3(stairHalfW * 1.9f, 0.02f, stairHalfLen * 1.8f),
                    Quaternion.identity);
                for (int si = 0; si < 7; si++)
                    RailKit.AddBox(tactile,
                        new Vector3(furnX, top + 0.03f,
                            Mathf.Lerp(-stairHalfLen + 0.7f, stairHalfLen - 0.7f, si / 6f)),
                        new Vector3(stairHalfW * 1.9f, 0.02f, 0.16f), Quaternion.identity);
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

            // 上屋から吊り下げる番線標。実物と同じく乗車位置の目印になるよう
            // 上屋の範囲の両端寄りへ、線路に面している側だけに出す
            foreach (var e in layout.edges)
            {
                if (e.platformIndex != pi) continue;
                // 線路は -e.side 側。番線標は線路側の縁寄りに吊る
                float hangX = p.x - e.side * Mathf.Min(1.0f, visualW * 0.5f - 0.5f);
                for (int hi = -1; hi <= 1; hi += 2)
                {
                    float hz = hi * coveredLen * 0.3f;
                    RailKit.AddBox(metalwork, new Vector3(hangX, beamY - 0.34f, hz),
                        new Vector3(0.06f, 0.52f, 0.06f), Quaternion.identity);
                    RailKit.AddBox(signBoard, new Vector3(hangX, beamY - 0.72f, hz),
                        new Vector3(0.05f, 0.34f, 1.15f), Quaternion.identity);
                }
            }
        }

        // 駅舎は壁・庇・ガラス出入口・方立を分け、単なる箱に見えないようにする。
        var house = new RailKit.MeshData();
        float houseX = layout.totalWidth * 0.5f + 6.5f;
        RailKit.AddBox(house, new Vector3(houseX, 2.35f, 0), new Vector3(10f, 4.7f, 8.5f), Quaternion.identity);
        RailKit.AddBox(canopyRoof, new Vector3(houseX, 4.78f, 0), new Vector3(11.2f, 0.28f, 9.5f), Quaternion.identity);
        RailKit.AddBox(canopyRoof, new Vector3(houseX + 5.25f, 3.75f, 0), new Vector3(1.8f, 0.18f, 6.4f), Quaternion.identity);
        RailKit.AddBox(glass, new Vector3(houseX + 5.02f, 2.15f, 0), new Vector3(0.12f, 2.9f, 5.6f), Quaternion.identity);
        for (int mullion = -2; mullion <= 2; mullion++)
            RailKit.AddBox(metalwork, new Vector3(houseX + 5.12f, 2.15f, mullion * 1.35f),
                new Vector3(0.1f, 3.0f, 0.1f), Quaternion.identity);

        AddStopMarkers(metalwork, signBoard);

        // 高架駅は桁と橋脚で地面まで支える。ローカルy=0がレール基面なので、
        // 桁はその直下、橋脚はさらに地面(ローカルy=-Height)まで下ろす
        if (level != 0)
        {
            var viaduct = new RailKit.MeshData();
            AddViaduct(viaduct, H, T);
            RailKit.MeshGO("Viaduct", viaduct.ToMesh(), MatLib.Get("Platform"), transform);
        }

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
        // スマホでのタップを外しにくいよう実寸よりだいぶ大きめに取る。
        // ただし高架でも橋脚ぶん下へ伸ばしてはいけない。立体交差では上を向いた
        // レイが先に高架駅へ当たり、真下の地上駅が一切選べなくなる
        // (実装後レビューでCodex CLIが指摘)
        col.center = new Vector3(0, 5, 0);
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
    // ホーム端の直前に置く押さえ点までの距離。Chaikin(2回)の角の丸めが影響する
    // 範囲は隣接区間長のおよそ3/4なので、この長さを短くするほど収束の滲み出しが
    // ホーム側へ届かなくなる
    public const float PlatformEndHold = 3f;

    // RebuildTrackVisualが実際に描画へ渡した線ごとの中心線(ローカル、平滑化済み)。
    // 走行経路もここから切り出すので、レールと列車の通り道は同じ実体になる
    List<Vector3>[] trackCentres;

    // 線trackの中心線(ローカル)。未生成なら生成しておく
    public List<Vector3> TrackCentreLocal(int track)
    {
        if (trackCentres == null || track < 0 || track >= trackCentres.Length) return null;
        return trackCentres[track];
    }

    // 中心線のうち、ローカルzがzFrom→zToの区間だけを切り出す(両端は補間して正確に合わせる)。
    // zFrom>zToなら逆向き(=-z方向へ進む列車)の順で返す。
    // 中心線はzについて単調に並んでいる前提(TrackVisualPathがそう作っている)
    public static List<Vector3> PortionByZ(List<Vector3> pts, float zFrom, float zTo)
    {
        var outPts = new List<Vector3>();
        if (pts == null || pts.Count < 2) return outPts;
        float lo = Mathf.Min(zFrom, zTo), hi = Mathf.Max(zFrom, zTo);
        Vector3? loPt = null, hiPt = null;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            Vector3 p = pts[i], q = pts[i + 1];
            if (Mathf.Approximately(p.z, q.z)) continue;
            float tLo = (lo - p.z) / (q.z - p.z);
            if (tLo >= 0f && tLo <= 1f && loPt == null) loPt = Vector3.Lerp(p, q, tLo);
            float tHi = (hi - p.z) / (q.z - p.z);
            if (tHi >= 0f && tHi <= 1f) hiPt = Vector3.Lerp(p, q, tHi);
        }
        if (loPt.HasValue) outPts.Add(loPt.Value);
        foreach (var p in pts) if (p.z > lo && p.z < hi) outPts.Add(p);
        if (hiPt.HasValue) outPts.Add(hiPt.Value);
        if (zFrom > zTo) outPts.Reverse();
        return outPts;
    }

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
        // ホーム端の直前に「押さえ」の点を入れる。この経路は最後にChaikinで平滑化
        // されるが、ホーム区間は非常に長い(編成長ぶん)ため、押さえが無いとスロートの
        // 収束がホーム区間まで遡って滲み出し、道床がホームへ食い込む
        // (3線以上かつ端が接続されている駅で発生。4面8線では3.25mも食い込んでいた)。
        // 端の直前に短い区間を作ることで、角の丸めをスロート側だけに閉じ込める
        pts.Add(new Vector3(off, 0, -H));            // ホーム端
        pts.Add(new Vector3(off, 0, -H + PlatformEndHold));
        pts.Add(new Vector3(off, 0, H - PlatformEndHold));
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

        // 描画に使った中心線をそのまま保持する。列車の走行経路(Train.BuildLeg)も
        // これを切り出して使うため、レールと列車の通り道が原理的にズレない
        // (以前は同じ形を別々に組んでいたため、片方だけ直すと列車がレールから浮いた)
        trackCentres = new List<Vector3>[layout.trackOffsets.Length];
        for (int i = 0; i < layout.trackOffsets.Length; i++)
        {
            trackCentres[i] = RailKit.Chaikin(TrackVisualPath(i, conn[0], conn[1]), 2);
            RailKit.AddTrack(ballast, rail, tie, trackCentres[i],
                TrackBedType.Ballast, null, RailDimensions.StationBedHalfWidth);
        }

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

    // 高架の桁と橋脚(駅ローカル)。桁はスロート端まで通し、橋脚は一定間隔で
    // 構内幅の左右2列に立てる。地上駅(level==0)では呼ばない
    void AddViaduct(RailKit.MeshData md, float H, float T)
    {
        float deck = RailDimensions.ViaductDeckThickness;
        float drop = Height;                       // レール基面から地面までの落差
        float w = layout.totalWidth + 2f;          // 構内幅より少し広く張り出す
        float halfW = w * 0.5f;
        // スロート端では線路が中心へ収束しているので、桁もそこまで絞る。
        // 全幅のまま伸ばすと、車止めの先まで何も載っていない板が突き出して見える
        // 駅間の桁(TrackSegment.AddViaduct)と同じ幅にして、接合部に段差を出さない
        float tipHalf = Mathf.Min(halfW, TrackSegment.HalfCorridorWidth);

        // ホーム部は全幅
        RailKit.AddBox(md, new Vector3(0f, -deck * 0.5f, 0f),
            new Vector3(w, deck, H * 2f), Quaternion.identity);
        // スロート部は線路の収束に合わせて絞る
        for (int s = -1; s <= 1; s += 2)
            AddTaperedApron(md, 0f, s * H, s * (H + T), halfW, tipHalf, -deck, 0f);

        // 橋脚。地上へ届かない高さ(level==0)なら不要
        float pierTop = -deck;
        float pierBottom = -drop;
        float pierH = pierTop - pierBottom;
        if (pierH <= 0.1f) return;
        float pw = RailDimensions.ViaductPierWidth;
        float end = H + T;
        int rows = Mathf.Max(2, Mathf.CeilToInt(end * 2f / RailDimensions.ViaductPierSpacing));
        for (int i = 0; i <= rows; i++)
        {
            float z = Mathf.Lerp(-end + pw, end - pw, i / (float)rows);
            // その位置での桁の幅に合わせて橋脚を内側へ寄せる(桁から食み出させない)
            float local = Mathf.Abs(z) <= H
                ? halfW
                : Mathf.Lerp(halfW, tipHalf, (Mathf.Abs(z) - H) / Mathf.Max(0.01f, T));
            float px = Mathf.Max(pw * 0.5f, local - 2f);
            for (int sx = -1; sx <= 1; sx += 2)
                RailKit.AddBox(md, new Vector3(sx * px, pierBottom + pierH * 0.5f, z),
                    new Vector3(pw, pierH, pw), Quaternion.identity);
        }
    }

    // ---- 停止位置目標 ----
    // 運転士がここへ先頭を合わせて停める標識。前面展望で「画面下端に来たら停止」
    // という目安になるよう、車窓カメラの幾何から逆算した位置に置く。
    //
    // 車窓カメラ(Train.CabPose)は鼻先の2.2m前・レール面から3.46m
    // (BogieRootY + TrainVisual.CabEyeLocalY)の高さで、CameraRigが約3.4°下を向ける。
    // 垂直画角はCameraの既定60°。実機は縦画面(402×874)で水平画角が約30°しかないため、
    // 線路脇に人の背丈で立てると画面下端へ来る前に横へ切れてしまう。
    // そのため実物の低い停止位置目標と同じく、低く・線路寄りに置く
    // 横方向は2つの制約の間に収める必要がある。内側=レールへ重ねない(板の内端が
    // 軌間の外)、外側=縦画面の車窓の横画角(約30°)から外さない
    const float MarkerLateral = 1.00f;   // 線路中心から(車体の下に収まる)
    const float MarkerPlateW = 0.34f;    // 板の幅
    const float MarkerPlateY = 0.55f;    // 標識板の中心高さ(レール面から)
    // 停止時の鼻先から標識までの距離。実機(402×874)の車窓へ投影して測った値で、
    // 板の下端が画面高の1.5%、上端が7.2%に来る=画面下端にちょうど板が収まる
    const float MarkerAhead = 7.0f;

    void AddStopMarkers(RailKit.MeshData post, RailKit.MeshData plate)
    {
        // 建設プレビューには要らない。両数ぶんのTextMeshを毎回作り直すのは無駄
        if (preview || layout.stopTracks == null) return;
        foreach (int t in layout.stopTracks)
        {
            float tx = layout.trackOffsets[t];
            foreach (int n in SupportedFormationCars())
            {
                // 先頭がここへ来る(Train.HalfTrainと同じ規約)
                float nose = n * StationLayout.CarLength * 0.5f;
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    float z = sign * (nose + MarkerAhead);
                    if (Mathf.Abs(z) > HalfLen + StationLayout.ThroatLen * 0.5f) continue;
                    // 日本の左側通行に合わせ、運転台のある進行方向左側へ置く
                    float x = tx - sign * MarkerLateral;
                    RailKit.AddBox(post, new Vector3(x, MarkerPlateY * 0.5f, z),
                        new Vector3(0.09f, MarkerPlateY, 0.09f), Quaternion.identity);
                    // 板は進行方向の逆(近づいてくる列車)へ向ける
                    RailKit.AddBox(plate, new Vector3(x, MarkerPlateY, z),
                        new Vector3(MarkerPlateW, 0.30f, 0.05f), Quaternion.identity);
                    // 両数を書き入れる。複数の停止位置が並ぶので、番号が無いと
                    // どれが自分の位置か分からない
                    CreateStopMarkerText(n, t, sign,
                        new Vector3(x, MarkerPlateY, z - sign * 0.04f));
                }
            }
        }
    }

    // 停止位置目標の両数表示。近づいてくる列車(進行方向の逆)へ向ける
    void CreateStopMarkerText(int carCount, int track, int sign, Vector3 localPosition)
    {
        var go = new GameObject("StopMarkerText_" + track + "_" + sign + "_" + carCount);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPosition;
        // TextMeshは-z側から読める向きに出るので、+z進行(=-z側から見る)は回転0
        go.transform.localRotation = Quaternion.Euler(0, sign > 0 ? 0f : 180f, 0);
        var tm = go.AddComponent<TextMesh>();
        tm.font = MatLib.JpFont;
        tm.text = carCount.ToString();
        tm.fontSize = 64;
        tm.characterSize = 0.040f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.white;
        go.GetComponent<MeshRenderer>().sharedMaterial = MatLib.JpFont.material;
    }

    // この駅に停まれる編成の両数(重複を除く)
    List<int> SupportedFormationCars()
    {
        var list = new List<int>();
        foreach (var f in TrainCatalog.Formations)
            if (f.cars <= cars && !list.Contains(f.cars)) list.Add(f.cars);
        list.Sort();
        return list;
    }

    // ベンチ・待合室などの設備を線路から逃がす向き(ホームローカルのx)。
    // ホーム縁は centerX - side*幅/2 にあるので、**線路は -side 側**にある。
    // つまり逃がす向きは +side。符号を取り違えると設備が線路側へ寄ってしまう
    // (実装後レビューでCodex CLIが指摘)。両側が線路の島式は0(中央のまま)。
    // Mathf.Signは0に対して1を返すので、島式をそれで判定してはいけない
    public static int FurnitureAwayDirection(StationLayout.Result layout, int platformIndex)
    {
        int sum = 0;
        foreach (var e in layout.edges) if (e.platformIndex == platformIndex) sum += e.side;
        return sum == 0 ? 0 : (sum > 0 ? 1 : -1);
    }

    // 絞ったホーム端を囲む柵。実物のホーム端は必ず柵で囲われており、
    // 白い斜路が剥き出しのままだと模型のように見えてしまう。
    // 柵はホーム縁より内側に置き、線路側へは張り出させない
    static void AddApronFence(RailKit.MeshData md, float centerX, float z0, float z1,
        float halfW0, float halfW1)
    {
        const int steps = 4;
        float top = RailDimensions.PlatformTop;
        var prev = new Vector3[2];
        for (int s = 0; s <= steps; s++)
        {
            float t = s / (float)steps;
            float z = Mathf.Lerp(z0, z1, t);
            float hw = Mathf.Lerp(halfW0, halfW1, t) - 0.14f;
            for (int i = 0; i < 2; i++)
            {
                float x = centerX + (i == 0 ? -hw : hw);
                var pt = new Vector3(x, 0f, z);
                RailKit.AddBox(md, new Vector3(x, top + 0.62f, z),
                    new Vector3(0.1f, 1.15f, 0.1f), Quaternion.identity);
                if (s > 0)
                {
                    // 前の柱との間に手すり2段。絞りに沿って斜めになる
                    Vector3 mid = (prev[i] + pt) * 0.5f;
                    Vector3 d = pt - prev[i];
                    float len = new Vector2(d.x, d.z).magnitude;
                    var rot = Quaternion.Euler(0, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 0);
                    for (int rail = 0; rail < 2; rail++)
                        RailKit.AddBox(md,
                            new Vector3(mid.x, top + 0.55f + rail * 0.5f, mid.z),
                            new Vector3(0.07f, 0.08f, len), rot);
                }
                prev[i] = pt;
            }
        }
        // 先端を塞ぐ手すり
        float tipHw = halfW1 - 0.14f;
        for (int rail = 0; rail < 2; rail++)
            RailKit.AddBox(md, new Vector3(centerX, top + 0.55f + rail * 0.5f, z1),
                new Vector3(tipHw * 2f, 0.08f, 0.07f), Quaternion.identity);
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
        // 高さが十分違えば、平面上は重なっていても当たらない(高架下・跨線)
        if (Mathf.Abs(local.y) >= RailDimensions.LevelClearance) return false;
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
        if (Mathf.Abs(local.y) >= RailDimensions.LevelClearance) return false;
        return Mathf.Abs(local.x) <= FootprintHalfWidth + margin
            && Mathf.Abs(local.z) <= FootprintHalfLength + margin;
    }

    // 2駅の占有矩形(それぞれ任意の向き)が平面上で重なるか。分離軸定理で厳密に見る
    // (4隅の内包判定だけでは、十字に交差する配置を取りこぼすため)
    public static bool FootprintsOverlap(Station a, Station b, float margin)
    {
        if (a == null || b == null) return false;
        if (a.layout.trackOffsets == null || b.layout.trackOffsets == null) return false;
        // 階が違って十分な高低差があれば、平面が重なっていても干渉しない(立体交差)
        if (Mathf.Abs(a.transform.position.y - b.transform.position.y) >= RailDimensions.LevelClearance)
            return false;
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
