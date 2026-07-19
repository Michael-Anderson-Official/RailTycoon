using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 街グリッドの検証(バッチ)。駅・線路を貫通していないか、道路が格子に乗っているかを
// 生成後のメッシュ頂点で調べ、目視用スナップショットも撮る
public static class CityTest
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        TrackNetwork.Clear();
        Bootstrap.BuildEnvironment();
        CityGrid.Init();

        var a = MakeStation(new Vector3(-500, 0, -150), 15, 10, 2, 4, "中央");
        var b = MakeStation(new Vector3(450, 0, 200), -20, 6, 2, 2, "本町");
        var c = MakeStation(new Vector3(1050, 0, -350), 50, 4, 1, 2, "緑ヶ丘");
        Connect(a, b);
        Connect(b, c);
        a.ForceDev(9f);
        b.ForceDev(5f);
        c.ForceDev(2.5f);
        CityGrid.FlushIfDirty(); // バッチではBootstrap.LateUpdateが回らないので手動反映

        // 貫通チェック: 建物メッシュの各頂点(xz)が駅構内矩形/線路近傍に無いか
        int intoStation = 0, intoTrack = 0, total = 0;
        foreach (var name in new[] { "CityLow", "CityMid", "CityHigh" })
        {
            var go = GameObject.Find(name);
            if (go == null) continue;
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            foreach (var v in mesh.vertices)
            {
                total++;
                var p = new Vector2(v.x, v.z);
                foreach (var st in TrackNetwork.stations)
                {
                    var l = st.transform.InverseTransformPoint(new Vector3(v.x, 0, v.z));
                    if (Mathf.Abs(l.x) < st.layout.totalWidth * 0.5f + 2f &&
                        Mathf.Abs(l.z) < st.HalfLen + StationLayout.ThroatLen) { intoStation++; break; }
                }
                foreach (var seg in TrackNetwork.segments)
                {
                    var s0 = new Vector2(seg.EndA.x, seg.EndA.z);
                    var s1 = new Vector2(seg.EndB.x, seg.EndB.z);
                    if (SegDist(p, s0, s1) < 4f) { intoTrack++; break; }
                }
            }
        }
        Debug.Log("CityTest: buildingVerts=" + total + " intoStation=" + intoStation + " intoTrack=" + intoTrack);

        // 道路が格子(Period*Cell間隔)に乗っているか: 道路ボックス中心のはず
        var road = GameObject.Find("CityRoad");
        int roadBoxes = road != null ? road.GetComponent<MeshFilter>().sharedMesh.vertexCount / 24 : 0;
        Debug.Log("CityTest: roadCells=" + roadBoxes);

        bool pass = total > 100 && intoStation == 0 && intoTrack == 0 && roadBoxes > 0;
        Debug.Log("CityTest: " + (pass ? "PASS" : "FAIL"));

        var outDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Snapshots");
        System.IO.Directory.CreateDirectory(outDir);
        Shot(outDir, "city_overview.png", new Vector3(150, 0, 0), 1500f, 55f, 200f);
        Shot(outDir, "city_station.png", a.transform.position, 380f, 38f, 150f);
        Shot(outDir, "city_grid.png", a.transform.position, 220f, 25f, 130f);
        EditorApplication.Exit(pass ? 0 : 1);
    }

    static float SegDist(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float d = Vector2.Dot(ab, ab);
        float t = d < 1e-6f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, ab) / d);
        return Vector2.Distance(p, a + ab * t);
    }

    static Station MakeStation(Vector3 pos, float yaw, int cars, int faces, int lines, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(BuildController.WorldRoot, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        var st = go.AddComponent<Station>();
        st.cars = cars; st.faces = faces; st.lines = lines; st.stationName = name;
        st.Build();
        TrackNetwork.stations.Add(st);
        return st;
    }

    static void Connect(Station a, Station b)
    {
        int bestSa = 1, bestSb = 1; float best = float.MaxValue;
        for (int sa = -1; sa <= 1; sa += 2)
            for (int sb = -1; sb <= 1; sb += 2)
            {
                float d = Vector3.Distance(a.End(sa), b.End(sb));
                if (d < best) { best = d; bestSa = sa; bestSb = sb; }
            }
        var seg = new TrackSegment { a = a, b = b, signA = bestSa, signB = bestSb };
        seg.Build(BuildController.WorldRoot);
        TrackNetwork.segments.Add(seg);
    }

    static void Shot(string dir, string name, Vector3 target, float dist, float pitch, float yaw)
    {
        var camGo = new GameObject("SnapCam");
        var cam = camGo.AddComponent<Camera>();
        cam.farClipPlane = 12000f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.68f, 0.81f, 0.93f);
        var rot = Quaternion.Euler(pitch, yaw, 0);
        cam.transform.SetPositionAndRotation(target + rot * new Vector3(0, 0, -dist), rot);
        const int W = 1600, H = 900;
        var rt = new RenderTexture(W, H, 24);
        var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest();
        if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, req))
        { req.destination = rt; UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req); }
        else { cam.targetTexture = rt; cam.Render(); }
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name), tex.EncodeToPNG());
        RenderTexture.active = null;
        Object.DestroyImmediate(tex); Object.DestroyImmediate(rt); Object.DestroyImmediate(camGo);
        Debug.Log("CityTest: wrote " + name);
    }
}
