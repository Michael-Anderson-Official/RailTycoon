using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// M2-D: セーブv3(ホーム縁の乗降モード)のEditModeテスト。
// PlayerPrefsの"railtycoon_save"キーは実ゲームの本セーブと共有のため、
// テスト前後で退避・復元し、実プレイヤーのセーブデータを壊さないようにする。
public class SaveLoadV3Tests
{
    const string Key = "railtycoon_save";
    bool hadRealSave;
    string realSaveBackup;

    [SetUp]
    public void SetUp()
    {
        hadRealSave = PlayerPrefs.HasKey(Key);
        if (hadRealSave) realSaveBackup = PlayerPrefs.GetString(Key);
        PlayerPrefs.DeleteKey(Key);

        TrackNetwork.Clear();
        Services.Clear();
        SaveLoad.suppress = false;
        GameState.money = 100e8;
        GameState.carried = 0;
        GameState.gameMinutes = 6 * 60;
        GameState.timeScale = 5f;
        GameState.paused = false;
        GameRandom.Seed(777u);
    }

    [TearDown]
    public void TearDown()
    {
        EditModeTestHelpers.DestroyWorldRoot();
        TrackNetwork.Clear();
        Services.Clear();
        SaveLoad.suppress = true;
        GameState.money = 100e8;
        GameState.carried = 0;
        GameState.gameMinutes = 6 * 60;
        GameState.timeScale = 5f;
        GameState.paused = false;

        PlayerPrefs.DeleteKey(Key);
        if (hadRealSave) PlayerPrefs.SetString(Key, realSaveBackup);
        PlayerPrefs.Save();
    }

    // ============================================================
    // v3ラウンドトリップ: 3面2線のホーム縁モードが保存・復元されること
    // ============================================================
    [Test]
    public void V3RoundTrip_3Faces2Lines_EdgeOverridesPreserved()
    {
        var a = EditModeTestHelpers.MakeStation(new Vector3(-800, 0, 0), 90, 8, 3, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(800, 0, 0), 90, 8, 2, 2, "B");
        EditModeTestHelpers.Connect(a, b);
        a.SetPlatformEdgeMode(0, -1, StationLayout.PlatformEdgeMode.BoardOnly);
        a.SetPlatformEdgeMode(1, 1, StationLayout.PlatformEdgeMode.Disabled);

        SaveLoad.Save();
        Object.DestroyImmediate(BuildController.WorldRoot.gameObject);
        TrackNetwork.Clear();
        bool loaded = SaveLoad.Load();

        Assert.That(loaded, Is.True);
        Assert.That(TrackNetwork.stations.Count, Is.EqualTo(2));
        var aAfter = TrackNetwork.stations[0];
        Assert.That(aAfter.faces, Is.EqualTo(3));
        Assert.That(aAfter.PlatformEdges.Count, Is.EqualTo(4));

        int normalCount = 0, boardOnlyCount = 0, disabledCount = 0;
        foreach (var e in aAfter.PlatformEdges)
        {
            if (e.trackIndex == 0 && e.side == -1) Assert.That(e.mode, Is.EqualTo(StationLayout.PlatformEdgeMode.BoardOnly));
            else if (e.trackIndex == 1 && e.side == 1) Assert.That(e.mode, Is.EqualTo(StationLayout.PlatformEdgeMode.Disabled));
            else Assert.That(e.mode, Is.EqualTo(StationLayout.PlatformEdgeMode.Normal));
            if (e.mode == StationLayout.PlatformEdgeMode.Normal) normalCount++;
            else if (e.mode == StationLayout.PlatformEdgeMode.BoardOnly) boardOnlyCount++;
            else if (e.mode == StationLayout.PlatformEdgeMode.Disabled) disabledCount++;
        }
        Assert.That(normalCount, Is.EqualTo(2));
        Assert.That(boardOnlyCount, Is.EqualTo(1));
        Assert.That(disabledCount, Is.EqualTo(1));

        string raw = PlayerPrefs.GetString(Key);
        Assert.That(raw, Does.Contain("\"v\":3"), "保存は常にv3で行われること");
    }

