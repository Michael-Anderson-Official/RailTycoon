using UnityEngine;

// 先頭車の運転台(内装)。前面展望を運転士目線にしたので、画面下に映る。
// 車体ローカルで組む(車体の子なのでPlaceCarsStaticがそのまま運ぶ)。
//
// **運転席は左**(日本の鉄道の一般)。前面側の貫通扉・助士席は右になる。
//
// 実車の運転台(ユーザー提供の写真)に合わせた構成:
//   幅いっぱいのコンソール天板 → その奥に少し起きた背面パネル →
//   背面パネルに表示器ユニットが横に並ぶ(左から 速度計/モニタ/液晶/液晶) →
//   天板の左手前にT字形ワンハンドル(ノッチ表示板つき) → 天板に押しボタン群
//
// 形式ごとの実車写真は手元に無いため、**世代による作り分け**にとどめる
// (ユーザーと合意済み)。実車どおりにしたい形式は資料をもらって個別に直す。
public static class TrainCab
{
    // 運転台の世代
    public enum Style
    {
        OneHandleLcd,  // T字形ワンハンドル + 液晶モニタ(新しい車両)
        OneHandle,     // ワンハンドル + 計器盤
        TwoHandle,     // マスコン(左) + ブレーキ弁(右) + アナログ計器(古い車両)
    }

    // 車体ローカルの寸法。
    // **この車両モデルは前面窓が高い**。実車の床からの寸法をそのまま使うと
    // 目線が窓の下に潜って前が見えないので、運転台一式は「窓から見て自然に
    // 見える位置」を基準に組む(目線→天板→座席の上下関係を保つ)
    public const float FloorY = RailDimensions.VehicleFloorLocalY;   // 床面(参考)
    public const float FrontZ = 9.7f;        // 前面(車体半長)
    public const float BulkheadZ = 7.3f;     // 客室との仕切り

    // 運転士の着席目線(車体ローカル)。CabPoseがこれを使う。
    // **縦画面は水平画角が約30°しかない**。コンソールに近すぎると横に数十cmしか
    // 見えず、実車のように表示器を並べても1つも画面に入らない。目線はやや後ろに
    // 取って視野の幅を稼ぐ(FrontZ-1.55だと表示器が1つも入らなかった)
    public const float EyeX = -0.55f;
    public const float EyeY = 2.95f;         // 前面窓の中ほど
    public const float EyeZ = FrontZ - 1.80f;

    // 天板の高さ。**下げるほど窓から見える範囲が広がる**(コンソールの上端が
    // 画面の下へ移る)。実車の目線-天板は0.55〜0.65mだが、縦画面では前方が
    // 狭くなりすぎるので少し低め(0.68m)に取る
    public const float DeskTopY = EyeY - 0.68f;
    public const float CabFloorY = DeskTopY - 0.85f;
    public const float SeatZ = EyeZ;                 // 座面の真上に目線
    const float DeskFrontZ = 8.90f;                  // 天板の手前端
    const float DeskBackZ = 9.50f;                   // 天板の奥端
    const float PanelTiltDeg = -20f;                 // 背面パネルの起こし角

    // 傾いた面の上方向・法線(= Rx(PanelTiltDeg) を掛けた +Y と -Z)。
    // **表示器を「z方向へ少し手前」に置くだけでは駄目**で、面が傾いているぶん
    // ユニットの中に埋もれる(高い位置ほど深く沈む)。必ずこの向きで押し出す
    const float PodUpY = 0.940f, PodUpZ = -0.342f;
    const float PodOutY = -0.342f, PodOutZ = -0.940f;

    // 表示器ユニット(左から)。運転士(x=-0.55)の正面に速度計とモニタが並ぶよう置く
    const float PodY = DeskTopY + 0.19f;
    const float PodZ = 9.36f;
    const float PodH = 0.34f;      // ユニットの高さ
    const float PodW = 0.40f;      // 速度計・モニターのユニット幅
    const float LcdPodW = 0.44f;   // 液晶のユニット幅
    public const float PodHalfDepth = 0.07f;      // 筐体の奥行きの半分
    public const float PodFaceOffset = 0.08f;     // 面から手前へ押し出す量
    const float SpeedoX = -0.80f;
    public const float MonitorX = -0.38f;
    const float Lcd1X = 0.08f;
    const float Lcd2X = 0.56f;

