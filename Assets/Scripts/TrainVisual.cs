using System.Collections.Generic;
using UnityEngine;

// 編成のビジュアル。断面押し出しで丸みのある車体+屋根カーブ、側窓・ドア、床下機器、
// 台車、パンタgrラフ、前面ガラスを付ける。carTs[0]が進行方向先頭。
// 1両=パーツ別の子(マテリアルごと)。車体はz方向長さ、断面はxy。
public static class TrainVisual
{
    const float CarLen = 19.4f;
    const float HalfLen = CarLen * 0.5f;
    // 台車中心のz位置(車体ローカル、進行方向)。Train.PlaceCarsStaticが同じ値を
    // 参照して弧長サンプル位置を決めるので、変更する場合は両方を合わせること
    public const float BogieOffset = 6.2f;

    // 車体断面(正面図。x横/y縦、床上0.65〜屋根4.15)。反時計回り
    static readonly Vector2[] BodySection =
    {
        new Vector2(1.42f, 0.65f),
        new Vector2(1.45f, 1.1f),
        new Vector2(1.45f, 3.35f),
        new Vector2(1.28f, 3.75f),
        new Vector2(0.95f, 4.08f),
        new Vector2(0.45f, 4.15f),
        new Vector2(-0.45f, 4.15f),
        new Vector2(-0.95f, 4.08f),
        new Vector2(-1.28f, 3.75f),
        new Vector2(-1.45f, 3.35f),
        new Vector2(-1.45f, 1.1f),
        new Vector2(-1.42f, 0.65f),
    };