    // ============================================================
    // v2→v3 migration: 既存のv2セーブ(ホーム縁の概念が無い)は全て乗降可として
    // 読み込まれ、v1/v2の物理駅構造(cars/faces/lines)自体はそのまま引き継がれること
    // ============================================================
    const string V2Fixture =
        "{\"v\":2,\"money\":2000000000.0,\"carried\":50,\"minutes\":480.0,\"speed\":5.0,\"randomState\":123456," +
        "\"nameCounter\":2,\"stationIdCounter\":2,\"segmentIdCounter\":1,\"trainIdCounter\":0,\"lineIdCounter\":0," +
        "\"st\":[" +
        "{\"id\":1,\"x\":-800.0,\"z\":0.0,\"yaw\":90.0,\"cars\":8,\"faces\":3,\"lines\":2,\"name\":\"A\",\"dev\":0.0,\"spawnAcc\":0.0,\"waitToId\":[2],\"waitN\":[4]}," +
        "{\"id\":2,\"x\":800.0,\"z\":0.0,\"yaw\":90.0,\"cars\":8,\"faces\":2,\"lines\":2,\"name\":\"B\",\"dev\":0.0,\"spawnAcc\":0.0}" +
        "]," +
        "\"seg\":[{\"id\":1,\"aId\":1,\"bId\":2,\"sa\":1,\"sb\":-1}]" +
        "}";

    [Test]
    public void V2Migration_3Faces2LinesStation_AllEdgesDefaultToNormal()
    {
        PlayerPrefs.SetString(Key, V2Fixture);
        PlayerPrefs.Save();

        bool loaded = SaveLoad.Load();

        Assert.That(loaded, Is.True);
        var stA = TrackNetwork.stations[0].id == 1 ? TrackNetwork.stations[0] : TrackNetwork.stations[1];
        Assert.That(stA.faces, Is.EqualTo(3));
        Assert.That(stA.PlatformEdges.Count, Is.EqualTo(4), "v2にはホーム縁の概念が無いが、faces/linesから決定的に導出されること");
        foreach (var e in stA.PlatformEdges)
            Assert.That(e.mode, Is.EqualTo(StationLayout.PlatformEdgeMode.Normal), "v2 migrationは全ホーム縁を乗降可(Normal)にすること");
    }

    // 実装前設計レビューでCodex CLIが指摘: 4面4線はラウンドロビン化で物理配置が変わる
    // (旧: 4番目のホームが孤立無接続 → 新: 全ホームが接続)。migration後も
    // 4本の物理線が全て停車可能であり、駅として引き続き機能することを確認する
    const string V2Fixture4Faces4Lines =
        "{\"v\":2,\"money\":100.0,\"carried\":0,\"minutes\":360.0,\"speed\":5.0,\"randomState\":1," +
        "\"nameCounter\":1,\"stationIdCounter\":1,\"segmentIdCounter\":0,\"trainIdCounter\":0,\"lineIdCounter\":0," +
        "\"st\":[{\"id\":1,\"x\":0.0,\"z\":0.0,\"yaw\":0.0,\"cars\":6,\"faces\":4,\"lines\":4,\"name\":\"A\",\"dev\":0.0,\"spawnAcc\":0.0}]" +
        "}";

    [Test]
    public void V2Migration_4Faces4Lines_AllFourTracksRemainUsableAfterLayoutChange()
    {
        PlayerPrefs.SetString(Key, V2Fixture4Faces4Lines);
        PlayerPrefs.Save();

        bool loaded = SaveLoad.Load();

        Assert.That(loaded, Is.True);
        var st = TrackNetwork.stations[0];
        Assert.That(st.faces, Is.EqualTo(4));
        Assert.That(st.StopTracks.Count, Is.EqualTo(4), "新方式では旧方式で孤立していた4番目のホームにも物理線が接続され、4線とも停車可能になること");
        int reserved;
        Assert.That(st.TryReserve(out reserved), Is.True, "移行後も番線予約が正常に機能すること");
    }

    // ============================================================
    // v1→v2→v3 migration: v1には当然ホーム縁の概念も無い
    // ============================================================
    const string V1Fixture =
        "{\"v\":1,\"money\":100.0,\"carried\":0,\"minutes\":360.0,\"speed\":5.0," +
        "\"nameCounter\":2,\"lineIdCounter\":0," +
        "\"st\":[" +
        "{\"x\":-800.0,\"z\":0.0,\"yaw\":90.0,\"cars\":8,\"faces\":3,\"lines\":2,\"name\":\"A\",\"dev\":0.0,\"waitTo\":[],\"waitN\":[]}," +
        "{\"x\":800.0,\"z\":0.0,\"yaw\":90.0,\"cars\":8,\"faces\":2,\"lines\":2,\"name\":\"B\",\"dev\":0.0,\"waitTo\":[],\"waitN\":[]}" +
        "]," +
        "\"seg\":[{\"a\":0,\"b\":1,\"sa\":1,\"sb\":-1}]" +
        "}";

    [Test]
    public void V1Migration_ThroughV2ToV3_EdgesDefaultNormal()
    {
        PlayerPrefs.SetString(Key, V1Fixture);
        PlayerPrefs.Save();

        bool loaded = SaveLoad.Load();

        Assert.That(loaded, Is.True);
        var stA = TrackNetwork.stations[0].faces == 3 ? TrackNetwork.stations[0] : TrackNetwork.stations[1];
        Assert.That(stA.PlatformEdges.Count, Is.EqualTo(4));
        foreach (var e in stA.PlatformEdges)
            Assert.That(e.mode, Is.EqualTo(StationLayout.PlatformEdgeMode.Normal));
    }

