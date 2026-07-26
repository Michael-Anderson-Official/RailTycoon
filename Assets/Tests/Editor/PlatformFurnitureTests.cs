using NUnit.Framework;
using UnityEngine;

// ホーム上の設備(ベンチ・自販機・階段・待合室・エレベーター)が線路側へ寄らないこと。
// ホーム縁は centerX - side*幅/2 にあるので線路は -side 側で、逃がす向きは +side。
// ここを取り違えると設備が線路の方へ寄り、警戒線と点字ブロックの帯を塞ぐ
// (2026-07-26にCodexレビューで指摘された退行)。
public class PlatformFurnitureTests
{
    [TearDown]
    public void TearDown()
    {
        EditModeTestHelpers.DestroyWorldRoot();
        TrackNetwork.Clear();
    }

    [Test]
    public void IslandPlatform_KeepsFurnitureCentred()
    {
        var layout = StationLayout.Compute(1, 2);   // 島式(両側が線路)
        Assert.That(Station.FurnitureAwayDirection(layout, 0), Is.EqualTo(0),
            "両側が線路なら中央のままにすること");
    }

    [TestCase(2, 2)]
    [TestCase(2, 4)]
    [TestCase(1, 1)]
    public void SinglePlatform_MovesFurnitureAwayFromTheTrack(int faces, int lines)
    {
        var layout = StationLayout.Compute(faces, lines);
        for (int pi = 0; pi < layout.platforms.Count; pi++)
        {
            int sideSum = 0, count = 0;
            foreach (var e in layout.edges)
                if (e.platformIndex == pi) { sideSum += e.side; count++; }
            if (count != 1) continue;   // 片面ホームだけを見る

            int away = Station.FurnitureAwayDirection(layout, pi);
            Assert.That(away, Is.EqualTo(sideSum),
                "片面ホームでは縁のsideと同じ向き(=線路の反対)へ逃がすこと");

            // 逃がした先が実際に線路から遠ざかっているか、縁の座標で確かめる
            var p = layout.platforms[pi];
            float edgeX = p.x - sideSum * p.y * 0.5f;              // 線路に面した縁
            float furnX = p.x + away * 0.9f;
            Assert.That(Mathf.Abs(furnX - edgeX), Is.GreaterThan(Mathf.Abs(p.x - edgeX)),
                "設備が縁(線路側)から遠ざかること");
        }
    }

    [Test]
    public void FurnitureMesh_StaysClearOfTheTrackSideBand()
    {
        // 実際に生成されたメッシュで、設備が警戒線・点字ブロックの帯へ出ていないこと
        foreach (var cfg in BuildController.StationPresets)
        {
            TrackNetwork.Clear();
            EditModeTestHelpers.DestroyWorldRoot();
            var st = EditModeTestHelpers.MakeStation(Vector3.zero, 0, 8, cfg.faces, cfg.lines, "P");
            var mf = st.transform.Find("Furniture").GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;

            foreach (var v in mf.sharedMesh.vertices)
            {
                // この頂点が属するホームを探す
                for (int pi = 0; pi < st.layout.platforms.Count; pi++)
                {
                    var p = st.layout.platforms[pi];
                    float visualW = Mathf.Max(2.6f, p.y - 0.02f);
                    if (Mathf.Abs(v.x - p.x) > visualW * 0.5f + 0.3f) continue;

                    foreach (var e in st.layout.edges)
                    {
                        if (e.platformIndex != pi) continue;
                        float edgeX = p.x - e.side * visualW * 0.5f;
                        // 縁から内側(+e.side方向)への距離
                        float inward = (v.x - edgeX) * e.side;
                        Assert.That(inward, Is.GreaterThan(1.0f),
                            cfg.label + ": 設備が線路側の帯(縁から1.0m)へ出ないこと" +
                            " 頂点x=" + v.x.ToString("F2") + " 縁x=" + edgeX.ToString("F2"));
                    }
                }
            }
        }
    }
}
