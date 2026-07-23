using UnityEngine;

// EditModeテスト共通のシーン構築ヘルパー。
// 既存の手動バッチテスト(BlockTest等)のMakeStation/Connectと同じロジックを
// NUnitテストから再利用できる形にしたもの。
public static class EditModeTestHelpers
{
    public static Station MakeStation(Vector3 pos, float yaw, int cars, int faces, int lines, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(BuildController.WorldRoot, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        var st = go.AddComponent<Station>();
        st.id = ++TrackNetwork.stationIdCounter; // M2-C: BuildController.ConfirmStationと同じ安定ID付与
        st.cars = cars;
        st.faces = faces;
        st.lines = lines;
        st.stationName = name;
        st.Build();
        TrackNetwork.stations.Add(st);
        return st;
    }

    // 両駅のスロート端同士のうち最短の組み合わせで結ぶ(既存テスト群と同一ロジック)
    public static TrackSegment Connect(Station a, Station b)
    {
        int bestSa = 1, bestSb = 1;
        float best = float.MaxValue;
        for (int sa = -1; sa <= 1; sa += 2)
            for (int sb = -1; sb <= 1; sb += 2)
            {
                float d = Vector3.Distance(a.End(sa), b.End(sb));
                if (d < best) { best = d; bestSa = sa; bestSb = sb; }
            }
        var seg = new TrackSegment { id = ++TrackNetwork.segmentIdCounter, a = a, b = b, signA = bestSa, signB = bestSb };
        seg.Build(BuildController.WorldRoot);
        TrackNetwork.segments.Add(seg);
        return seg;
    }

    // WorldRootごと破棄すれば、その配下に生成した駅・線路・列車が全て片付く
    public static void DestroyWorldRoot()
    {
        var root = BuildController.WorldRoot;
        if (root != null) Object.DestroyImmediate(root.gameObject);
    }
}
