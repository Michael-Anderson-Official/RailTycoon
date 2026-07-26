using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// 停車線が本線側と逆で渡り線を渡る場合、駅構内の経路がホーム部の番線座標を
// 保つこと。かつては渡り線より手前のxを一律±2.3(スロート端での収束位置)へ
// 潰しており、島式(番線±5.48)や2面4線(外側±13.26)では列車がホームの内側を
// 走っていた。相対式は番線がちょうど±2.3なので露見しなかった。
// 2026-07-26にユーザーが実機で発見(折り返しで復路の番線を変える系統)。
public class CrossoverPathTests
{
    [SetUp]
    public void SetUp() => TrackNetwork.Clear();

    [TearDown]
    public void TearDown()
    {
        EditModeTestHelpers.DestroyWorldRoot();
        TrackNetwork.Clear();
    }

    // 駅構内の全番線の中心線(ワールド)
    static List<List<Vector3>> StationRails(Station st)
    {
        var rails = new List<List<Vector3>>();
        for (int i = 0; i < st.layout.trackOffsets.Length; i++)
        {
            var loc = st.TrackCentreLocal(i);
            if (loc == null) continue;
            var w = new List<Vector3>();
            foreach (var q in loc) w.Add(st.transform.TransformPoint(q));
            rails.Add(w);
        }
        foreach (int sign in new[] { -1, 1 })
        {
            float cz = sign * (st.HalfLen + StationLayout.ThroatLen - StationLayout.LeadLen * 0.5f);
            float d = RailKit.CrossoverHalfLength;
            float off = RailDimensions.MainTrackOffset;
            rails.Add(RailKit.CrossoverPath(
                st.transform.TransformPoint(new Vector3(off, 0, cz - sign * d)),
                st.transform.TransformPoint(new Vector3(-off, 0, cz + sign * d)),
                st.Axis * sign));
            rails.Add(RailKit.CrossoverPath(
                st.transform.TransformPoint(new Vector3(-off, 0, cz - sign * d)),
                st.transform.TransformPoint(new Vector3(off, 0, cz + sign * d)),
                st.Axis * sign));
        }
        return rails;
    }

    static float DistToLine(Vector3 p, List<Vector3> line)
    {
        float best = float.MaxValue;
        for (int i = 0; i + 1 < line.Count; i++)
        {
            Vector3 s0 = line[i], e = line[i + 1], d = e - s0;
            float len2 = d.sqrMagnitude;
            float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(p - s0, d) / len2);
            best = Mathf.Min(best, Vector3.Distance(p, s0 + d * t));
        }
        return best;
    }

    // 番線が本線側と逆になる組み合わせを含め、駅構内で経路が中心線から外れないこと
    [TestCase(1, 2, "島式")]
    [TestCase(2, 4, "2面4線")]
    [TestCase(3, 2, "3面2線")]
    [TestCase(2, 2, "相対式")]
    public void LegThroughACrossover_StaysOnTheStoredCentreLine(int faces, int lines, string label)
    {
        var a = EditModeTestHelpers.MakeStation(Vector3.zero, 0, 10, faces, lines, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 2400), 0, 10, faces, lines, "B");
        var seg = EditModeTestHelpers.Connect(a, b);
        a.RebuildTrackVisual(); b.RebuildTrackVisual();

        var railsA = StationRails(a);
        var railsB = StationRails(b);

        // 発着の番線を総当たり。どれかが必ず渡り線を渡る側になる
        foreach (int ta in a.StopTracks)
            foreach (int tb in b.StopTracks)
            {
                var leg = Train.BuildLeg(a, ta, seg.SignAt(a), b, tb, seg.SignAt(b), 100f);
                float worst = 0f; Vector3 worstP = Vector3.zero;
                foreach (var p in leg)
                {
                    // 駅構内(ホーム部)だけを見る。駅間はTrackSegment側のテストが見ている
                    var la = a.transform.InverseTransformPoint(p);
                    var lb = b.transform.InverseTransformPoint(p);
                    List<List<Vector3>> rails = null;
                    if (Mathf.Abs(la.z) <= a.HalfLen) rails = railsA;
                    else if (Mathf.Abs(lb.z) <= b.HalfLen) rails = railsB;
                    if (rails == null) continue;

                    float best = float.MaxValue;
                    foreach (var r in rails) best = Mathf.Min(best, DistToLine(p, r));
                    if (best > worst) { worst = best; worstP = p; }
                }
                Assert.That(worst, Is.LessThan(0.05f),
                    label + " 発" + ta + "→着" + tb +
                    ": 駅構内で経路が中心線から外れないこと(最大" + worst.ToString("F2") +
                    "m, x=" + worstP.x.ToString("F2") + " z=" + worstP.z.ToString("F0") + ")");
            }
    }

    // ホーム部では、経路が必ずその番線の座標にいること(±2.3へ潰されていないこと)
    [Test]
    public void LegThroughACrossover_KeepsThePlatformTrackOffset()
    {
        var a = EditModeTestHelpers.MakeStation(Vector3.zero, 0, 10, 1, 2, "A");   // 島式(±5.48)
        var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 2400), 0, 10, 1, 2, "B");
        var seg = EditModeTestHelpers.Connect(a, b);
        a.RebuildTrackVisual(); b.RebuildTrackVisual();

        foreach (int ta in a.StopTracks)
        {
            var leg = Train.BuildLeg(a, ta, seg.SignAt(a), b, b.StopTracks[0], seg.SignAt(b), 100f);
            float want = a.layout.trackOffsets[ta];
            foreach (var p in leg)
            {
                var loc = a.transform.InverseTransformPoint(p);
                // ホームの中央寄り(停車範囲の内側)は必ずその番線の座標
                if (Mathf.Abs(loc.z) > a.HalfLen - 20f) continue;
                Assert.That(loc.x, Is.EqualTo(want).Within(0.15f),
                    "発" + ta + ": ホーム部で番線" + want.ToString("F2") +
                    "の上にいること(z=" + loc.z.ToString("F0") + " のx=" + loc.x.ToString("F2") + ")");
            }
        }
    }
}
