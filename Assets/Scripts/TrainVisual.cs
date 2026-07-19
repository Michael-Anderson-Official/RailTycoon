using System.Collections.Generic;
using UnityEngine;

// 編成のビジュアル。断面押し出しで丸みのある車体+屋根カーブ、側窓・ドア、床下機器、
// 台車、パンタgrラフ、前面ガラスを付ける。carTs[0]が進行方向先頭。
// 1両=パーツ別の子(マテリアルごと)。車体はz方向長さ、断面はxy。
public static class TrainVisual
{
    const float CarLen = 19.4f;
    const float HalfLen = CarLen * 0.5f;

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

    public static List<Transform> BuildCars(Transform root, TrainCatalog.Formation fm)
    {
        var t = fm.type;
        var bodyMat = MatLib.Tinted("TrainBase", t.body);
        var stripeMat = MatLib.Tinted("TrainBase", t.stripe);
        var frontMat = MatLib.Tinted("TrainBase", t.front);
        var doorMat = MatLib.Tinted("TrainBase", Darken(t.body, 0.82f));
        var glassMat = MatLib.Get("TrainGlass");
        var roofMat = MatLib.Get("TrainRoof");
        var underMat = MatLib.Get("TrainUnder");
        var pantoMat = MatLib.Get("TrainPanto");

        var cars = new List<Transform>();
        for (int i = 0; i < fm.cars; i++)
        {
            bool headEnd = i == 0;
            bool tailEnd = i == fm.cars - 1;
            var go = new GameObject("Car" + i);
            go.transform.SetParent(root, false);

            // 車体(丸み断面の押し出し)
            var body = new RailKit.MeshData();
            RailKit.AddExtrude(body, BodySection, -HalfLen, HalfLen);
            RailKit.MeshGO("Body", body.ToMesh(), bodyMat, go.transform);

            // 屋根(緩い蒲鉾)を車体上にかぶせて滑らかに
            var roof = new RailKit.MeshData();
            RailKit.AddExtrude(roof, RoofSection(), -HalfLen + 0.15f, HalfLen - 0.15f);
            RailKit.MeshGO("Roof", roof.ToMesh(), roofMat, go.transform);

            // 側面の窓帯(左右)・ドア・カラー帯
            var glass = new RailKit.MeshData();
            var doors = new RailKit.MeshData();
            var stripe = new RailKit.MeshData();
            for (int side = -1; side <= 1; side += 2)
            {
                float x = 1.455f * side;
                // 連続窓帯
                RailKit.AddBox(glass, new Vector3(x, 3.02f, 0), new Vector3(0.06f, 0.95f, CarLen - 2.2f), Quaternion.identity);
                // ドア4枚(窓より暗い面+小窓)
                float[] doorZ = { -6.4f, -2.15f, 2.15f, 6.4f };
                foreach (float dz in doorZ)
                {
                    RailKit.AddBox(doors, new Vector3(x, 2.15f, dz), new Vector3(0.05f, 3.0f, 1.3f), Quaternion.identity);
                    RailKit.AddBox(glass, new Vector3(x, 3.1f, dz), new Vector3(0.07f, 0.8f, 1.0f), Quaternion.identity);
                }
                // カラー帯(腰部)
                RailKit.AddBox(stripe, new Vector3(x, 1.75f, 0), new Vector3(0.05f, 0.5f, CarLen - 0.4f), Quaternion.identity);
            }
            RailKit.MeshGO("Glass", glass.ToMesh(), glassMat, go.transform);
            RailKit.MeshGO("Doors", doors.ToMesh(), doorMat, go.transform);
            RailKit.MeshGO("Stripe", stripe.ToMesh(), stripeMat, go.transform);

            // 床下機器+台車
            var under = new RailKit.MeshData();
            RailKit.AddBox(under, new Vector3(0, 0.35f, 0), new Vector3(2.4f, 0.6f, CarLen - 5f), Quaternion.identity);
            foreach (float bz in new[] { 6.2f, -6.2f })
            {
                RailKit.AddBox(under, new Vector3(0, 0.5f, bz), new Vector3(2.5f, 0.5f, 2.6f), Quaternion.identity);
                // 車輪
                for (int w = 0; w < 2; w++)
                    for (int side = -1; side <= 1; side += 2)
                        RailKit.AddBox(under, new Vector3(1.0f * side, 0.42f, bz + (w == 0 ? 0.9f : -0.9f)),
                            new Vector3(0.2f, 0.84f, 0.84f), Quaternion.identity);
            }
            RailKit.MeshGO("Under", under.ToMesh(), underMat, go.transform);

            // パンタグラフ(先頭/最後尾以外の偶数車に1基、簡易シングルアーム)
            if (!headEnd && !tailEnd && i % 2 == 0)
                RailKit.MeshGO("Panto", Pantograph(), pantoMat, go.transform);

            // 前面(先頭・最後尾車の外側端に傾斜ガラス顔)
            if (headEnd) AddFace(go.transform, +1, frontMat, glassMat, t);
            if (tailEnd) AddFace(go.transform, -1, frontMat, glassMat, t);
            // 中間連結面は暗色の貫通路
            if (!headEnd) AddGangway(go.transform, -1, doorMat);
            if (!tailEnd) AddGangway(go.transform, +1, doorMat);

            cars.Add(go.transform);
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

    // 傾斜した前面(車体色の枠+黒ガラス+ライト)。sign=+1で+z端、-1で-z端
    static void AddFace(Transform car, int sign, Material bodyMat, Material glassMat, TrainCatalog.TrainTypeDef t)
    {
        float z = HalfLen * sign;
        var frame = new RailKit.MeshData();
        var glass = new RailKit.MeshData();
        var lite = new RailKit.MeshData();
        var q = Quaternion.Euler(sign * 12f, sign > 0 ? 0 : 180f, 0); // 前面をわずかに傾ける
        Vector3 c = new Vector3(0, 2.4f, z + sign * 0.15f);
        // 前面パネル(車体色)
        RailKit.AddBox(frame, c + new Vector3(0, 0.9f, 0), new Vector3(2.7f, 1.7f, 0.3f), q);
        RailKit.AddBox(frame, c + new Vector3(0, -1.4f, 0), new Vector3(2.7f, 1.1f, 0.3f), q);
        // 運転席ガラス
        RailKit.AddBox(glass, c + new Vector3(0, 0.95f, sign * 0.05f), new Vector3(2.2f, 1.15f, 0.12f), q);
        // 前照灯・尾灯
        for (int side = -1; side <= 1; side += 2)
            RailKit.AddBox(lite, c + new Vector3(0.85f * side, -0.55f, sign * 0.12f),
                new Vector3(0.45f, 0.32f, 0.14f), q);
        RailKit.MeshGO("Face", frame.ToMesh(), bodyMat, car);
        RailKit.MeshGO("FaceGlass", glass.ToMesh(), glassMat, car);
        RailKit.MeshGO("FaceLite", Recolor(lite.ToMesh()), MatLib.Get("TrainLight"), car);
    }

    static void AddGangway(Transform car, int sign, Material mat)
    {
        var md = new RailKit.MeshData();
        RailKit.AddBox(md, new Vector3(0, 2.4f, HalfLen * sign - sign * 0.05f),
            new Vector3(1.9f, 2.4f, 0.12f), Quaternion.identity);
        RailKit.MeshGO("Gangway", md.ToMesh(), mat, car);
    }

    static Mesh Recolor(Mesh m) => m; // ライトは専用マテリアルで着色

    static Color Darken(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);
}
