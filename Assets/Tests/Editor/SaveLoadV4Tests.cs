using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// M2-E: セーブv4(通過駅を挟む多区間の走行状態)のEditModeテスト。
// PlayerPrefsの"railtycoon_save"キーは実ゲームの本セーブと共有のため、
// テスト前後で退避・復元し、実プレイヤーのセーブデータを壊さないようにする。
public class SaveLoadV4Tests
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

    static (Station a, Station b, Station c, Train train) MakeRunningSkipStopTrain(int ticks)
    {
        var a = EditModeTestHelpers.MakeStation(new Vector3(-6000, 0, 0), 90, 6, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 0), 90, 6, 2, 2, "B");
        var c = EditModeTestHelpers.MakeStation(new Vector3(6000, 0, 0), 90, 6, 2, 2, "C");
        EditModeTestHelpers.Connect(a, b);
        EditModeTestHelpers.Connect(b, c);

        var fm = TrainCatalog.Formations[0];
        int trackA;
        a.TryReserve(out trackA);
        var go = new GameObject("Train");
        go.transform.SetParent(BuildController.WorldRoot, false);
        var train = go.AddComponent<Train>();
        train.id = ++TrackNetwork.trainIdCounter;
        TrackNetwork.trains.Add(train);
        train.Init(fm, new List<Station> { a, c }, new List<int> { trackA, c.StopTracks[0] });

        float tick = Bootstrap.TickSeconds;
        for (int i = 0; i < ticks; i++) train.SimTick(tick);
        return (a, b, c, train);
    }

    static Station FindByName(string name)
    {
        foreach (var st in TrackNetwork.stations) if (st.stationName == name) return st;
        return null;
    }

    // ============================================================
    // v4ラウンドトリップ: 通過駅Bのチェックポイントをまだ消化していない
    // (A-B区間・B番線・B-C区間を全て確保したまま走行中)状態
    // ============================================================
    [Test]
    public void V4RoundTrip_SkipStopBeforeCheckpoint_ReservationsMatchAfterLoad()
    {
        var (a, b, c, train) = MakeRunningSkipStopTrain(700);
        Assert.That(train.IsDwelling, Is.False, "この時点で走行中のはず");

        int bTrack = b.StopTracks[0];
        var segAB = TrackNetwork.Find(a, b);
        var segBC = TrackNetwork.Find(b, c);
        bool preBOccupied = b.occupied[bTrack];
        bool preSegABHeld = segAB.OccupantFrom(a) != null;
        bool preSegBCHeld = segBC.OccupantFrom(b) != null;
        float preS = train.RouteS;
        Assert.That(preBOccupied && preSegABHeld && preSegBCHeld, Is.True, "チェックポイント消化前は全区間・B番線を確保したままのはず");

        SaveLoad.Save();
        Object.DestroyImmediate(BuildController.WorldRoot.gameObject);
        TrackNetwork.Clear();
        bool loaded = SaveLoad.Load();
        Assert.That(loaded, Is.True);

        var a2 = FindByName("A"); var b2 = FindByName("B"); var c2 = FindByName("C");
        Train train2 = null;
        foreach (var t in TrackNetwork.trains) train2 = t;
        Assert.That(train2, Is.Not.Null);

        Assert.That(train2.IsDwelling, Is.False);
        Assert.That(train2.RouteS, Is.EqualTo(preS).Within(0.01f));
        int b2Track = b2.StopTracks[0];
        var segAB2 = TrackNetwork.Find(a2, b2);
        var segBC2 = TrackNetwork.Find(b2, c2);
        Assert.That(b2.occupied[b2Track], Is.EqualTo(preBOccupied));
        Assert.That(segAB2.OccupantFrom(a2) != null, Is.EqualTo(preSegABHeld));
        Assert.That(segBC2.OccupantFrom(b2) != null, Is.EqualTo(preSegBCHeld));

        // ロード後も正常にCへ到着できること(閉塞・番線がデッドロックしていないこと)
        float tick = Bootstrap.TickSeconds;
        for (int i = 0; i < 60000 && train2.ArrivalCount < 1; i++) train2.SimTick(tick);
        Assert.That(train2.ArrivalCount, Is.EqualTo(1), "ロード後も最終目的地(C)へ到達できること");
    }

    // ============================================================
    // v4ラウンドトリップ: 通過駅Bのチェックポイントを既に消化した
    // (A-B区間・B番線は解放済み、B-C区間のみ確保中)状態
    // ============================================================
    [Test]
    public void V4RoundTrip_SkipStopAfterCheckpoint_PartialReleaseMatchesAfterLoad()
    {
        var (a, b, c, train) = MakeRunningSkipStopTrain(0);
        int bTrack = b.StopTracks[0];
        float tick = Bootstrap.TickSeconds;
        bool everOccupiedB = false;
        for (int i = 0; i < 30000; i++)
        {
            train.SimTick(tick);
            if (b.occupied[bTrack]) everOccupiedB = true;
            if (everOccupiedB && !b.occupied[bTrack]) break; // Bを通過し終えた直後で止める
        }
        Assert.That(everOccupiedB, Is.True, "このテストの前提としてBを一度は通過している必要がある");

        var segAB = TrackNetwork.Find(a, b);
        var segBC = TrackNetwork.Find(b, c);
        bool preBOccupied = b.occupied[bTrack];
        bool preSegABHeld = segAB.OccupantFrom(a) != null;
        bool preSegBCHeld = segBC.OccupantFrom(b) != null;
        Assert.That(preBOccupied, Is.False, "Bのチェックポイント消化後はB番線が解放済みのはず");
        Assert.That(preSegABHeld, Is.False, "Bのチェックポイント消化後はA-B区間が解放済みのはず");
        Assert.That(preSegBCHeld, Is.True, "最終区間(B-C)はArrive()まで保持され続けるはず");
        float preS = train.RouteS;

        SaveLoad.Save();
        Object.DestroyImmediate(BuildController.WorldRoot.gameObject);
        TrackNetwork.Clear();
        bool loaded = SaveLoad.Load();
        Assert.That(loaded, Is.True);

        var a2 = FindByName("A"); var b2 = FindByName("B"); var c2 = FindByName("C");
        Train train2 = null;
        foreach (var t in TrackNetwork.trains) train2 = t;
        Assert.That(train2, Is.Not.Null);
        Assert.That(train2.RouteS, Is.EqualTo(preS).Within(0.01f));

        int b2Track = b2.StopTracks[0];
        var segAB2 = TrackNetwork.Find(a2, b2);
        var segBC2 = TrackNetwork.Find(b2, c2);
        Assert.That(b2.occupied[b2Track], Is.False, "解放済みだったB番線は、ロード後も再確保されない(=二重予約を作らない)こと");
        Assert.That(segAB2.OccupantFrom(a2), Is.Null, "解放済みだったA-B区間は、ロード後も再確保されないこと");
        Assert.That(segBC2.OccupantFrom(b2), Is.Not.Null, "保持中だったB-C区間は、ロード後も引き続き確保されていること");

        for (int i = 0; i < 60000 && train2.ArrivalCount < 1; i++) train2.SimTick(tick);
        Assert.That(train2.ArrivalCount, Is.EqualTo(1), "ロード後も最終目的地(C)へ到達できること");
    }

    // ============================================================
    // v3→v4 migration: 通過駅の概念が無いv3セーブ(単一legSegmentIdのみ)は、
    // 単一区間(N=1、transitSegmentIds等は空)としてそのまま読み込めること
    // ============================================================
    const string V3FixtureSingleHopRunning =
        "{\"v\":3,\"money\":5000000000.0,\"carried\":0,\"minutes\":360.0,\"speed\":5.0,\"randomState\":777," +
        "\"nameCounter\":2,\"stationIdCounter\":2,\"segmentIdCounter\":1,\"trainIdCounter\":1,\"lineIdCounter\":0," +
        "\"st\":[" +
        "{\"id\":1,\"x\":-3000.0,\"z\":0.0,\"yaw\":90.0,\"cars\":10,\"faces\":2,\"lines\":2,\"name\":\"A\",\"dev\":0.0,\"spawnAcc\":0.0}," +
        "{\"id\":2,\"x\":3000.0,\"z\":0.0,\"yaw\":90.0,\"cars\":10,\"faces\":2,\"lines\":2,\"name\":\"B\",\"dev\":0.0,\"spawnAcc\":0.0}" +
        "]," +
        "\"seg\":[{\"id\":1,\"aId\":1,\"bId\":2,\"sa\":1,\"sb\":-1}]," +
        "\"tr\":[{\"id\":1,\"typeId\":\"keio5000\",\"cars\":10,\"routeIds\":[1,2],\"tracks\":[0,0],\"lineIds\":[]," +
        "\"idx\":1,\"dir\":1,\"curTrack\":0,\"cyclic\":true,\"state\":1,\"dwellPathKind\":-1," +
        "\"dwellT\":0.0,\"s\":300.0,\"v\":20.0,\"departStationId\":1,\"departTrack\":0," +
        "\"releaseS\":50.0,\"released\":true,\"legSegmentId\":1,\"onboard\":[],\"departureCount\":1,\"arrivalCount\":0}]" +
        "}";

    [Test]
    public void V3Migration_SingleHopRunningTrain_LoadsAsSingleSegmentLeg()
    {
        PlayerPrefs.SetString(Key, V3FixtureSingleHopRunning);
        PlayerPrefs.Save();

        bool loaded = SaveLoad.Load();
        Assert.That(loaded, Is.True);

        Train trn = null;
        foreach (var t in TrackNetwork.trains) trn = t;
        Assert.That(trn, Is.Not.Null);
        Assert.That(trn.IsDwelling, Is.False);
        Assert.That(trn.CurSeg, Is.Not.Null, "v3セーブのlegSegmentIdがそのまま単一区間として復元されること");

        string raw = PlayerPrefs.GetString(Key);
        Assert.That(raw, Does.Not.Contain("Object reference"), "移行時に例外(null参照)が起きていないこと");

        SaveLoad.Save();
        string raw2 = PlayerPrefs.GetString(Key);
        Assert.That(raw2, Does.Contain("\"v\":4"), "次にSaveすると初めてv4として書き戻ること");
    }
}
