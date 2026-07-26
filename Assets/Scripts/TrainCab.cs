using UnityEngine;

// 先頭車の運転台(内装)。前面展望を運転士目線にしたので、画面下半分に映る。
// 車体ローカルで組む(車体の子なのでPlaceCarsStaticがそのまま運ぶ)。
//
// **運転席は左**(日本の鉄道の一般)。前面側の貫通扉・助士席は右になる。
//
// 形式ごとの実車写真は手元に無いため、**世代による一般的な配置**で作り分ける
// (ユーザーと合意済み)。実車どおりにしたい形式が出てきたら、資料をもらって
// その形式だけ個別に直す。
public static class TrainCab
{
    // 運転台の世代
    public enum Style
    {
        OneHandleLcd,  // T字形ワンハンドル + 液晶モニタ2画面(新しい車両)
        OneHandle,     // ワンハンドル + 計器盤
        TwoHandle,     // マスコン(左) + ブレーキ弁(右) + アナログ丸型計器(古い車両)
    }

    // 車体ローカルの寸法。
    // **この車両モデルは前面窓が高い**(車体ローカルで約2.8〜4.0m)。実車の床からの
    // 寸法をそのまま使うと目線が窓の下に潜って前が見えないので、運転台一式は
    // 「窓から見て自然に見える位置」を基準に組む(目線→デスク→座席の上下関係を保つ)
    public const float FloorY = RailDimensions.VehicleFloorLocalY;   // 床面(参考)
    public const float FrontZ = 9.7f;        // 前面(車体半長)
    public const float BulkheadZ = 7.3f;     // 客室との仕切り
    public const float DeskX = -0.72f;       // 運転台の中心x(左)

    // 運転士の着席目線(車体ローカル)。CabPoseがこれを使う
    public const float EyeX = -0.55f;
    public const float EyeY = 2.95f;         // 前面窓の中ほど
    // 着席位置(座面の真上)。座席の背もたれより前でないと、背もたれが目線の直前へ来て
    // 画面を塞ぐ。デスクが画面下14%あたりに入る距離でもある
    public const float EyeZ = FrontZ - 1.55f;

    public const float DeskTopY = EyeY - 0.62f;   // 目線の下に天板が見える
    public const float CabFloorY = DeskTopY - 0.85f;
    public const float SeatZ = 8.15f;

    // モニター画面。縦画面は水平画角が約30°しかないので、助士側へ置くと映らない。
    // 実車の車側カメラのモニタと同じく**運転士の正面**、計器盤の右寄りに置く
    public const float MonitorX = -0.50f;
    // 実車と同じくコンソールの上面に立てる。低く置くと縦画角(60°)の下端から
    // 落ちて画面に映らない(2026-07-27にユーザーが実機で「モニター無い」と指摘)
    // 実車のITVモニタ相当(20cm級)。目線から約0.9mなので、これ以上大きいと
    // 画面幅いっぱいに広がって前方を塞ぐ
    public const float MonitorY = DeskTopY + 0.30f;
    public const float MonitorZ = FrontZ - 0.65f;
    public const float MonitorW = 0.26f, MonitorH = 0.17f;

    public static Style StyleOf(TrainCatalog.TrainTypeDef t) => t.cabStyle;