    // 1両分の主要Transform。bogieF/bogieRはPlaceCarsStaticが弧長サンプルで
    // 独立に位置・向きを合わせる台車(先頭寄り/後尾寄り)
    public static List<(Transform body, Transform bogieF, Transform bogieR)> BuildCars(Transform root, TrainCatalog.Formation fm)
    {
        var t = fm.type;
        var bodyMat = MatLib.Tinted("TrainBase", t.body);
        var stripeMat = MatLib.Tinted("TrainBase", t.stripe);
        var band2Mat = MatLib.Tinted("TrainBase", t.band2.a > 0f ? t.band2 : t.stripe);
        var frontMat = MatLib.Tinted("TrainBase", t.front);
        var doorMat = MatLib.Tinted("TrainBase", Darken(t.body, 0.82f));
        var glassMat = MatLib.Get("TrainGlass");
        var roofMat = MatLib.Get("TrainRoof");
        var underMat = MatLib.Get("TrainUnder");
        var pantoMat = MatLib.Get("TrainPanto");

        var cars = new List<(Transform body, Transform bogieF, Transform bogieR)>();
        for (int i = 0; i < fm.cars; i++)
        {
            bool headEnd = i == 0;
            bool tailEnd = i == fm.cars - 1;
            var go = new GameObject("Car" + i);
            go.transform.SetParent(root, false);

            float[] doorZ = { -6.4f, -2.15f, 2.15f, 6.4f };

            // 車体(丸み断面の押し出し)+ 窓柱(連続窓を個別窓に見せる)
            var body = new RailKit.MeshData();
            RailKit.AddExtrude(body, BodySection, -HalfLen, HalfLen);
            for (int side = -1; side <= 1; side += 2)
            {
                float px = 1.452f * side;
                for (float pz = -HalfLen + 1.0f; pz <= HalfLen - 1.0f; pz += 1.5f)
                {
                    bool nearDoor = false;
                    foreach (float dz in doorZ) if (Mathf.Abs(pz - dz) < 1.05f) nearDoor = true;
                    if (nearDoor) continue;
                    RailKit.AddBox(body, new Vector3(px, 3.02f, pz), new Vector3(0.06f, 1.06f, 0.14f), Quaternion.identity);
                }
                // ドア枠(左右の縦桟, 車体色)
                foreach (float dz in doorZ)
                {
                    RailKit.AddBox(body, new Vector3(px, 2.05f, dz + 0.7f), new Vector3(0.05f, 2.95f, 0.1f), Quaternion.identity);
                    RailKit.AddBox(body, new Vector3(px, 2.05f, dz - 0.7f), new Vector3(0.05f, 2.95f, 0.1f), Quaternion.identity);
                }
            }
            RailKit.MeshGO("Body", body.ToMesh(), bodyMat, go.transform);

            // 屋根(緩い蒲鉾)を車体上にかぶせて滑らかに
            var roof = new RailKit.MeshData();
            RailKit.AddExtrude(roof, RoofSection(), -HalfLen + 0.15f, HalfLen - 0.15f);
            RailKit.MeshGO("Roof", roof.ToMesh(), roofMat, go.transform);

            // 屋根上機器(冷房装置・配管)。俯瞰でよく見える
            var equip = new RailKit.MeshData();
            RailKit.AddBox(equip, new Vector3(0, 4.28f, 0), new Vector3(0.34f, 0.14f, CarLen - 1.2f), Quaternion.identity); // 配管
            if (t.keio5000)
            {
                // 京王5000: 大型冷房装置を各車1台(中央・分散ファン)
                RailKit.AddBox(equip, new Vector3(0, 4.36f, 0), new Vector3(2.1f, 0.34f, 8.5f), Quaternion.identity);
                foreach (float fz in new[] { -3f, 0f, 3f })
                {
                    RailKit.AddBox(equip, new Vector3(0.55f, 4.56f, fz), new Vector3(0.74f, 0.06f, 0.74f), Quaternion.identity);
                    RailKit.AddBox(equip, new Vector3(-0.55f, 4.56f, fz), new Vector3(0.74f, 0.06f, 0.74f), Quaternion.identity);
                }
            }
            else
            {
                foreach (float ez in new[] { 4.7f, -4.7f })
                {
                    RailKit.AddBox(equip, new Vector3(0, 4.36f, ez), new Vector3(2.0f, 0.32f, 3.0f), Quaternion.identity);
                    RailKit.AddBox(equip, new Vector3(0.55f, 4.55f, ez), new Vector3(0.72f, 0.06f, 0.72f), Quaternion.identity);
                    RailKit.AddBox(equip, new Vector3(-0.55f, 4.55f, ez), new Vector3(0.72f, 0.06f, 0.72f), Quaternion.identity);
                }
            }
            RailKit.MeshGO("Equip", equip.ToMesh(), pantoMat, go.transform);

            // 側面の窓帯(左右)・ドア・カラー帯
            var glass = new RailKit.MeshData();
            var doors = new RailKit.MeshData();
            var bandHi = new RailKit.MeshData();  // 窓上帯(京王レッド)
            var bandLo = new RailKit.MeshData();  // 窓下帯(京王ブルー/他は腰帯)
            for (int side = -1; side <= 1; side += 2)
            {
                float x = 1.455f * side;
                // 連続窓帯(柱で個別窓に見える)
                RailKit.AddBox(glass, new Vector3(x, 3.02f, 0), new Vector3(0.06f, 0.9f, CarLen - 2.2f), Quaternion.identity);
                // ドア4枚(凹んだ暗色面+小窓)
                foreach (float dz in doorZ)
                {
                    RailKit.AddBox(doors, new Vector3(x - 0.02f * side, 2.05f, dz), new Vector3(0.05f, 2.9f, 1.28f), Quaternion.identity);
                    RailKit.AddBox(glass, new Vector3(x, 3.05f, dz), new Vector3(0.07f, 0.82f, 1.0f), Quaternion.identity);
                }
                if (t.keio5000)
                {
                    // 窓上=京王レッド(細帯)、窓下=京王ブルー(下部を広く)
                    RailKit.AddBox(bandHi, new Vector3(x, 3.58f, 0), new Vector3(0.05f, 0.2f, CarLen - 0.4f), Quaternion.identity);
                    RailKit.AddBox(bandLo, new Vector3(x, 1.5f, 0), new Vector3(0.05f, 0.95f, CarLen - 0.4f), Quaternion.identity);
                }
                else
                {
                    RailKit.AddBox(bandLo, new Vector3(x, 1.75f, 0), new Vector3(0.05f, 0.5f, CarLen - 0.4f), Quaternion.identity);
                }
            }
            RailKit.MeshGO("Glass", glass.ToMesh(), glassMat, go.transform);
            RailKit.MeshGO("Doors", doors.ToMesh(), doorMat, go.transform);
            if (bandHi.v.Count > 0) RailKit.MeshGO("BandHi", bandHi.ToMesh(), stripeMat, go.transform);
            RailKit.MeshGO("BandLo", bandLo.ToMesh(), band2Mat, go.transform);

            // 床下機器(車体に固定、台車とは独立)
            var under = new RailKit.MeshData();
            RailKit.AddBox(under, new Vector3(0, 0.42f, 0), new Vector3(2.5f, 0.62f, CarLen - 5f), Quaternion.identity); // 床下機器箱
            RailKit.AddBox(under, new Vector3(1.15f, 0.5f, 2.5f), new Vector3(0.5f, 0.5f, 3.0f), Quaternion.identity);    // 補機
            RailKit.AddBox(under, new Vector3(-1.1f, 0.45f, -3f), new Vector3(0.6f, 0.55f, 2.4f), Quaternion.identity);
            RailKit.MeshGO("Under", under.ToMesh(), underMat, go.transform);

            // 台車(側枠・車軸・車輪)は車体とは別Transformの子として作る。
            // PlaceCarsStaticが台車ごとに自分の弧長位置でレール中心線を独立サンプルして
            // 向きを合わせるので、渡り線のような急なカーブでも車体1枚の平均姿勢に
            // 引っ張られず、車輪が必ずレールへ追従する(BogieOffsetと一致させること)
            Transform bogieF = null, bogieR = null;
            foreach (float bz in new[] { BogieOffset, -BogieOffset })
            {
                var bogieGo = new GameObject(bz > 0 ? "BogieF" : "BogieR");
                bogieGo.transform.SetParent(go.transform, false);
                bogieGo.transform.localPosition = new Vector3(0, 0, bz);
                var bogie = new RailKit.MeshData(); // メッシュ座標は台車中心(bz)基準のローカル
                for (int side = -1; side <= 1; side += 2)
                    RailKit.AddBox(bogie, new Vector3(1.02f * side, 0.5f, 0), new Vector3(0.16f, 0.42f, 2.9f), Quaternion.identity); // 側枠
                RailKit.AddBox(bogie, new Vector3(0, 0.62f, 0), new Vector3(2.1f, 0.3f, 0.7f), Quaternion.identity); // 枕梁
                foreach (float wz in new[] { 0.95f, -0.95f })
                {
                    RailKit.AddBox(bogie, new Vector3(0, 0.42f, wz), new Vector3(2.0f, 0.16f, 0.16f), Quaternion.identity); // 車軸
                    for (int side = -1; side <= 1; side += 2)
                        RailKit.AddBox(bogie, new Vector3(1.02f * side, 0.42f, wz), new Vector3(0.24f, 0.86f, 0.86f), Quaternion.identity); // 車輪
                }
                RailKit.MeshGO("BogieMesh", bogie.ToMesh(), underMat, bogieGo.transform);
                if (bz > 0) bogieF = bogieGo.transform; else bogieR = bogieGo.transform;
            }

            // パンタグラフ(シングルアーム)
            if (t.keio5000)
            {
                // 6M4T相当: 電動車の八王子寄り(+z端)に1基。両端車以外の奇数indexに配置
                if (!headEnd && !tailEnd && i % 2 == 1)
                {
                    var p = RailKit.MeshGO("Panto", Pantograph(), pantoMat, go.transform);
                    p.transform.localPosition = new Vector3(0, 0, 6f);
                }
            }
            else if (!headEnd && !tailEnd && i % 2 == 0)
            {
                RailKit.MeshGO("Panto", Pantograph(), pantoMat, go.transform);
            }

            // 前面
            if (t.keio5000)
            {
                if (headEnd) AddKeioFace(go.transform, +1, frontMat, glassMat, stripeMat, band2Mat);
                if (tailEnd) AddKeioFace(go.transform, -1, frontMat, glassMat, stripeMat, band2Mat);
            }
            else
            {
                if (headEnd) AddFace(go.transform, +1, frontMat, glassMat, t);
                if (tailEnd) AddFace(go.transform, -1, frontMat, glassMat, t);
            }
            // 中間連結面は暗色の貫通路
            if (!headEnd) AddGangway(go.transform, -1, doorMat);
            if (!tailEnd) AddGangway(go.transform, +1, doorMat);

            cars.Add((go.transform, bogieF, bogieR));
        }
        return cars;
    }