    // ドアモニターの画面(表示器ユニットの中にはめ込む)。
    // 縦横比はRenderTexture(256×160)に合わせる。ずらすと映像が伸びる
    public const float MonitorY = PodY + (PodFaceOffset + 0.005f) * PodOutY;
    public const float MonitorZ = PodZ + (PodFaceOffset + 0.005f) * PodOutZ;
    public const float MonitorW = 0.28f, MonitorH = 0.175f;

    public static Style StyleOf(TrainCatalog.TrainTypeDef t) => t.cabStyle;

    // 運転台一式を車体の子として作る。戻り値はモニター画面のTransform
    public static Transform Build(Transform car, TrainCatalog.TrainTypeDef t)
    {
        var dark = new RailKit.MeshData();     // 内装の暗色(仕切り・座席・床)
        var panel = new RailKit.MeshData();    // コンソール本体
        var pod = new RailKit.MeshData();      // 表示器ユニットの筐体
        var metal = new RailKit.MeshData();    // ハンドル・手すり
        var screen = new RailKit.MeshData();   // 計器・液晶の面
        var tilt = Quaternion.Euler(PanelTiltDeg, 0, 0);

        // ---- 仕切り・床・座席 ----
        RailKit.AddBox(dark, new Vector3(-1.05f, CabFloorY + 1.4f, BulkheadZ),
            new Vector3(1.3f, 2.8f, 0.1f), Quaternion.identity);
        RailKit.AddBox(dark, new Vector3(1.15f, CabFloorY + 1.4f, BulkheadZ),
            new Vector3(1.1f, 2.8f, 0.1f), Quaternion.identity);
        RailKit.AddBox(dark, new Vector3(0.15f, CabFloorY + 2.55f, BulkheadZ),
            new Vector3(1.0f, 0.5f, 0.1f), Quaternion.identity);
        RailKit.AddBox(dark, new Vector3(0, CabFloorY - 0.03f, (FrontZ + BulkheadZ) * 0.5f),
            new Vector3(2.7f, 0.06f, FrontZ - BulkheadZ), Quaternion.identity);
        RailKit.AddBox(dark, new Vector3(EyeX, CabFloorY + 0.44f, SeatZ),
            new Vector3(0.5f, 0.1f, 0.46f), Quaternion.identity);
        RailKit.AddBox(dark, new Vector3(EyeX, CabFloorY + 0.74f, SeatZ - 0.28f),
            new Vector3(0.5f, 0.6f, 0.08f), Quaternion.identity);

        // ---- コンソール ----
        float deskMidZ = (DeskFrontZ + DeskBackZ) * 0.5f;
        float deskDepth = DeskBackZ - DeskFrontZ;
        RailKit.AddBox(panel, new Vector3(0, DeskTopY - 0.36f, deskMidZ),
            new Vector3(2.66f, 0.72f, deskDepth), Quaternion.identity);   // 台座
        RailKit.AddBox(panel, new Vector3(0, DeskTopY, deskMidZ),
            new Vector3(2.7f, 0.07f, deskDepth + 0.04f), Quaternion.identity);  // 天板
        RailKit.AddBox(panel, new Vector3(0, PodY, PodZ + 0.09f),
            new Vector3(2.7f, PodH + 0.12f, 0.1f), tilt);                 // 奥の背面パネル

        // ---- 表示器ユニット ----
        // 速度計(丸型2つ+小さな表示)。実車どおり運転士の正面やや左
        AddPod(pod, panel, SpeedoX, PodW);
        // 丸型計器は**ユニットの上寄り**に置く。下に置くとマスコンのグリップに隠れる
        for (int k = -1; k <= 1; k += 2)
            AddOnPod(screen, SpeedoX + k * 0.09f, 0.055f, new Vector3(0.15f, 0.15f, 0.02f));
        AddOnPod(screen, SpeedoX, -0.11f, new Vector3(0.28f, 0.07f, 0.02f));

        // ドアモニターのユニット(運転士の正面やや右)
        AddPod(pod, panel, MonitorX, PodW);

        // 液晶(世代による)。縦画面ではほぼ映らないが、外から見たときの体裁として置く
        if (t.cabStyle != Style.TwoHandle)
        {
            AddPod(pod, panel, Lcd1X, LcdPodW);
            AddOnPod(screen, Lcd1X, 0f, new Vector3(0.36f, 0.24f, 0.02f));
            if (t.cabStyle == Style.OneHandleLcd)
            {
                AddPod(pod, panel, Lcd2X, LcdPodW);
                AddOnPod(screen, Lcd2X, 0f, new Vector3(0.36f, 0.24f, 0.02f));
            }
        }

        // ---- 主幹制御器(マスコン) ----
        // **画面に映る位置**へ寄せる。実車の位置(運転士の左脇)は水平画角30°の
        // 外へ出てしまうので、速度計の真下=左手の届く範囲の内側に置く
        float handleZ = DeskFrontZ + 0.16f;
        if (t.cabStyle == Style.TwoHandle)
        {
            // マスコン(左)とブレーキ弁(右)。丸ハンドルを2つ
            foreach (float hx in new[] { -0.90f, -0.28f })
            {
                RailKit.AddBox(metal, new Vector3(hx, DeskTopY + 0.09f, handleZ),
                    new Vector3(0.2f, 0.12f, 0.2f), Quaternion.identity);
                RailKit.AddBox(metal, new Vector3(hx, DeskTopY + 0.18f, handleZ),
                    new Vector3(0.30f, 0.05f, 0.06f), Quaternion.Euler(0, 22f, 0));
            }
        }
        else
        {
            // T字形ワンハンドル。台座 → 縦の柱 → 横のグリップ。
            // 実車と同じく速度計の下へ少しかかる高さに収める(高くすると計器を隠す)
            const float hx = -0.90f;
            RailKit.AddBox(metal, new Vector3(hx, DeskTopY + 0.05f, handleZ),
                new Vector3(0.26f, 0.06f, 0.30f), Quaternion.identity);
            RailKit.AddBox(metal, new Vector3(hx, DeskTopY + 0.14f, handleZ),
                new Vector3(0.07f, 0.20f, 0.07f), Quaternion.identity);
            RailKit.AddBox(metal, new Vector3(hx, DeskTopY + 0.21f, handleZ),
                new Vector3(0.30f, 0.06f, 0.08f), Quaternion.identity);
            // ノッチ表示板(実車の P1..P5 / B1..B7 の目盛)
            RailKit.AddBox(screen, new Vector3(hx, DeskTopY + 0.055f, handleZ - 0.19f),
                new Vector3(0.24f, 0.02f, 0.20f), Quaternion.identity);
        }

        // ---- 天板の押しボタン・スイッチ ----
        for (int k = 0; k < 2; k++)
            RailKit.AddBox(screen, new Vector3(-0.30f + k * 0.28f, DeskTopY + 0.05f, DeskFrontZ + 0.18f),
                new Vector3(0.22f, 0.03f, 0.13f), Quaternion.identity);
        for (int k = -1; k <= 1; k++)
            RailKit.AddBox(metal, new Vector3(0.34f + k * 0.12f, DeskTopY + 0.06f, DeskFrontZ + 0.12f),
                new Vector3(0.05f, 0.05f, 0.05f), Quaternion.identity);

        // ---- 窓の内側のピラー ----
        foreach (float px in new[] { -1.32f, 1.32f })
            RailKit.AddBox(dark, new Vector3(px, CabFloorY + 1.6f, FrontZ - 0.15f),
                new Vector3(0.12f, 1.9f, 0.12f), Quaternion.identity);

        RailKit.MeshGO("CabInterior", dark.ToMesh(), MatLib.Get("TrainUnder"), car);
        RailKit.MeshGO("CabPanel", panel.ToMesh(),
            MatLib.Tinted("TrainPanto", new Color(0.80f, 0.81f, 0.78f)), car);
        RailKit.MeshGO("CabPod", pod.ToMesh(),
            MatLib.Tinted("TrainPanto", new Color(0.62f, 0.64f, 0.62f)), car);
        RailKit.MeshGO("CabMetal", metal.ToMesh(), MatLib.Get("Rail"), car);
        // 計器・液晶は**陰影のつかない**マテリアルで出す。運転士の方を向いた面には
        // 光が当たらないので、通常のマテリアルだと暗くて何も読めない
        // (2026-07-27にドアモニターで同じ問題を踏んだ)。実車の表示器も自光式
        RailKit.MeshGO("CabScreen", screen.ToMesh(),
            MatLib.Tinted("SpritesDefault", new Color(0.80f, 0.86f, 0.81f)), car);

        // ---- ドアモニターの画面。映像はTrainがRenderTextureで流し込む ----
        var monitor = GameObject.CreatePrimitive(PrimitiveType.Quad);
        monitor.name = "DoorMonitor";
        Object.DestroyImmediate(monitor.GetComponent<Collider>());
        monitor.transform.SetParent(car, false);
        monitor.transform.localPosition = new Vector3(MonitorX, MonitorY, MonitorZ);
        // 傾きは表示器ユニットと同じ。180°回すのは面を運転士へ向けるため
        monitor.transform.localRotation = Quaternion.Euler(PanelTiltDeg, 180f, 0);
        monitor.transform.localScale = new Vector3(MonitorW, MonitorH, 1f);
        var mr = monitor.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // 映像が来る前(車窓モード以外)は消灯した画面に見せる
        mr.sharedMaterial = MatLib.Tinted("SpritesDefault", new Color(0.10f, 0.12f, 0.11f));

        return monitor.transform;
    }

