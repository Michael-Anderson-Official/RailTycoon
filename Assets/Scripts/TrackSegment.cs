using System.Collections.Generic;
using UnityEngine;

// 駅間を結ぶ複線区間。両駅のスロート収束点(End)同士を直線で結ぶ
public class TrackSegment
{
    public Station a, b;
    public int signA, signB;  // 各駅のどちらの端に接続するか(±1)
    public GameObject go;
    public float length;

    public Vector3 EndA => a.End(signA);
    public Vector3 EndB => b.End(signB);

    public int SignAt(Station s) => s == a ? signA : signB;
    public Station Other(Station s) => s == a ? b : a;

    public void Build(Transform parent)
    {
        if (go != null) Object.Destroy(go);
        go = new GameObject("Track_" + a.stationName + "_" + b.stationName);
        go.transform.SetParent(parent, false);
        var ballast = new RailKit.MeshData();
        var rail = new RailKit.MeshData();
        var tie = new RailKit.MeshData();
        var center = CenterPoints();
        RailKit.AddTrack(ballast, rail, tie, RailKit.Offset(center, 2.3f));
        RailKit.AddTrack(ballast, rail, tie, RailKit.Offset(center, -2.3f));
        RailKit.MeshGO("Ballast", ballast.ToMesh(), MatLib.Get("Ballast"), go.transform);
        RailKit.MeshGO("Rail", rail.ToMesh(), MatLib.Get("Rail"), go.transform);
        RailKit.MeshGO("Tie", tie.ToMesh(), MatLib.Get("Tie"), go.transform);
        length = Vector3.Distance(EndA, EndB);
    }

    // A端→B端の中心線(80m刻み)
    public List<Vector3> CenterPoints()
    {
        var p0 = EndA;
        var p1 = EndB;
        float d = Vector3.Distance(p0, p1);
        int n = Mathf.Max(2, Mathf.CeilToInt(d / 80f) + 1);
        var pts = new List<Vector3>(n);
        for (int i = 0; i < n; i++) pts.Add(Vector3.Lerp(p0, p1, i / (float)(n - 1)));
        return pts;
    }
}

// 駅と線路の台帳+到達可能判定
public static class TrackNetwork
{
    public static readonly List<Station> stations = new List<Station>();
    public static readonly List<TrackSegment> segments = new List<TrackSegment>();
    public static int nameCounter;

    static readonly Dictionary<Station, HashSet<Station>> reachCache = new Dictionary<Station, HashSet<Station>>();

    public static void Clear()
    {
        stations.Clear();
        segments.Clear();
        reachCache.Clear();
        nameCounter = 0;
    }

    public static void MarkDirty() => reachCache.Clear();

    public static TrackSegment Find(Station x, Station y)
    {
        foreach (var s in segments)
            if ((s.a == x && s.b == y) || (s.a == y && s.b == x)) return s;
        return null;
    }

    public static bool Connected(Station x, Station y) => Find(x, y) != null;

    // sと同じ連結成分の他駅(乗客の行き先候補)
    public static HashSet<Station> Reachable(Station s)
    {
        HashSet<Station> r;
        if (reachCache.TryGetValue(s, out r)) return r;
        r = new HashSet<Station>();
        var q = new Queue<Station>();
        var seen = new HashSet<Station> { s };
        q.Enqueue(s);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            foreach (var seg in segments)
            {
                if (seg.a != cur && seg.b != cur) continue;
                var o = seg.Other(cur);
                if (seen.Add(o))
                {
                    r.Add(o);
                    q.Enqueue(o);
                }
            }
        }
        reachCache[s] = r;
        return r;
    }
}