    static Vector2[] RoofSection()
    {
        return new[]
        {
            new Vector2(1.28f, 3.78f),
            new Vector2(0.95f, 4.12f),
            new Vector2(0.45f, 4.22f),
            new Vector2(-0.45f, 4.22f),
            new Vector2(-0.95f, 4.12f),
            new Vector2(-1.28f, 3.78f),
            new Vector2(-1.1f, 3.7f),
            new Vector2(1.1f, 3.7f),
        };
    }

    static Mesh Pantograph()
    {
        var md = new RailKit.MeshData();
        // 台枠
        RailKit.AddBox(md, new Vector3(0, 4.3f, 0), new Vector3(1.6f, 0.14f, 2.0f), Quaternion.identity);
        // 下アーム(斜め)
        RailKit.AddBox(md, new Vector3(0, 4.75f, 0.35f), new Vector3(0.12f, 1.0f, 0.12f),
            Quaternion.Euler(35f, 0, 0));
        // 上アーム
        RailKit.AddBox(md, new Vector3(0, 5.25f, -0.35f), new Vector3(0.1f, 0.9f, 0.1f),
            Quaternion.Euler(-40f, 0, 0));
        // 舟体(架線と接する横棒)
        RailKit.AddBox(md, new Vector3(0, 5.55f, -0.05f), new Vector3(1.5f, 0.08f, 0.18f), Quaternion.identity);
        return md.ToMesh();
    }