    // 運転台一式を車体の子として作る。戻り値はモニター画面のTransform(無ければnull)
    public static Transform Build(Transform car, TrainCatalog.TrainTypeDef t)
    {
        var dark = new RailKit.MeshData();     // 内装の暗色(仕切り・デスク)
        var panel = new RailKit.MeshData();    // 計器盤・機器
        var metal = new RailKit.MeshData();    // ハンドル・手すり
        var screen = new RailKit.MeshData();   // 液晶・計器の面

        // 仕切り壁(客室との間)。中央やや右に乗務員扉の開口を残す
        RailKit.AddBox(dark, new Vector3(-1.05f, CabFloorY + 1.4f, BulkheadZ),
            new Vector3(1.3f, 2.8f, 0.1f), Quaternion.identity);
        RailKit.AddBox(dark, new Vector3(1.15f, CabFloorY + 1.4f, BulkheadZ),
            new Vector3(1.1f, 2.8f, 0.1f), Quaternion.identity);
        RailKit.AddBox(dark, new Vector3(0.15f, CabFloorY + 2.55f, BulkheadZ),
            new Vector3(1.0f, 0.5f, 0.1f), Quaternion.identity);

        // 床(運転室の踏板)
        RailKit.AddBox(dark, new Vector3(0, CabFloorY - 0.03f, (FrontZ + BulkheadZ) * 0.5f),
            new Vector3(2.7f, 0.06f, FrontZ - BulkheadZ), Quaternion.identity);

        // 運転台デスク(左)。天板と前面
        RailKit.AddBox(dark, new Vector3(DeskX, DeskTopY, FrontZ - 0.75f),
            new Vector3(1.5f, 0.08f, 0.62f), Quaternion.identity);
        RailKit.AddBox(dark, new Vector3(DeskX, CabFloorY + 0.43f, FrontZ - 0.48f),
            new Vector3(1.5f, 0.86f, 0.1f), Quaternion.identity);

        // 計器盤(デスクの奥、やや起こす)
        var tilt = Quaternion.Euler(-24f, 0, 0);
        RailKit.AddBox(panel, new Vector3(DeskX, DeskTopY + 0.10f, FrontZ - 0.85f),
            new Vector3(1.44f, 0.30f, 0.08f), tilt);

        // 運転士席
        RailKit.AddBox(dark, new Vector3(DeskX, CabFloorY + 0.44f, SeatZ),
            new Vector3(0.5f, 0.1f, 0.46f), Quaternion.identity);
        RailKit.AddBox(dark, new Vector3(DeskX, CabFloorY + 0.74f, SeatZ - 0.26f),
            new Vector3(0.5f, 0.6f, 0.08f), Quaternion.identity);

        // 助士側の台(右)
        RailKit.AddBox(dark, new Vector3(0.95f, DeskTopY - 0.12f, FrontZ - 0.7f),
            new Vector3(1.0f, 0.08f, 0.52f), Quaternion.identity);

        // 前面窓の内側のピラー(左右と中央寄り)
        foreach (float px in new[] { -1.32f, 1.32f })
            RailKit.AddBox(dark, new Vector3(px, CabFloorY + 1.6f, FrontZ - 0.15f),
                new Vector3(0.12f, 1.9f, 0.12f), Quaternion.identity);

        // ---- 世代ごとの機器 ----
        switch (t.cabStyle)
        {
            case Style.OneHandleLcd:
                // T字形ワンハンドル(左手)。台座+横棒
                RailKit.AddBox(metal, new Vector3(DeskX - 0.42f, DeskTopY + 0.10f, FrontZ - 0.62f),
                    new Vector3(0.16f, 0.14f, 0.16f), Quaternion.identity);
                RailKit.AddBox(metal, new Vector3(DeskX - 0.42f, DeskTopY + 0.20f, FrontZ - 0.62f),
                    new Vector3(0.34f, 0.06f, 0.07f), Quaternion.identity);
                // 液晶モニタ2画面(計器盤の面)
                for (int k = -1; k <= 1; k += 2)
                    RailKit.AddBox(screen,
                        new Vector3(DeskX + k * 0.33f, DeskTopY + 0.11f, FrontZ - 0.89f),
                        new Vector3(0.56f, 0.22f, 0.02f), tilt);
                break;

            case Style.OneHandle:
                RailKit.AddBox(metal, new Vector3(DeskX - 0.42f, DeskTopY + 0.12f, FrontZ - 0.62f),
                    new Vector3(0.14f, 0.18f, 0.14f), Quaternion.identity);
                RailKit.AddBox(metal, new Vector3(DeskX - 0.42f, DeskTopY + 0.24f, FrontZ - 0.68f),
                    new Vector3(0.1f, 0.08f, 0.22f), Quaternion.identity);
                // 速度計(丸)と小さな表示器
                RailKit.AddBox(screen, new Vector3(DeskX + 0.05f, DeskTopY + 0.11f, FrontZ - 0.89f),
                    new Vector3(0.26f, 0.24f, 0.02f), tilt);
                RailKit.AddBox(screen, new Vector3(DeskX + 0.48f, DeskTopY + 0.11f, FrontZ - 0.89f),
                    new Vector3(0.26f, 0.16f, 0.02f), tilt);
                break;

            case Style.TwoHandle:
                // マスコン(左)とブレーキ弁(右)。丸いハンドルを2つ
                RailKit.AddBox(metal, new Vector3(DeskX - 0.45f, DeskTopY + 0.12f, FrontZ - 0.62f),
                    new Vector3(0.2f, 0.18f, 0.2f), Quaternion.identity);
                RailKit.AddBox(metal, new Vector3(DeskX + 0.45f, DeskTopY + 0.12f, FrontZ - 0.62f),
                    new Vector3(0.2f, 0.18f, 0.2f), Quaternion.identity);
                // アナログ丸型計器3つ
                for (int k = -1; k <= 1; k++)
                    RailKit.AddBox(screen,
                        new Vector3(DeskX + k * 0.42f, DeskTopY + 0.11f, FrontZ - 0.89f),
                        new Vector3(0.24f, 0.22f, 0.02f), tilt);
                break;
        }

        var darkMat = MatLib.Get("TrainUnder");
        RailKit.MeshGO("CabInterior", dark.ToMesh(), darkMat, car);
        RailKit.MeshGO("CabPanel", panel.ToMesh(), MatLib.Get("TrainPanto"), car);
        RailKit.MeshGO("CabMetal", metal.ToMesh(), MatLib.Get("Rail"), car);
        RailKit.MeshGO("CabScreen", screen.ToMesh(), MatLib.Get("TrainLight"), car);

        // ドアモニター(画面本体)。映像はTrain側がRenderTextureで流し込む
        var monitor = GameObject.CreatePrimitive(PrimitiveType.Quad);
        monitor.name = "DoorMonitor";
        Object.DestroyImmediate(monitor.GetComponent<Collider>());
        monitor.transform.SetParent(car, false);
        // 運転士の方(-z)を向き、少し上へ起こす
        monitor.transform.localPosition = new Vector3(MonitorX, MonitorY, MonitorZ);
        monitor.transform.localRotation = Quaternion.Euler(-18f, 180f, 0);
        monitor.transform.localScale = new Vector3(MonitorW, MonitorH, 1f);
        var mr = monitor.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.sharedMaterial = MatLib.Get("TrainLight");

        // 画面の枠
        var frame = new RailKit.MeshData();
        RailKit.AddBox(frame, new Vector3(MonitorX, MonitorY, MonitorZ + 0.02f),
            new Vector3(MonitorW + 0.06f, MonitorH + 0.06f, 0.03f), Quaternion.Euler(-18f, 0, 0));
        RailKit.MeshGO("CabMonitorFrame", frame.ToMesh(), darkMat, car);

        return monitor.transform;
    }
}