    // 表示器ユニット1個。筐体(pod)と、上下の縁(panel側の色)で角の落ちた箱に見せる
    static void AddPod(RailKit.MeshData body, RailKit.MeshData bezel, float x, float w)
    {
        var tilt = Quaternion.Euler(PanelTiltDeg, 0, 0);
        RailKit.AddBox(body, new Vector3(x, PodY, PodZ), new Vector3(w, PodH, PodHalfDepth * 2f), tilt);
        for (int k = -1; k <= 1; k += 2)
        {
            float h = k * (PodH * 0.5f + 0.02f);
            RailKit.AddBox(bezel, PodPoint(x, h, 0.01f),
                new Vector3(w + 0.03f, 0.05f, 0.16f), tilt);
        }
    }

    // 表示器ユニットの面にはめ込む(hはユニット中心からの高さ)
    static void AddOnPod(RailKit.MeshData md, float x, float h, Vector3 size) =>
        RailKit.AddBox(md, PodPoint(x, h, PodFaceOffset), size, Quaternion.Euler(PanelTiltDeg, 0, 0));

    // コンソールの見かけの上端(いちばん高く、いちばん奥の点=背面パネルの上後端)。
    // 車窓では**ここより上にしか前方が見えない**。停車位置目標がこれに隠れないか、
    // 前方の見通しが残っているかは、この点を基準に測る
    public static Vector3 ConsoleTopLocal
    {
        get
        {
            float h = (PodH + 0.12f) * 0.5f;   // 背面パネルの半分の高さ
            const float d = 0.05f;             // 背面パネルの半分の厚み(奥向き)
            return new Vector3(0,
                PodY + h * PodUpY - d * PodOutY,
                PodZ + 0.09f + h * PodUpZ - d * PodOutZ);
        }
    }

    // 表示器ユニットの中心と、運転士へ向く法線(テストから検証できるよう公開)
    public static Vector3 PodCentre(float x) => new Vector3(x, PodY, PodZ);
    public static Vector3 PodOutward => new Vector3(0, PodOutY, PodOutZ);
    public static float PodHeight => PodH;

    // 傾いた面の上にある点(h=面に沿った高さ, d=面から手前への押し出し)
    public static Vector3 PodPoint(float x, float h, float d) => new Vector3(
        x,
        PodY + h * PodUpY + d * PodOutY,
        PodZ + h * PodUpZ + d * PodOutZ);
}