    // 傾斜した前面(車体色の枠+黒ガラス+ライト+スカート+連結器)。sign=+1で+z端、-1で-z端
    static void AddFace(Transform car, int sign, Material bodyMat, Material glassMat, TrainCatalog.TrainTypeDef t)
    {
        float z = HalfLen * sign;
        var frame = new RailKit.MeshData();
        var glass = new RailKit.MeshData();
        var lite = new RailKit.MeshData();
        var dark = new RailKit.MeshData();
        var q = Quaternion.Euler(sign * 12f, sign > 0 ? 0 : 180f, 0); // 前面をわずかに傾ける
        Vector3 c = new Vector3(0, 2.4f, z + sign * 0.15f);
        // 前面パネル(車体色): 窓上・窓下
        RailKit.AddBox(frame, c + new Vector3(0, 0.95f, 0), new Vector3(2.72f, 1.6f, 0.3f), q);
        RailKit.AddBox(frame, c + new Vector3(0, -1.35f, 0), new Vector3(2.72f, 1.1f, 0.3f), q);
        // 屋根と前面をつなぐ上部の丸み
        RailKit.AddBox(frame, c + new Vector3(0, 1.7f, sign * -0.12f), new Vector3(2.45f, 0.5f, 0.3f),
            q * Quaternion.Euler(sign * 22f, 0, 0));
        // 運転席ガラス
        RailKit.AddBox(glass, c + new Vector3(0, 0.98f, sign * 0.06f), new Vector3(2.26f, 1.2f, 0.12f), q);
        // 前照灯・尾灯
        for (int side = -1; side <= 1; side += 2)
            RailKit.AddBox(lite, c + new Vector3(0.88f * side, -0.5f, sign * 0.13f),
                new Vector3(0.42f, 0.3f, 0.15f), q);
        // スカート(前面下部カバー) + 連結器
        RailKit.AddBox(frame, c + new Vector3(0, -2.1f, sign * 0.02f), new Vector3(2.5f, 0.95f, 0.32f), q);
        RailKit.AddBox(dark, new Vector3(0, 0.78f, z + sign * 0.55f), new Vector3(0.5f, 0.36f, 0.8f), Quaternion.identity);
        RailKit.MeshGO("Face", frame.ToMesh(), bodyMat, car);
        RailKit.MeshGO("FaceGlass", glass.ToMesh(), glassMat, car);
        RailKit.MeshGO("FaceLite", lite.ToMesh(), MatLib.Get("TrainLight"), car);
        RailKit.MeshGO("FaceCoupler", dark.ToMesh(), MatLib.Get("TrainUnder"), car);
    }

