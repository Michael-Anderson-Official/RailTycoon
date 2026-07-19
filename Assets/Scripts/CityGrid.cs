using System.Collections.Generic;
using UnityEngine;

// 全駅共通のワールドグリッドに街を発展させる(Cities Skylines風)。
// 道路を格子状に敷き、区画セルに建物を建てる。駅構内・線路上には建物を置かない
// (道路は線路と交差してよいが駅構内は避ける)。配置は決定的(乱数不使用)なので
// セーブには街の詳細を持たず、駅のdevから毎回同じ街を再構築できる。
public static class CityGrid
{
    public const float Cell = 24f;   // 1区画セルの1辺
    public const int Period = 6;     // Period間隔の格子線が道路(=5棟セル+1道路セル)
    const float FootMax = 17f;       // 建物の最大footprint
    const float StationMargin = 14f; // 駅構内から建物を離す余白
    const float RoadStationMargin = 4f;
    const float TrackClear = 11f;    // 線路中心線から建物を離す半径

    static Transform root;
    static readonly HashSet<long> occupied = new HashSet<long>(); // 建物を建てた/確定したセル
    static readonly HashSet<long> roadDone = new HashSet<long>();

    static RailKit.MeshData mdLow, mdMid, mdHigh, mdRoad;
    static Mesh meshLow, meshMid, meshHigh, meshRoad;
    static bool dirty;

    static long Key(int cx, int cy) => ((long)cx << 32) ^ (uint)cy;
    static int Mod(int a, int m) { int r = a % m; return r < 0 ? r + m : r; }
    static bool IsRoad(int cx, int cy) => Mod(cx, Period) == 0 || Mod(cy, Period) == 0;
    static Vector3 CellCenter(int cx, int cy) => new Vector3(cx * Cell, 0, cy * Cell);

    public static void Init()
    {
        if (root != null) Object.Destroy(root.gameObject);
        occupied.Clear();
        roadDone.Clear();
        root = new GameObject("City").transform;

        mdLow = new RailKit.MeshData();
        mdMid = new RailKit.MeshData();
        mdHigh = new RailKit.MeshData();
        mdRoad = new RailKit.MeshData();
        meshLow = MakeGO("CityLow", "BuildingLow", out _);
        meshMid = MakeGO("CityMid", "BuildingMid", out _);
        meshHigh = MakeGO("CityHigh", "BuildingHigh", out _);
        meshRoad = MakeGO("CityRoad", "Road", out _);
        dirty = false;
    }

    static Mesh MakeGO(string name, string mat, out GameObject go)
    {
        var mesh = new Mesh();
        go = RailKit.MeshGO(name, mesh, MatLib.Get(mat), root);
        return mesh;
    }

    static int TargetFor(float dev) => 4 + (int)(dev * 5f);

    // 駅の発展度に応じて周辺セルを埋める。決定的(近い順+セル座標ハッシュ)
    public static void Develop(Station st)
    {
        if (root == null) Init();
        int target = TargetFor(st.dev);
        int need = target - st.developed;
        if (need <= 0) return;

        Vector3 p = st.transform.position;
        float R = 55f + st.dev * 22f;
        int scx = Mathf.RoundToInt(p.x / Cell);
        int scy = Mathf.RoundToInt(p.z / Cell);
        int reach = Mathf.CeilToInt(R / Cell) + 1;

        var cand = new List<(float d, int cx, int cy, Vector3 c)>();
        for (int dy = -reach; dy <= reach; dy++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                int cx = scx + dx, cy = scy + dy;
                if (IsRoad(cx, cy)) continue;
                if (occupied.Contains(Key(cx, cy))) continue;
                var c = CellCenter(cx, cy);
                float d = Vector2.Distance(new Vector2(c.x, c.z), new Vector2(p.x, p.z));
                if (d > R) continue;
                if (BlockedForBuilding(c)) continue;
                cand.Add((d, cx, cy, c));
            }
        cand.Sort((a, b) => a.d.CompareTo(b.d));

        int placed = 0;
        for (int i = 0; i < cand.Count && placed < need; i++)
        {
            var (d, cx, cy, c) = cand[i];
            occupied.Add(Key(cx, cy));
            AddBuilding(c, d);
            AddRoadsAround(cx, cy);
            st.developed++;
            placed++;
        }
        if (placed > 0) dirty = true;
    }

