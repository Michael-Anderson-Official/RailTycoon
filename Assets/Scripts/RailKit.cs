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

    // 2D断面(xy、閉じた輪郭を反時計回り)をzStart..zEndへ押し出す。両端に妻面を張る。
    // 車体・屋根の丸みを一定断面で作るのに使う
    public static void AddExtrude(MeshData md, Vector2[] section, float zStart, float zEnd)
    {
        int n = section.Length;
        int b0 = md.v.Count;
        for (int i = 0; i < n; i++) md.v.Add(new Vector3(section[i].x, section[i].y, zStart));
        int b1 = md.v.Count;
        for (int i = 0; i < n; i++) md.v.Add(new Vector3(section[i].x, section[i].y, zEnd));
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            md.t.Add(b0 + i); md.t.Add(b0 + j); md.t.Add(b1 + j);
            md.t.Add(b0 + i); md.t.Add(b1 + j); md.t.Add(b1 + i);
        }
        // 妻面(三角ファン、輪郭は凸に近いので十分)
        for (int i = 1; i + 1 < n; i++)
        {
            md.t.Add(b0); md.t.Add(b0 + i + 1); md.t.Add(b0 + i);       // zStart(外向き-z)
            md.t.Add(b1); md.t.Add(b1 + i); md.t.Add(b1 + i + 1);       // zEnd(外向き+z)
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

    // Offset()と同じだが、始点/終点だけは近傍点からの近似接線(NormalAt)ではなく、
    // 呼び出し側が知っている正確な接線(tan0/tan1)を使う。エルミート曲線(HermitePath)の
    // 両端は、駅側の線路の接線と厳密に一致させたい(近似だと点数が少ないほど数十cm〜
    // 1m弱ずれ、駅の自前スロートのメッシュとの継ぎ目に隙間が見えてしまう)ため
    public static List<Vector3> OffsetWithEndTangents(List<Vector3> pts, float lateral, Vector3 tan0, Vector3 tan1)
    {
        var r = new List<Vector3>(pts.Count);
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 n = i == 0 ? LeftOf(tan0) : i == pts.Count - 1 ? LeftOf(tan1) : NormalAt(pts, i);
            r.Add(pts[i] + n * lateral);
        }
        return r;
    }

    static Vector3 LeftOf(Vector3 tan)
    {
        tan.y = 0;
        if (tan.sqrMagnitude < 1e-8f) tan = Vector3.forward;
        tan.Normalize();
        return new Vector3(-tan.z, 0, tan.x);
    }

    // p0からp1へ、それぞれtan0/tan1の向き(大きさは無視、内部で弦長の0.35倍へ
    // 正規化する)へ滑らかに発着する3次エルミート曲線をn+1点に離散化する。
    // 駅同士を結ぶ線路が、駅を出た瞬間に折れ曲がらず滑らかにカーブするようにするため
    // (直線Lerpだと、斜めに向き合う駅同士がキンク(急な折れ)で繋がってしまう)
    public static List<Vector3> HermitePath(Vector3 p0, Vector3 tan0, Vector3 p1, Vector3 tan1, int n)
    {
        float mag = Vector3.Distance(p0, p1) * 0.35f;
        Vector3 t0 = tan0.sqrMagnitude > 1e-8f ? tan0.normalized * mag : Vector3.zero;
        Vector3 t1 = tan1.sqrMagnitude > 1e-8f ? tan1.normalized * mag : Vector3.zero;
        var pts = new List<Vector3>(n + 1);
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n;
            float tt = t * t, ttt = tt * t;
            float h00 = 2f * ttt - 3f * tt + 1f;
            float h10 = ttt - 2f * tt + t;
            float h01 = -2f * ttt + 3f * tt;
            float h11 = ttt - tt;
            pts.Add(h00 * p0 + h10 * t0 + h01 * p1 + h11 * t1);
        }
        return pts;
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

    public const float Gauge = 0.7175f;   // 軌間1.435mの半分
    public const float RailTop = 0.55f;    // レール頭頂の高さ
    public const float TieSpacing = 0.95f; // 枕木の間隔(実物~0.6mを少し粗く)

    // 1本の線路(バラスト肩+枕木+2本レール)をMeshDataに追加。よりリアルな断面
    public static void AddTrack(MeshData ballast, MeshData rail, MeshData tie, List<Vector3> center)
    {
        // バラスト: 幅広の低い基部 + その上に枕木が載る一段高い天端(=肩のある台形)
        AddSlab(ballast, center, 2.5f, 0.22f, 0.22f);
        AddSlab(ballast, center, 1.95f, 0.36f, 0.16f);

        float total = PathLength(center);
        var cum = Cumulative(center);
        // 枕木(密に敷く)。バラスト天端に半分埋まる高さ
        for (float s = 0.6f; s < total; s += TieSpacing)
        {
            Vector3 p, f;
            Sample(center, cum, s, out p, out f);
            AddBox(tie, p + Vector3.up * 0.34f, new Vector3(2.6f, 0.17f, 0.26f),
                Quaternion.LookRotation(f, Vector3.up));
        }
        // レール(枕木の上に立つ。頭部・腹部の2段で断面らしく)
        AddSlab(rail, Offset(center, Gauge), 0.045f, RailTop, 0.16f);
        AddSlab(rail, Offset(center, Gauge), 0.075f, RailTop - 0.13f, 0.05f);   // 底部フランジ
        AddSlab(rail, Offset(center, -Gauge), 0.045f, RailTop, 0.16f);
        AddSlab(rail, Offset(center, -Gauge), 0.075f, RailTop - 0.13f, 0.05f);
    }

    // 両渡り線(シザースクロッシング)を直線の複線上に描く。centerを中心、dir=線路方向。
    // 左右トラックは center±perp*2.3。直進本線レール+交差2本(中央ダイヤモンドクロッシング)+
    // 各分岐のフログ・トングレール・ガードレール・転てつ機・長い分岐枕木・バラスト。
    public static void AddCrossover(MeshData rail, MeshData metal, MeshData box, MeshData tie,
        MeshData ballast, Vector3 center, Vector3 dir)
    {
        dir.y = 0;
        if (dir.sqrMagnitude < 1e-4f) return;
        dir.Normalize();
        var perp = Vector3.Cross(Vector3.up, dir).normalized;
        const float g = Gauge, ry = RailTop, half = 2.3f, d = 10f;
        var up = Vector3.up;

        // バラスト
        var cp = new List<Vector3> { center - dir * (d + 3f), center + dir * (d + 3f) };
        AddSlab(ballast, cp, 3.7f, 0.36f, 0.16f);
        // 分岐枕木(線路方向に直交・中央ほど長い)
        var tieRot = Quaternion.LookRotation(perp, up);
        int nT = Mathf.Max(4, Mathf.CeilToInt(d * 2 / TieSpacing));
        for (int i = 0; i <= nT; i++)
        {
            float tt = i / (float)nT;
            var p = Vector3.Lerp(center - dir * d, center + dir * d, tt);
            float w = Mathf.Lerp(5.4f, 7.0f, 1f - Mathf.Abs(tt - 0.5f) * 2f);
            AddBox(tie, p + up * 0.34f, new Vector3(0.26f, 0.17f, w), tieRot);
        }
        // 直進本線レール(両トラック)
        var rot = Quaternion.LookRotation(dir, up);
        for (int s = -1; s <= 1; s += 2)
        {
            var tc = center + perp * (half * s);
            AddBox(rail, tc + perp * g + up * ry, new Vector3(0.09f, 0.16f, (d + 4f) * 2f), rot);
            AddBox(rail, tc - perp * g + up * ry, new Vector3(0.09f, 0.16f, (d + 4f) * 2f), rot);
        }
        // 交差する2本の渡り線 → 中央ダイヤモンドクロッシングX
        var A1 = center - perp * half - dir * d; var B1 = center + perp * half + dir * d;
        var A2 = center + perp * half - dir * d; var B2 = center - perp * half + dir * d;
        DiagRail(rail, A1, B1, g, ry);
        DiagRail(rail, A2, B2, g, ry);
        AddBox(metal, center + up * (ry - 0.02f), new Vector3(1.5f, 0.2f, 1.5f), rot);
        // 4分岐(転てつ機は本線の外側へ。左本線A1/B2は-perp、右本線A2/B1は+perp側)
        CrossPoint(metal, box, A1, dir, perp, -1, g, ry);
        CrossPoint(metal, box, B2, dir, perp, -1, g, ry);
        CrossPoint(metal, box, A2, dir, perp, +1, g, ry);
        CrossPoint(metal, box, B1, dir, perp, +1, g, ry);
    }

    static void DiagRail(MeshData rail, Vector3 a, Vector3 b, float g, float ry)
    {
        var dd = b - a; dd.y = 0; float len = dd.magnitude;
        if (len < 0.1f) return;
        dd /= len;
        var pp = Vector3.Cross(Vector3.up, dd).normalized;
        var rot = Quaternion.LookRotation(dd, Vector3.up);
        var mid = (a + b) * 0.5f + Vector3.up * ry;
        AddBox(rail, mid + pp * g, new Vector3(0.09f, 0.16f, len), rot);
        AddBox(rail, mid - pp * g, new Vector3(0.09f, 0.16f, len), rot);
    }

    // 分岐1か所: トングレール(可動)・フログ・ガードレール・転てつ機。at=本線側の分岐点
    static void CrossPoint(MeshData metal, MeshData box, Vector3 at, Vector3 dir, Vector3 perp, int outSide, float g, float ry)
    {
        var rot = Quaternion.LookRotation(dir, Vector3.up);
        var up = Vector3.up;
        AddBox(metal, at - perp * (outSide * g) + up * ry, new Vector3(0.1f, 0.15f, 5.5f), rot);        // トングレール
        AddBox(metal, at + up * (ry - 0.02f), new Vector3(0.55f, 0.2f, 1.6f), rot);                     // フログ
        AddBox(metal, at - perp * (outSide * (g - 0.2f)) + up * ry, new Vector3(0.08f, 0.15f, 3.2f), rot); // ガードレール(内側)
        var mach = at + perp * (2.9f * outSide);
        AddBox(box, mach + up * 0.42f, new Vector3(0.9f, 0.72f, 1.3f), rot);
        AddBox(metal, mach + up * 0.95f, new Vector3(0.14f, 0.5f, 0.14f), rot * Quaternion.Euler(0, 0, 22f));
        var rodEnd = at + perp * (0.9f * outSide);
        var rdir = rodEnd - mach; rdir.y = 0;
        if (rdir.magnitude > 0.1f)
            AddBox(metal, (mach + rodEnd) * 0.5f + up * 0.34f, new Vector3(0.08f, 0.08f, rdir.magnitude),
                Quaternion.LookRotation(rdir.normalized, Vector3.up));
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