    // 京王5000系(2代)の前面: 黒主体で大きく前傾、貫通扉は左(助士側)、窓上=赤/窓下=青帯、
    // 窓下左右にLED灯、スカート+連結器
    static void AddKeioFace(Transform car, int sign, Material blackMat, Material glassMat, Material redMat, Material blueMat)
    {
        float z = HalfLen * sign;
        var black = new RailKit.MeshData();
        var glass = new RailKit.MeshData();
        var red = new RailKit.MeshData();
        var blue = new RailKit.MeshData();
        var lite = new RailKit.MeshData();
        var dark = new RailKit.MeshData();
        var q = Quaternion.Euler(sign * 18f, sign > 0 ? 0 : 180f, 0);   // 大きく前傾
        var c = new Vector3(0, 2.4f, z + sign * 0.18f);

        // 黒い前面パネル(窓上・窓下・上部丸み)
        RailKit.AddBox(black, c + q * new Vector3(0, 1.05f, 0), new Vector3(2.74f, 1.55f, 0.3f), q);
        RailKit.AddBox(black, c + q * new Vector3(0, -1.25f, 0), new Vector3(2.74f, 1.15f, 0.3f), q);
        RailKit.AddBox(black, c + q * new Vector3(0, 1.78f, -0.14f), new Vector3(2.5f, 0.5f, 0.3f), q * Quaternion.Euler(sign * 22f, 0, 0));
        // 窓上=京王レッド帯 / 窓下=京王ブルー帯(前面に回り込む)
        RailKit.AddBox(red, c + q * new Vector3(0, 1.92f, 0.02f), new Vector3(2.74f, 0.16f, 0.32f), q);
        RailKit.AddBox(blue, c + q * new Vector3(0, -0.05f, 0.02f), new Vector3(2.74f, 0.24f, 0.32f), q);
        // 運転席ガラス(貫通扉が左=助士側なので中央よりやや右寄り)
        RailKit.AddBox(glass, c + q * new Vector3(0.35f, 1.0f, 0.06f), new Vector3(1.7f, 1.2f, 0.12f), q);
        // 前面貫通扉(左側、黒。窓付き)
        RailKit.AddBox(black, c + q * new Vector3(-0.95f, 0.55f, 0.03f), new Vector3(0.75f, 2.9f, 0.28f), q);
        RailKit.AddBox(glass, c + q * new Vector3(-0.95f, 1.3f, 0.08f), new Vector3(0.58f, 0.7f, 0.1f), q);
        // LED灯(窓下左右)
        for (int side = -1; side <= 1; side += 2)
            RailKit.AddBox(lite, c + q * new Vector3(1.0f * side, -0.55f, 0.14f), new Vector3(0.4f, 0.26f, 0.14f), q);
        // スカート + 連結器
        RailKit.AddBox(black, c + q * new Vector3(0, -2.0f, 0.02f), new Vector3(2.55f, 0.95f, 0.32f), q);
        RailKit.AddBox(dark, new Vector3(0, 0.78f, z + sign * 0.55f), new Vector3(0.5f, 0.36f, 0.8f), Quaternion.identity);

        RailKit.MeshGO("Face", black.ToMesh(), blackMat, car);
        RailKit.MeshGO("FaceGlass", glass.ToMesh(), glassMat, car);
        RailKit.MeshGO("FaceRed", red.ToMesh(), redMat, car);
        RailKit.MeshGO("FaceBlue", blue.ToMesh(), blueMat, car);
        RailKit.MeshGO("FaceLite", lite.ToMesh(), MatLib.Get("TrainLight"), car);
        RailKit.MeshGO("FaceCoupler", dark.ToMesh(), MatLib.Get("TrainUnder"), car);
    }

    static void AddGangway(Transform car, int sign, Material mat)
    {
        var md = new RailKit.MeshData();
        RailKit.AddBox(md, new Vector3(0, 2.4f, HalfLen * sign - sign * 0.05f),
            new Vector3(1.9f, 2.4f, 0.12f), Quaternion.identity);
        RailKit.MeshGO("Gangway", md.ToMesh(), mat, car);
    }

    static Color Darken(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);
}