    // 駅構内の矩形内、または線路中心線の近傍なら建物不可
    static bool BlockedForBuilding(Vector3 c)
    {
        foreach (var st in TrackNetwork.stations)
        {
            if (st == null) continue;
            var local = st.transform.InverseTransformPoint(new Vector3(c.x, 0, c.z));
            float halfW = st.layout.totalWidth * 0.5f + StationMargin;
            float halfL = st.HalfLen + StationLayout.ThroatLen + StationMargin;
            if (Mathf.Abs(local.x) < halfW && Mathf.Abs(local.z) < halfL) return true;
        }
        var cxz = new Vector2(c.x, c.z);
        foreach (var seg in TrackNetwork.segments)
        {
            if (seg == null) continue;
            var a = seg.EndA; var b = seg.EndB;
            if (SegDist(cxz, new Vector2(a.x, a.z), new Vector2(b.x, b.z)) < TrackClear) return true;
        }
        return false;
    }

    static bool BlockedForRoad(Vector3 c)
    {
        foreach (var st in TrackNetwork.stations)
        {
            if (st == null) continue;
            var local = st.transform.InverseTransformPoint(new Vector3(c.x, 0, c.z));
            float halfW = st.layout.totalWidth * 0.5f + RoadStationMargin;
            float halfL = st.HalfLen + StationLayout.ThroatLen + RoadStationMargin;
            if (Mathf.Abs(local.x) < halfW && Mathf.Abs(local.z) < halfL) return true;
        }
        return false;
    }

    static float SegDist(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float denom = Vector2.Dot(ab, ab);
        float t = denom < 1e-6f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, ab) / denom);
        return Vector2.Distance(p, a + ab * t);
    }

    static void AddBuilding(Vector3 center, float distToStation)
    {
        uint h = (uint)(Mathf.RoundToInt(center.x) * 73856093 ^ Mathf.RoundToInt(center.z) * 19349663);
        float r = (h & 0xFFFF) / 65535f;
        RailKit.MeshData md;
        float baseH, range, foot;
        if (distToStation < 65f) { md = mdHigh; baseH = 22f; range = 20f; foot = FootMax; }
        else if (distToStation < 140f) { md = mdMid; baseH = 12f; range = 12f; foot = FootMax - 1f; }
        else { md = mdLow; baseH = 6f; range = 6f; foot = FootMax - 3f; }
        float ht = baseH + r * range;
        float fw = foot * (0.85f + 0.15f * r);
        float rot = ((h >> 16) & 3) * 3f - 4.5f; // わずかに向きを散らす
        var rq = Quaternion.Euler(0, rot, 0);
        RailKit.AddBox(md, center + Vector3.up * (ht * 0.5f), new Vector3(fw, ht, fw), rq);
        // 屋上のアクセント(塔屋/設備)
        float cap = 1.5f + r * 2.5f;
        RailKit.AddBox(md, center + Vector3.up * (ht + cap * 0.5f),
            new Vector3(fw * 0.45f, cap, fw * 0.45f), rq);
    }

    static void AddRoadsAround(int cx, int cy)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int rx = cx + dx, ry = cy + dy;
                if (!IsRoad(rx, ry)) continue;
                long k = Key(rx, ry);
                if (roadDone.Contains(k)) continue;
                var c = CellCenter(rx, ry);
                if (BlockedForRoad(c)) continue;
                roadDone.Add(k);
                RailKit.AddBox(mdRoad, c + Vector3.up * 0.06f, new Vector3(Cell, 0.12f, Cell), Quaternion.identity);
            }
    }

    // フレーム終わりにまとめてメッシュ反映(Bootstrap.LateUpdateから)
    public static void FlushIfDirty()
    {
        if (!dirty || root == null) return;
        dirty = false;
        mdLow.ApplyTo(meshLow);
        mdMid.ApplyTo(meshMid);
        mdHigh.ApplyTo(meshHigh);
        mdRoad.ApplyTo(meshRoad);
    }
}
