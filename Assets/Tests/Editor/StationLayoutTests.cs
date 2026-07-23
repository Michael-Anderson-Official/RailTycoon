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

    // ==================== M2-D: 3面2線・ラウンドロビン化 ====================

    [Test]
    public void Slots_3Faces2Lines_OneTrackPerInnerGap()
    {
        // 3面2線 → 外側|1番線|島式|2番線|外側(各内側隙間に1線ずつ)
        var slots = StationLayout.Slots(3, 2);
        Assert.That(slots, Is.EqualTo(new[] { 0, 1, 1, 0 }));
    }

    [Test]
    public void Compute_3Faces2Lines_TwoPhysicalTracksThreePlatforms()
    {
        var r = StationLayout.Compute(3, 2);

        Assert.That(r.trackOffsets.Length, Is.EqualTo(2), "物理線は2本のはず");
        Assert.That(r.platforms.Count, Is.EqualTo(3), "ホーム本体は3面のはず");
        Assert.That(r.stopTracks.Count, Is.EqualTo(2), "番線(物理線ベース)は2つのはず");
    }

    [Test]
    public void Compute_3Faces2Lines_FourPlatformEdgesOnTwoTracks()
    {
        // 2本の物理線に合計4つのホーム縁が正しく接続されること
        var r = StationLayout.Compute(3, 2);

        Assert.That(r.edges.Count, Is.EqualTo(4), "ホーム縁は合計4つのはず(各線が両側に1つずつ)");

        int track0Edges = 0, track1Edges = 0;
        foreach (var e in r.edges)
        {
            if (e.trackIndex == 0) track0Edges++;
            else if (e.trackIndex == 1) track1Edges++;
            Assert.That(e.side, Is.EqualTo(-1).Or.EqualTo(1), "sideは-1か+1のみ");
            Assert.That(e.mode, Is.EqualTo(StationLayout.PlatformEdgeMode.Normal), "初期値は乗降可(Normal)");
        }
        Assert.That(track0Edges, Is.EqualTo(2), "1番線(track0)は両側にホーム縁を持つ(外側ホームと島式ホーム)");
        Assert.That(track1Edges, Is.EqualTo(2), "2番線(track1)は両側にホーム縁を持つ(島式ホームと外側ホーム)");

        // 島式ホーム(platformIndex=1)は両方の線からアクセスできる
        int islandEdgeCount = 0;
        foreach (var e in r.edges) if (e.platformIndex == 1) islandEdgeCount++;
        Assert.That(islandEdgeCount, Is.EqualTo(2), "島式ホーム(中央)は両側の線からそれぞれ1つずつホーム縁を持つ");
    }

    // 既存プリセット(1面1線・1面2線・2面2線・2面3線・2面4線)はラウンドロビン化しても
    // 完全に同じ結果になること(内側隙間が高々1箇所なので、逐次方式と数学的に同一)
    [TestCase(1, 1, new[] { 1, 0 })]
    [TestCase(1, 2, new[] { 1, 1 })]
    [TestCase(2, 2, new[] { 0, 2, 0 })]
    [TestCase(2, 3, new[] { 1, 2, 0 })]
    [TestCase(2, 4, new[] { 1, 2, 1 })]
    [TestCase(3, 3, new[] { 0, 2, 1, 0 })]
    [TestCase(3, 4, new[] { 0, 2, 2, 0 })]
    [TestCase(3, 5, new[] { 1, 2, 2, 0 })]
    [TestCase(4, 5, new[] { 0, 2, 2, 1, 0 })]
    [TestCase(4, 8, new[] { 1, 2, 2, 2, 1 })]
    public void Slots_ExistingPresetsUnchangedByRoundRobin(int faces, int lines, int[] expected)
    {
        Assert.That(StationLayout.Slots(faces, lines), Is.EqualTo(expected));
    }

    // 実装前設計レビューでCodex CLIが指摘: 4面4線だけはラウンドロビン化で配置が変わる
    // (旧来は内側隙間を逐次に最大2ずつ埋めていたため、4番目のホーム本体が物理線に
    // 一切接しない孤立ホームになっていた)。この変更はv1/v2→v3 migrationの対象として
    // 明示的に許容する(SaveLoadV3Testsのmigrationテストを参照)
    [Test]
    public void Slots_4Faces4Lines_ChangesFromOldGreedyResult_ByDesign()
    {
        var slots = StationLayout.Slots(4, 4);
        Assert.That(slots, Is.EqualTo(new[] { 0, 2, 1, 1, 0 }),
            "新方式: 内側3隙間へラウンドロビン(旧方式の[0,2,2,0,0]から意図的に変更)");

        var r = StationLayout.Compute(4, 4);
        foreach (int t in r.stopTracks)
            Assert.That(t, Is.InRange(0, 3), "4本全ての物理線が引き続き何らかの停車可能線であること");
        Assert.That(r.stopTracks.Count, Is.EqualTo(4), "旧方式で孤立していた4番目のホームにも新方式では物理線が接続される");
    }
}
