using NUnit.Framework;

// StationLayout.Compute の代表ケース・境界ケース。
// 純粋なstatic計算(Unityオブジェクト生成なし)なのでセットアップ/後片付けは不要。
public class StationLayoutTests
{
    // ドキュメントコメント(StationLayout.cs冒頭)に明記された代表配置パターン
    [Test]
    public void Slots_IslandPlatform_1Face2Lines()
    {
        // 1面2線 → 線 面 線(島式)
        var slots = StationLayout.Slots(1, 2);
        Assert.That(slots, Is.EqualTo(new[] { 1, 1 }));
    }

    [Test]
    public void Slots_OppositePlatforms_2Faces2Lines()
    {
        // 2面2線 → 面 線線 面(相対式)
        var slots = StationLayout.Slots(2, 2);
        Assert.That(slots, Is.EqualTo(new[] { 0, 2, 0 }));
    }

    [Test]
    public void Slots_4Lines_2Faces()
    {
        // 2面4線 → 線 面 線線 面 線
        var slots = StationLayout.Slots(2, 4);
        Assert.That(slots, Is.EqualTo(new[] { 1, 2, 1 }));
    }

    [Test]
    public void Slots_3Lines_2Faces()
    {
        // 2面3線 → 線 面 線線 面
        var slots = StationLayout.Slots(2, 3);
        Assert.That(slots, Is.EqualTo(new[] { 1, 2, 0 }));
    }

    // 境界: 最小構成(1面1線)
    [Test]
    public void Compute_MinimalStation_1Face1Line()
    {
        var r = StationLayout.Compute(1, 1);

        Assert.That(r.trackOffsets.Length, Is.EqualTo(1), "線は1本のはず");
        Assert.That(r.platforms.Count, Is.EqualTo(1), "面は1つのはず");
        Assert.That(r.stopTracks.Count, Is.EqualTo(1), "唯一の線がホームに接し停車可能なはず");
        Assert.That(r.totalWidth, Is.GreaterThan(0f));
    }

    // 境界: README記載の面数上限(4面)
    [Test]
    public void Compute_MaxFaces_4Faces8Lines()
    {
        var r = StationLayout.Compute(4, 8);

        Assert.That(r.trackOffsets.Length, Is.EqualTo(8), "線の本数は指定どおり8本");
        Assert.That(r.platforms.Count, Is.EqualTo(4), "面の数は指定どおり4面");
        // 各線オフセットは重複しない(スロットごとに配置がずれている)
        var offsets = new System.Collections.Generic.HashSet<float>(r.trackOffsets);
        Assert.That(offsets.Count, Is.EqualTo(r.trackOffsets.Length), "線の中心オフセットに重複が無いこと");
    }

    [Test]
    public void Compute_TrackOffsetsAreSymmetricAroundCenter()
    {
        // 対称なfaces/linesなら、中心(駅の中心線)を基準に線配置も対称になるはず
        var r = StationLayout.Compute(2, 4);
        float sum = 0f;
        foreach (var o in r.trackOffsets) sum += o;
        Assert.That(sum, Is.EqualTo(0f).Within(0.01f), "対称配置なのでオフセットの合計はほぼ0");
    }

    [Test]
    public void Compute_StopTracksAreWithinTrackRange()
    {
        var r = StationLayout.Compute(2, 3);
        foreach (var idx in r.stopTracks)
            Assert.That(idx, Is.InRange(0, r.trackOffsets.Length - 1), "停車可能線のindexは線配列の範囲内");
    }
}