    // ============================================================
    // 異常系: ホーム縁の不正な上書き設定を拒否し、現在のワールドを変化させない
    // ============================================================
    struct WorldSnapshot { public int stations; public double money; }
    static WorldSnapshot SnapWorld() => new WorldSnapshot { stations = TrackNetwork.stations.Count, money = GameState.money };

    void AssertLoadFailsWithoutChangingWorld(string badJson)
    {
        var a = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 0), 90, 8, 3, 2, "A");
        var before = SnapWorld();

        PlayerPrefs.SetString(Key, badJson);
        PlayerPrefs.Save();
        UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
        bool loaded = SaveLoad.Load();
        UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

        Assert.That(loaded, Is.False);
        var after = SnapWorld();
        Assert.That(after.stations, Is.EqualTo(before.stations));
        Assert.That(after.money, Is.EqualTo(before.money));
    }

    static string V3WithStationOverride(string edgeOverridesJson) =>
        "{\"v\":3,\"money\":100.0,\"carried\":0,\"minutes\":360.0,\"speed\":5.0,\"randomState\":1," +
        "\"nameCounter\":1,\"stationIdCounter\":1,\"segmentIdCounter\":0,\"trainIdCounter\":0,\"lineIdCounter\":0," +
        "\"st\":[{\"id\":1,\"x\":0.0,\"z\":0.0,\"yaw\":90.0,\"cars\":8,\"faces\":3,\"lines\":2,\"name\":\"A\",\"dev\":0.0,\"spawnAcc\":0.0" +
        (edgeOverridesJson != null ? ",\"edgeOverrides\":" + edgeOverridesJson : "") + "}]}";

    // 1面1線(StationLayout.Compute(1,1)はtrack0にside=+1のホーム縁を1つだけ持ち、
    // side=-1は実在しない)。「実在しない(trackIndex,side)の組」を、重複チェックに
    // 引っかからない形で検証するためのfixture
    static string V3WithStationOverride1Face1Line(string edgeOverridesJson) =>
        "{\"v\":3,\"money\":100.0,\"carried\":0,\"minutes\":360.0,\"speed\":5.0,\"randomState\":1," +
        "\"nameCounter\":1,\"stationIdCounter\":1,\"segmentIdCounter\":0,\"trainIdCounter\":0,\"lineIdCounter\":0," +
        "\"st\":[{\"id\":1,\"x\":0.0,\"z\":0.0,\"yaw\":90.0,\"cars\":8,\"faces\":1,\"lines\":1,\"name\":\"A\",\"dev\":0.0,\"spawnAcc\":0.0" +
        (edgeOverridesJson != null ? ",\"edgeOverrides\":" + edgeOverridesJson : "") + "}]}";

    [Test]
    public void Load_DuplicateEdgeOverride_FailsWithoutChangingWorld() =>
        AssertLoadFailsWithoutChangingWorld(V3WithStationOverride(
            "[{\"trackIndex\":0,\"side\":-1,\"mode\":1},{\"trackIndex\":0,\"side\":-1,\"mode\":2}]"));

    [Test]
    public void Load_InvalidEdgeSide_FailsWithoutChangingWorld() =>
        AssertLoadFailsWithoutChangingWorld(V3WithStationOverride(
            "[{\"trackIndex\":0,\"side\":0,\"mode\":1}]"));

    [Test]
    public void Load_InvalidEdgeMode_FailsWithoutChangingWorld() =>
        AssertLoadFailsWithoutChangingWorld(V3WithStationOverride(
            "[{\"trackIndex\":0,\"side\":-1,\"mode\":99}]"));

    [Test]
    public void Load_NonexistentEdgeReference_FailsWithoutChangingWorld() =>
        // 3面2線(track0,1のみ)に対し、存在しないtrack5を参照
        AssertLoadFailsWithoutChangingWorld(V3WithStationOverride(
            "[{\"trackIndex\":5,\"side\":-1,\"mode\":1}]"));

    [Test]
    public void Load_EdgeSideThatTrackDoesNotHave_FailsWithoutChangingWorld() =>
        // 実装後レビューでCodex CLIが指摘: 3面2線のtrack1はside±1両方が実在するため、
        // 3件目を追加すると「実在しないside」ではなく「重複」で拒否されてしまい、
        // 意図した検証経路(実在性チェック)に到達しない。1面1線はtrack0にside=+1の
        // ホーム縁しか無い(StationLayout.Compute(1,1)で確認済み)ため、side=-1を
        // 指定すれば重複と無関係に「実在しない組」を正しく検証できる
        AssertLoadFailsWithoutChangingWorld(V3WithStationOverride1Face1Line(
            "[{\"trackIndex\":0,\"side\":-1,\"mode\":1}]"));

    // ============================================================
    // 決定的継続: モード付きホーム縁を含むワールドでもセーブ→ロード→継続が
    // セーブ無し継続と一致すること(M2-B.2/M2-Cの手法を踏襲)
    // ============================================================
    static void RunTicks(List<Station> stations, List<Train> trains, float multiplier, int ticks)
    {
        float dt = Bootstrap.TickSeconds * multiplier;
        for (int i = 0; i < ticks; i++)
        {
            foreach (var st in stations) st.Tick(dt);
            foreach (var t in trains) t.SimTick(dt);
        }
    }

    [Test]
    public void DeterministicContinuation_WithPlatformEdgeModes_SaveThenLoad_Matches()
    {
        // 初期Dwell(8秒=480tick)の途中でセーブする(NTicks<480)。まだ発車していない
        // 状態でホーム縁モードを保存/復元させ、復元後の発車判定(TryDepart→Board)に
        // 実際にDisabledが効くかどうかを検証する。列車がB駅へ到達するまで待つ必要は無い
        const int NTicks = 300, MTicks = 600;

        (double money, long carried, bool dwell, int onboard, int waitingAtA) RunScenario(bool viaSaveLoad)
        {
            TrackNetwork.Clear();
            Services.Clear();
            GameState.money = 100e8; GameState.carried = 0; GameState.gameMinutes = 6 * 60; GameState.timeScale = 5f;
            GameRandom.Seed(999u);

            var a = EditModeTestHelpers.MakeStation(new Vector3(-800, 0, 0), 90, 8, 3, 2, "A");
            var b = EditModeTestHelpers.MakeStation(new Vector3(800, 0, 0), 90, 8, 2, 2, "B");
            EditModeTestHelpers.Connect(a, b);
            // track0の両側をDisabledにして、A駅での乗車を実際に禁止する
            a.SetPlatformEdgeMode(0, -1, StationLayout.PlatformEdgeMode.Disabled);
            a.SetPlatformEdgeMode(0, 1, StationLayout.PlatformEdgeMode.Disabled);

            int ta; a.TryReserve(out ta);
            a.waiting[b] = 5; // B行きの待ち客。乗車禁止なら発車後もこの人数が残るはず
            var go = new GameObject("Train");
            go.transform.SetParent(BuildController.WorldRoot, false);
            var tr = go.AddComponent<Train>();
            tr.id = ++TrackNetwork.trainIdCounter;
            TrackNetwork.trains.Add(tr);
            tr.Init(TrainCatalog.Formations[0], new List<Station> { a, b }, new List<int> { ta, b.StopTracks[0] });

            RunTicks(TrackNetwork.stations, TrackNetwork.trains, 1f, NTicks);

            if (viaSaveLoad)
            {
                SaveLoad.Save();
                Object.DestroyImmediate(BuildController.WorldRoot.gameObject);
                TrackNetwork.Clear();
                Services.Clear();
                Assert.That(SaveLoad.Load(), Is.True);
            }

            RunTicks(TrackNetwork.stations, TrackNetwork.trains, 1f, MTicks);
            var t0 = TrackNetwork.trains[0];
            var aAfter = TrackNetwork.stations.Find(s => s.stationName == "A");
            var bAfter = TrackNetwork.stations.Find(s => s.stationName == "B");
            int stillWaiting = aAfter.waiting.TryGetValue(bAfter, out int w) ? w : 0;
            return (GameState.money, GameState.carried, t0.IsDwelling, t0.OnboardCount, stillWaiting);
        }

        var withoutSave = RunScenario(false);
        var withSave = RunScenario(true);

        Assert.That(withSave.money, Is.EqualTo(withoutSave.money).Within(0.5));
        Assert.That(withSave.carried, Is.EqualTo(withoutSave.carried));
        Assert.That(withSave.dwell, Is.EqualTo(withoutSave.dwell));
        Assert.That(withSave.onboard, Is.EqualTo(withoutSave.onboard));
        Assert.That(withSave.waitingAtA, Is.EqualTo(withoutSave.waitingAtA));
        // Disabledが実際に効いていること自体も固定する(乗車0人。待ち客は需要生成で
        // 増え続けるため厳密な5ではなく、最初に置いた5人が消費されていないことだけ見る)
        Assert.That(withoutSave.onboard, Is.EqualTo(0));
        Assert.That(withoutSave.waitingAtA, Is.GreaterThanOrEqualTo(5));
    }
}
