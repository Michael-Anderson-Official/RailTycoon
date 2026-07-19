using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 閉塞・左側通行の検証(バッチ)。
// 1) 駅間走行経路の中間点が中心線の左側(進行方向)に寄っているか
// 2) TryReserveForが進行方向左側のホーム線を選ぶか
// 3) 閉塞TryEnterが同一方向の重複進入を拒否し、逆方向は許すか
public static class BlockTest
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        TrackNetwork.Clear();

        // A(西)→B(東)、どちらも軸は東西(yaw90)
        var a = MakeStation(new Vector3(-700, 0, 0), 90, 6, 2, 2, "A");
        var b = MakeStation(new Vector3(700, 0, 0), 90, 6, 2, 2, "B");
        var seg = Connect(a, b);

        // 1) 左側通行: A→B(東行き)の経路中間は中心線の北(+z)側=左側にあるはず
        int trA = a.layout.stopTracks[0];
        int trB = b.layout.stopTracks[0];
        var path = Train.BuildLeg(a, trA, seg.SignAt(a), b, trB, seg.SignAt(b), 60f);
        var cum = RailKit.Cumulative(path);
        Vector3 mid, f;
        RailKit.Sample(path, cum, cum[cum.Length - 1] * 0.5f, out mid, out f);
        Debug.Log("BlockTest: eastbound mid z=" + mid.z.ToString("F2") + " (期待: +2.3=北=左側)");
        bool leftOk = mid.z > 1f;

        // 2) 左側ホーム選択: Bへ東から進入(enterSign=B側の接続端)
        int tb;
        bool r1 = b.TryReserveFor(seg.SignAt(b), out tb);
        float off = b.layout.trackOffsets[tb];
        // Bの進行方向はローカル-enterSign*z。左側はローカルx符号=enterSign
        bool platOk = r1 && Mathf.Sign(off) == seg.SignAt(b);
        Debug.Log("BlockTest: reservedTrack=" + tb + " offset=" + off.ToString("F1") +
            " enterSign=" + seg.SignAt(b) + " leftPlatform=" + platOk);
        b.Release(tb);

        // 3) 閉塞
        var t1 = new GameObject("T1").AddComponent<Train>();
        var t2 = new GameObject("T2").AddComponent<Train>();
        bool e1 = seg.TryEnter(a, t1);        // A→B 進入OK
        bool e2 = seg.TryEnter(a, t2);        // 同方向は拒否されるはず
        bool e3 = seg.TryEnter(b, t2);        // 逆方向(B→A)はOK
        seg.Leave(a, t1);
        bool e4 = seg.TryEnter(a, t2);        // 解放後はOK
        Debug.Log("BlockTest: block enter same=" + e1 + "/" + e2 + " opposite=" + e3 + " afterLeave=" + e4);
        bool blockOk = e1 && !e2 && e3 && e4;

        bool pass = leftOk && platOk && blockOk;
        Debug.Log("BlockTest: " + (pass ? "PASS" : "FAIL") +
            " (left=" + leftOk + " platform=" + platOk + " block=" + blockOk + ")");
        EditorApplication.Exit(pass ? 0 : 1);
    }

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
}
