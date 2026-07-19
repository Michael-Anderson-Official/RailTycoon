using System.Collections.Generic;
using UnityEngine;

// メッシュ生成の共通部品(箱・ポリライン沿いスラブ・オフセット・スムージング)
public static class RailKit
{
    public class MeshData
    {
        public List<Vector3> v = new List<Vector3>();
        public List<int> t = new List<int>();

        public Mesh ToMesh()
        {
            var m = new Mesh();
            ApplyTo(m);
            return m;
        }

        // 既存Meshを差し替える(街のように増分更新するとき用)
        public void ApplyTo(Mesh m)
        {
            m.Clear();
            m.indexFormat = v.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            m.SetVertices(v);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
        }

        public void Clear() { v.Clear(); t.Clear(); }
    }

    // 面ごとに頂点を分けてエッジをシャープに保つ
    static readonly int[] BoxQuads = {
        0,2,3,1,  // -z
        5,7,6,4,  // +z
        4,6,2,0,  // -x
        1,3,7,5,  // +x
        2,6,7,3,  // +y
        4,0,1,5,  // -y
    };

    public static void AddBox(MeshData md, Vector3 center, Vector3 size, Quaternion rot)
    {
        Vector3 h = size * 0.5f;
        var c = new Vector3[8];
        for (int i = 0; i < 8; i++)
            c[i] = center + rot * new Vector3(
                ((i & 1) == 0 ? -h.x : h.x),
                ((i & 2) == 0 ? -h.y : h.y),
                ((i & 4) == 0 ? -h.z : h.z));
        for (int f = 0; f < 6; f++)
        {
            int b = md.v.Count;
            for (int k = 0; k < 4; k++) md.v.Add(c[BoxQuads[f * 4 + k]]);
            md.t.Add(b); md.t.Add(b + 1); md.t.Add(b + 2);
            md.t.Add(b); md.t.Add(b + 2); md.t.Add(b + 3);
        }
    }

    // ポリライン沿いのスラブ(断面: 幅2*half、厚みthick、上面が各点のy+yTop)
    public static void AddSlab(MeshData md, List<Vector3> pts, float half, float yTop, float thick)
    {
        if (pts.Count < 2) return;
        int b = md.v.Count;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 n = NormalAt(pts, i);
            Vector3 p = pts[i];
            md.v.Add(p + n * half + Vector3.up * yTop);          // 0 topL
            md.v.Add(p - n * half + Vector3.up * yTop);          // 1 topR
            md.v.Add(p + n * half + Vector3.up * (yTop - thick)); // 2 botL
            md.v.Add(p - n * half + Vector3.up * (yTop - thick)); // 3 botR
        }
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            int a = b + i * 4, c = b + (i + 1) * 4;
            // 上面
            md.t.Add(a + 0); md.t.Add(c + 0); md.t.Add(c + 1);
            md.t.Add(a + 0); md.t.Add(c + 1); md.t.Add(a + 1);
            // 左側面
            md.t.Add(a + 2); md.t.Add(c + 2); md.t.Add(c + 0);
            md.t.Add(a + 2); md.t.Add(c + 0); md.t.Add(a + 0);
            // 右側面
            md.t.Add(a + 1); md.t.Add(c + 1); md.t.Add(c + 3);
            md.t.Add(a + 1); md.t.Add(c + 3); md.t.Add(a + 3);
        }
    }

    public static Vector3 NormalAt(List<Vector3> pts, int i)
    {
        Vector3 tan;
        if (i == 0) tan = pts[1] - pts[0];
        else if (i == pts.Count - 1) tan = pts[i] - pts[i - 1];
        else tan = pts[i + 1] - pts[i - 1];
        tan.y = 0;
        if (tan.sqrMagnitude < 1e-8f) tan = Vector3.forward;
        tan.Normalize();
        return new Vector3(-tan.z, 0, tan.x); // 進行方向左手
    }

    public static List<Vector3> Offset(List<Vector3> pts, float lateral)
    {
        var r = new List<Vector3>(pts.Count);
        for (int i = 0; i < pts.Count; i++) r.Add(pts[i] + NormalAt(pts, i) * lateral);
        return r;
    }

    // Chaikin平滑化(端点固定)
    public static List<Vector3> Chaikin(List<Vector3> pts, int iterations)
    {
        var cur = pts;
        for (int it = 0; it < iterations; it++)
        {
            if (cur.Count < 3) return cur;
            var next = new List<Vector3> { cur[0] };
            for (int i = 0; i + 1 < cur.Count; i++)
            {
                next.Add(Vector3.Lerp(cur[i], cur[i + 1], 0.25f));
                next.Add(Vector3.Lerp(cur[i], cur[i + 1], 0.75f));
            }
            next.Add(cur[cur.Count - 1]);
            cur = next;
        }
        return cur;
    }

    // 1本の線路(バラスト+レール2本+枕木)をMeshDataに追加
    public static void AddTrack(MeshData ballast, MeshData rail, MeshData tie, List<Vector3> center)
    {
        AddSlab(ballast, center, 1.9f, 0.25f, 0.35f);
        AddSlab(rail, Offset(center, 0.75f), 0.05f, 0.45f, 0.15f);
        AddSlab(rail, Offset(center, -0.75f), 0.05f, 0.45f, 0.15f);
        float total = PathLength(center);
        var cum = Cumulative(center);
        for (float s = 1.5f; s < total; s += 4f)
        {
            Vector3 p, f;
            Sample(center, cum, s, out p, out f);
            AddBox(tie, p + Vector3.up * 0.28f, new Vector3(2.3f, 0.12f, 0.25f),
                Quaternion.LookRotation(f, Vector3.up));
        }
    }

    public static float[] Cumulative(List<Vector3> pts)
    {
        var c = new float[pts.Count];
        for (int i = 1; i < pts.Count; i++) c[i] = c[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
        return c;
    }

    public static float PathLength(List<Vector3> pts)
    {
        float l = 0;
        for (int i = 1; i < pts.Count; i++) l += Vector3.Distance(pts[i - 1], pts[i]);
        return l;
    }

    // 弧長sの位置と接線方向を返す
    public static void Sample(List<Vector3> pts, float[] cum, float s, out Vector3 pos, out Vector3 fwd)
    {
        float total = cum[cum.Length - 1];
        s = Mathf.Clamp(s, 0, total);
        int lo = 0, hi = cum.Length - 1;
        while (lo + 1 < hi)
        {
            int mid = (lo + hi) / 2;
            if (cum[mid] <= s) lo = mid; else hi = mid;
        }
        float segLen = Mathf.Max(cum[hi] - cum[lo], 1e-6f);
        float t = (s - cum[lo]) / segLen;
        pos = Vector3.Lerp(pts[lo], pts[hi], t);
        fwd = (pts[hi] - pts[lo]).normalized;
        if (fwd.sqrMagnitude < 0.5f) fwd = Vector3.forward;
    }

    public static GameObject MeshGO(string name, Mesh mesh, Material mat, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }
}
