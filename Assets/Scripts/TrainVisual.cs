using System.Collections.Generic;
using UnityEngine;

// 編成のビジュアル(箱ベース+帯・窓・前面色)。carTs[0]が進行方向先頭
public static class TrainVisual
{
    public static List<Transform> BuildCars(Transform root, TrainCatalog.Formation fm)
    {
        var t = fm.type;
        var bodyMat = MatLib.Tinted("TrainBase", t.body);
        var stripeMat = MatLib.Tinted("TrainBase", t.stripe);
        var frontMat = MatLib.Tinted("TrainBase", t.front);
        var darkMat = MatLib.Get("TrainDark");
        var roofMat = MatLib.Get("TrainRoof");

        var cars = new List<Transform>();
        for (int i = 0; i < fm.cars; i++)
        {
            var go = new GameObject("Car" + i);
            go.transform.SetParent(root, false);

            var body = new RailKit.MeshData();
            RailKit.AddBox(body, new Vector3(0, 2.5f, 0), new Vector3(2.9f, 3.0f, 19.4f), Quaternion.identity);
            RailKit.MeshGO("Body", body.ToMesh(), bodyMat, go.transform);

            var stripe = new RailKit.MeshData();
            RailKit.AddBox(stripe, new Vector3(1.49f, 1.95f, 0), new Vector3(0.06f, 0.55f, 19.4f), Quaternion.identity);
            RailKit.AddBox(stripe, new Vector3(-1.49f, 1.95f, 0), new Vector3(0.06f, 0.55f, 19.4f), Quaternion.identity);
            RailKit.MeshGO("Stripe", stripe.ToMesh(), stripeMat, go.transform);

            var win = new RailKit.MeshData();
            RailKit.AddBox(win, new Vector3(1.49f, 3.05f, 0), new Vector3(0.05f, 0.9f, 18.2f), Quaternion.identity);
            RailKit.AddBox(win, new Vector3(-1.49f, 3.05f, 0), new Vector3(0.05f, 0.9f, 18.2f), Quaternion.identity);
            RailKit.MeshGO("Windows", win.ToMesh(), darkMat, go.transform);

            var roof = new RailKit.MeshData();
            RailKit.AddBox(roof, new Vector3(0, 4.1f, 0), new Vector3(2.7f, 0.22f, 19.2f), Quaternion.identity);
            RailKit.AddBox(roof, new Vector3(0, 0.6f, 6.5f), new Vector3(2.4f, 0.9f, 2.8f), Quaternion.identity);
            RailKit.AddBox(roof, new Vector3(0, 0.6f, -6.5f), new Vector3(2.4f, 0.9f, 2.8f), Quaternion.identity);
            RailKit.MeshGO("RoofBogies", roof.ToMesh(), roofMat, go.transform);

            // 前面色: 先頭・最後尾車の外側の端面のみ。中間車の連結面は暗色
            var caps = new RailKit.MeshData();
            var capsFront = new RailKit.MeshData();
            if (i == 0) RailKit.AddBox(capsFront, new Vector3(0, 2.5f, 9.78f), new Vector3(2.92f, 3.05f, 0.2f), Quaternion.identity);
            else RailKit.AddBox(caps, new Vector3(0, 2.5f, 9.75f), new Vector3(2.7f, 2.9f, 0.12f), Quaternion.identity);
            if (i == fm.cars - 1) RailKit.AddBox(capsFront, new Vector3(0, 2.5f, -9.78f), new Vector3(2.92f, 3.05f, 0.2f), Quaternion.identity);
            else RailKit.AddBox(caps, new Vector3(0, 2.5f, -9.75f), new Vector3(2.7f, 2.9f, 0.12f), Quaternion.identity);
            if (caps.v.Count > 0) RailKit.MeshGO("Gangway", caps.ToMesh(), darkMat, go.transform);
            if (capsFront.v.Count > 0) RailKit.MeshGO("Face", capsFront.ToMesh(), frontMat, go.transform);

            cars.Add(go.transform);
        }
        return cars;
    }
}
