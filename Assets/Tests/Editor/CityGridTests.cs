using NUnit.Framework;
using UnityEngine;

// 街グリッド(CityGrid)の検証。
//
// CityGrid.Enabled は 2026-07-20 のコミット 23fbb17 (「番線指定運転・車両ビジュアル刷新・
// 更地路線化(街オフ)+白飛び調整」) で意図的に false へ変更されて以降、現在まで一度も
// true に戻されていない(git log --follow で確認、以降のコミットはCityGrid.csに一切触れていない)。
// この状態で以下のアサーションを実行すると、CityGrid.Develop() が即returnするため
// 建物メッシュ頂点0・道路0となり、必ず失敗する。
//
// これはテストの不備ではなく、「街機能が現在は無効な仮実装である」という製品状態を
// 正しく検知している。街機能を復活させる/させないは別途判断事項のため、本テストは
// 削除・弱体化せず [Ignore] で無効化状態を明示する(採用前にユーザー承認済み)。
[Ignore("CityGrid.Enabled=false (23fbb17, 2026-07-20時点で意図的に無効化・以降変更なし)。" +
        "街機能を復活させ CityGrid.Enabled=true にしたらこのIgnoreを外すこと。")]
public class CityGridTests
{
    [SetUp]
    public void SetUp()
    {
        TrackNetwork.Clear();
        Bootstrap.BuildEnvironment();
        CityGrid.Init();
    }

    [TearDown]
    public void TearDown()
    {
        EditModeTestHelpers.DestroyWorldRoot();
        TrackNetwork.Clear();
    }

    [Test]
    public void Develop_BuildsBuildingsWithoutIntersectingStationsOrTracks()
    {
        var a = EditModeTestHelpers.MakeStation(new Vector3(-500, 0, -150), 15, 10, 2, 4, "中央");
        var b = EditModeTestHelpers.MakeStation(new Vector3(450, 0, 200), -20, 6, 2, 2, "本町");
        EditModeTestHelpers.Connect(a, b);
        a.ForceDev(9f);
        b.ForceDev(5f);
        CityGrid.FlushIfDirty();

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

        Assert.That(total, Is.GreaterThan(100), "建物メッシュが生成されていること");
        Assert.That(intoStation, Is.EqualTo(0), "建物が駅構内に食い込んでいないこと");
        Assert.That(intoTrack, Is.EqualTo(0), "建物が線路に食い込んでいないこと");

        var road = GameObject.Find("CityRoad");
        int roadBoxes = road != null ? road.GetComponent<MeshFilter>().sharedMesh.vertexCount / 24 : 0;
        Assert.That(roadBoxes, Is.GreaterThan(0), "道路が生成されていること");
    }

    static float SegDist(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float d = Vector2.Dot(ab, ab);
        float t = d < 1e-6f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, ab) / d);
        return Vector2.Distance(p, a + ab * t);
    }
}
