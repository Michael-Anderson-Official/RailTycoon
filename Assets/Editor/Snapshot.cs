using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// バッチから -executeMethod Snapshot.Run。デモ配置(駅3・線路2・列車2)を組んで
// 複数視点のPNGを Snapshots/ に書き出す(見た目のリモート検証用)
public static class Snapshot
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        TrackNetwork.Clear();
        Bootstrap.BuildEnvironment();

        var stA = MakeStation(new Vector3(-600, 0, -250), 15f, 10, 2, 4, "中央");
        var stB = MakeStation(new Vector3(350, 0, 180), -25f, 6, 2, 2, "本町");
        var stC = MakeStation(new Vector3(1000, 0, -420), 55f, 4, 1, 2, "緑ヶ丘");
        stA.ForceDev(4);
        stB.ForceDev(2);
        stC.ForceDev(1);

        var segAB = Connect(stA, stB);
        var segBC = Connect(stB, stC);

        // 京王5000系10連をA→B走行中に、名鉄6000系4連をC駅停車中に配置
        PlaceTrain(TrainCatalog.Formations[0], stA, segAB, stB, 0.45f);
        PlaceTrain(TrainCatalog.Formations[5], stB, segBC, stC, 1f);

        var outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Snapshots");
        Directory.CreateDirectory(outDir);
        Shot(outDir, "overview.png", new Vector3(150, 0, -150), 1900f, 50f, 205f);
        Shot(outDir, "station_a.png", stA.transform.position, 420f, 35f, 160f);
        Shot(outDir, "station_a_close.png", stA.transform.position, 160f, 24f, 120f);
        Shot(outDir, "station_b.png", stB.transform.position, 300f, 30f, 230f);
        Shot(outDir, "station_c.png", stC.transform.position, 250f, 30f, 20f);
        Shot(outDir, "train_running.png", TrainPos, 140f, 18f, 150f);
        Shot(outDir, "throat.png", stA.End(1), 150f, 28f, 190f);

        Debug.Log("Snapshot: done");
        EditorApplication.Exit(0);
    }

    static Vector3 TrainPos;

    static Station MakeStation(Vector3 pos, float yaw, int cars, int faces, int lines, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(BuildController.WorldRoot, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        var st = go.AddComponent<Station>();
        st.cars = cars;
        st.faces = faces;
        st.lines = lines;
        st.stationName = name;
        st.Build();
        TrackNetwork.stations.Add(st);
        return st;
    }

    static TrackSegment Connect(Station a, Station b)
    {
        int bestSa = 1, bestSb = 1;
        float best = float.MaxValue;
        for (int sa = -1; sa <= 1; sa += 2)
            for (int sb = -1; sb <= 1; sb += 2)
            {
                float d = Vector3.Distance(a.End(sa), b.End(sb));
                if (d < best) { best = d; bestSa = sa; bestSb = sb; }
            }
        var seg = new TrackSegment { a = a, b = b, signA = bestSa, signB = bestSb };
        seg.Build(BuildController.WorldRoot);
        TrackNetwork.segments.Add(seg);
        return seg;
    }

    // fromの停車線からtoの停車線への走行経路上、割合fracの位置に編成を置く
    static void PlaceTrain(TrainCatalog.Formation fm, Station from, TrackSegment seg, Station to, float frac)
    {
        int t0 = from.layout.stopTracks[0];
        int t1 = to.layout.stopTracks[0];
        float half = fm.cars * StationLayout.CarLength * 0.5f;
        var path = Train.BuildLeg(from, t0, seg.SignAt(from), to, t1, seg.SignAt(to), half);
        var cum = RailKit.Cumulative(path);
        var root = new GameObject("DemoTrain_" + fm.Label);
        root.transform.SetParent(BuildController.WorldRoot, false);
        var cars = TrainVisual.BuildCars(root.transform, fm);
        float s = Mathf.Lerp(half * 2f, cum[cum.Length - 1], frac);
        Train.PlaceCarsStatic(cars, path, cum, s);
        if (frac > 0.3f && frac < 0.7f)
        {
            Vector3 p, f;
            RailKit.Sample(path, cum, s - half, out p, out f);
            TrainPos = p;
        }
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
        var req = new RenderPipeline.StandardRequest();
        if (RenderPipeline.SupportsRenderRequest(cam, req))
        {
            req.destination = rt;
            RenderPipeline.SubmitRenderRequest(cam, req);
        }
        else
        {
            cam.targetTexture = rt;
            cam.Render();
        }
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        File.WriteAllBytes(Path.Combine(dir, name), tex.EncodeToPNG());
        RenderTexture.active = null;
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGo);
        Debug.Log("Snapshot: wrote " + name);
    }
}
