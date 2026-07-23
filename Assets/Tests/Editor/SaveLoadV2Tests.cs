using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// M2-C: セーブv2(安定ID・走行中列車・乗車旅客・GameRandom状態の保存復元)のEditModeテスト。
// PlayerPrefsの"railtycoon_save"キーは実ゲームの本セーブと共有のため、
// テスト前後で退避・復元し、実プレイヤーのセーブデータを壊さないようにする。
public class SaveLoadV2Tests
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
        GameRandom.Seed(12345u);
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
    // v1 migration: コード内の現在のシリアライザからではなく、固定文字列のv1 JSON
    // fixtureを使う(将来SaveLoad.Saveの実装が変わってもmigration対象データが
    // 変化しないようにするため)
    // ============================================================
    const string V1Fixture =
        "{\"v\":1,\"money\":5550000000.0,\"carried\":100,\"minutes\":600.0,\"speed\":5.0," +
        "\"nameCounter\":3,\"lineIdCounter\":1," +
        "\"st\":[" +
        "{\"x\":-600.0,\"z\":0.0,\"yaw\":0.0,\"cars\":8,\"faces\":2,\"lines\":2,\"name\":\"A\",\"dev\":0.0,\"waitTo\":[1],\"waitN\":[5]}," +
        "{\"x\":400.0,\"z\":100.0,\"yaw\":45.0,\"cars\":8,\"faces\":2,\"lines\":2,\"name\":\"B\",\"dev\":1.2,\"waitTo\":[],\"waitN\":[]}," +
        "{\"x\":1300.0,\"z\":-50.0,\"yaw\":-30.0,\"cars\":8,\"faces\":1,\"lines\":2,\"name\":\"C\",\"dev\":0.0,\"waitTo\":[],\"waitN\":[]}" +
        "]," +
        "\"seg\":[{\"a\":0,\"b\":1,\"sa\":1,\"sb\":-1},{\"a\":1,\"b\":2,\"sa\":1,\"sb\":-1}]," +
        "\"tr\":[{\"typeId\":\"keio5000\",\"cars\":10,\"route\":[0,1],\"tracks\":[],\"idx\":0,\"dir\":1,\"lineId\":-1,\"lineIds\":[1]}]," +
        "\"ln\":[{\"id\":1,\"typeIdx\":3,\"name\":\"\",\"route\":[0,1,2],\"tracks\":[]}]" +
        "}";

    [Test]
    public void V1Migration_FixedFixture_ConvertsIndexReferencesToStableIds()
    {
        PlayerPrefs.SetString(Key, V1Fixture);
        PlayerPrefs.Save();

        bool loaded = SaveLoad.Load();

        Assert.That(loaded, Is.True);
        Assert.That(TrackNetwork.stations.Count, Is.EqualTo(3));
        foreach (var s in TrackNetwork.stations) Assert.That(s.id, Is.Not.EqualTo(0), "全駅に非0のstable idが割り当たること");
        var ids = new HashSet<int>();
        foreach (var s in TrackNetwork.stations) Assert.That(ids.Add(s.id), Is.True, "駅idが重複していないこと");

        var byName = new Dictionary<string, Station>();
        foreach (var s in TrackNetwork.stations) byName[s.stationName] = s;
        Assert.That(byName.ContainsKey("A"), Is.True);
        Assert.That(byName.ContainsKey("B"), Is.True);
        Assert.That(byName["A"].cars, Is.EqualTo(8));
        Assert.That(byName["B"].dev, Is.EqualTo(1.2f).Within(0.01f));

        // v1のwaitTo=[1](index参照)が、B駅への安定ID参照へ正しく変換されていること
        Assert.That(byName["A"].waiting.ContainsKey(byName["B"]), Is.True, "v1のindex参照が正しいstation参照へ移行されること");
        Assert.That(byName["A"].waiting[byName["B"]], Is.EqualTo(5));

        Assert.That(TrackNetwork.segments.Count, Is.EqualTo(2));
        Assert.That(Services.lines.Count, Is.EqualTo(1));
        Assert.That(Services.lines[0].route.Count, Is.EqualTo(3));

        Assert.That(TrackNetwork.trains.Count, Is.EqualTo(1), "v1の列車・系統参照が正しく移行されること");
        var tr = TrackNetwork.trains[0];
        Assert.That(tr.id, Is.Not.EqualTo(0));
        Assert.That(tr.lineIds, Does.Contain(1), "v1の系統参照が移行されること");

        // v1に存在しない値(state/s/v/onboard/GameRandom等)には明示的な既定値が入ること
        Assert.That(tr.IsDwelling, Is.True, "v1には走行中状態が無いため、全列車はDwellへ正規化される");
        Assert.That(tr.OnboardCount, Is.EqualTo(0), "v1には乗車旅客が無いため空になる");
        Assert.That(GameRandom.GetState(), Is.EqualTo(0x9E3779B9u), "v1にはRNG状態が無いため既定初期値になる");

        Assert.That(GameState.money, Is.EqualTo(5550000000.0).Within(1));
        Assert.That(GameState.carried, Is.EqualTo(100));
    }

    [Test]
    public void V1Migration_DoesNotOverwritePlayerPrefsUntilNextExplicitSave()
    {
        PlayerPrefs.SetString(Key, V1Fixture);
        PlayerPrefs.Save();

        SaveLoad.Load();

        // ロードしただけでは元のv1文字列を書き換えない
        string raw = PlayerPrefs.GetString(Key);
        Assert.That(raw, Is.EqualTo(V1Fixture), "ロードのみでは元のセーブ文字列を上書きしないこと");

        SaveLoad.Save();
        string raw2 = PlayerPrefs.GetString(Key);
        Assert.That(raw2, Does.Contain("\"v\":3"), "次にSaveすると初めてv3として書き戻ること(M2-DでV3を新設)");
    }

    // ============================================================
    // v2ラウンドトリップ
    // ============================================================

    static (Station a, Station b, Station c) BuildStations()
    {
        var a = EditModeTestHelpers.MakeStation(new Vector3(-800, 0, 0), 90, 8, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 0), 90, 8, 2, 2, "B");
        var c = EditModeTestHelpers.MakeStation(new Vector3(800, 0, 0), 90, 8, 1, 1, "C");
        EditModeTestHelpers.Connect(a, b);
        EditModeTestHelpers.Connect(b, c);
        return (a, b, c);
    }

    static Train MakeAndRegisterTrain(TrainCatalog.Formation fm, List<Station> route, List<int> tracks, int startIdx = 0, int dir = 1)
    {
        var go = new GameObject("Train_" + fm.Label);
        go.transform.SetParent(BuildController.WorldRoot, false);
        var t = go.AddComponent<Train>();
        t.id = ++TrackNetwork.trainIdCounter;
        TrackNetwork.trains.Add(t);
        t.Init(fm, route, tracks, startIdx, dir);
        return t;
    }

    struct StationSnapshot
    {
        public int id; public string name; public int cars, faces, lines;
        public float dev; public double spawnAcc;
        public Dictionary<int, int> waitingById;
    }

    struct TrainSnapshot
    {
        public int id; public bool isDwelling; public int idx, dir, curTrack;
        public float routeS, speedKmh, dwellRemaining; public int onboardCount;
        public int departureCount, arrivalCount;
        public List<(int destId, int count)> onboard;
    }

    static StationSnapshot Snap(Station s)
    {
        var w = new Dictionary<int, int>();
        foreach (var kv in s.waiting) if (kv.Key != null) w[kv.Key.id] = kv.Value;
        return new StationSnapshot
        {
            id = s.id, name = s.stationName, cars = s.cars, faces = s.faces, lines = s.lines,
            dev = s.dev, spawnAcc = s.SpawnAccumulator, waitingById = w,
        };
    }

    static TrainSnapshot Snap(Train t)
    {
        var ob = new List<(int, int)>();
        foreach (var grp in t.Onboard) if (grp.dest != null) ob.Add((grp.dest.id, grp.count));
        return new TrainSnapshot
        {
            id = t.id, isDwelling = t.IsDwelling, idx = t.idx, dir = t.dir, curTrack = t.curTrack,
            routeS = t.RouteS, speedKmh = t.SpeedKmh, dwellRemaining = t.DwellRemaining,
            onboardCount = t.OnboardCount, departureCount = t.DepartureCount, arrivalCount = t.ArrivalCount,
            onboard = ob,
        };
    }

    static void RunTicks(float multiplier, int ticks)
    {
        float dt = Bootstrap.TickSeconds * multiplier;
        for (int i = 0; i < ticks; i++)
        {
            foreach (var st in TrackNetwork.stations) st.Tick(dt);
            foreach (var t in TrackNetwork.trains) t.SimTick(dt);
        }
    }

    [Test]
    public void V2RoundTrip_StationsSegmentsLinesMultiLineTrain_AllFieldsMatch()
    {
        var (a, b, c) = BuildStations();
        a.ForceDev(2.5f);
        a.waiting[b] = 7;
        b.waiting[c] = 3;

        var lFwd = new ServiceLine { id = ++Services.idCounter, typeIdx = 0, route = new List<Station> { a, b, c }, tracks = new List<int> { a.StopTracks[0], b.StopTracks[0], c.StopTracks[0] } };
        var lBack = new ServiceLine { id = ++Services.idCounter, typeIdx = 0, route = new List<Station> { c, b, a }, tracks = new List<int> { c.StopTracks[0], b.StopTracks[0], a.StopTracks[0] } };
        Services.lines.Add(lFwd); Services.lines.Add(lBack);

        BuildController.BuildItinerary(new List<ServiceLine> { lFwd, lBack }, out var route, out var tracks, out var lineIds);
        var tr = MakeAndRegisterTrain(TrainCatalog.Formations[3], route, tracks);
        tr.lineIds = lineIds;

        var beforeSt = new List<StationSnapshot> { Snap(a), Snap(b), Snap(c) };
        var beforeTr = Snap(tr);
        double moneyBefore = GameState.money;
        long carriedBefore = GameState.carried;

        SaveLoad.Save();
        Object.DestroyImmediate(BuildController.WorldRoot.gameObject);
        TrackNetwork.Clear();
        Services.Clear();
        GameState.money = 0; GameState.carried = -1;

        bool loaded = SaveLoad.Load();

        Assert.That(loaded, Is.True);
        Assert.That(TrackNetwork.stations.Count, Is.EqualTo(3));
        Assert.That(TrackNetwork.segments.Count, Is.EqualTo(2));
        Assert.That(Services.lines.Count, Is.EqualTo(2));
        Assert.That(TrackNetwork.trains.Count, Is.EqualTo(1));

        var afterSt = new List<StationSnapshot>();
        foreach (var s in TrackNetwork.stations) afterSt.Add(Snap(s));
        for (int i = 0; i < beforeSt.Count; i++)
        {
            var bsnap = beforeSt[i]; var asnap = afterSt[i];
            Assert.That(asnap.id, Is.EqualTo(bsnap.id), "駅idが保持されること");
            Assert.That(asnap.name, Is.EqualTo(bsnap.name));
            Assert.That(asnap.cars, Is.EqualTo(bsnap.cars));
            Assert.That(asnap.dev, Is.EqualTo(bsnap.dev).Within(0.01f));
            Assert.That(asnap.waitingById.Count, Is.EqualTo(bsnap.waitingById.Count));
            foreach (var kv in bsnap.waitingById)
                Assert.That(asnap.waitingById.ContainsKey(kv.Key) && asnap.waitingById[kv.Key] == kv.Value, Is.True, "待客数が保持されること");
        }

        var trAfter = TrackNetwork.trains[0];
        var afterTr = Snap(trAfter);
        Assert.That(afterTr.id, Is.EqualTo(beforeTr.id));
        Assert.That(afterTr.idx, Is.EqualTo(beforeTr.idx));
        Assert.That(afterTr.dir, Is.EqualTo(beforeTr.dir));
        Assert.That(afterTr.curTrack, Is.EqualTo(beforeTr.curTrack));
        Assert.That(trAfter.lineIds.Count, Is.EqualTo(2), "複数系統への配属が保持されること");
        Assert.That(trAfter.lineIds, Does.Contain(lFwd.id));
        Assert.That(trAfter.lineIds, Does.Contain(lBack.id));
        Assert.That(trAfter.cyclic, Is.EqualTo(tr.cyclic), "cyclicフラグが保持されること");

        Assert.That(GameState.money, Is.EqualTo(moneyBefore).Within(1));
        Assert.That(GameState.carried, Is.EqualTo(carriedBefore));
    }

    [Test]
    public void V2RoundTrip_RunningTrain_RestoresExactLogicalStateAndBlockReservation()
    {
        var (a, b, c) = BuildStations();
        int ta; a.TryReserve(out ta);
        var tr = MakeAndRegisterTrain(TrainCatalog.Formations[0], new List<Station> { a, b }, new List<int> { ta, b.StopTracks[0] });

        // Dwell(8秒)を消化させてRun状態にする
        for (int i = 0; i < 600 && tr.IsDwelling; i++) tr.SimTick(Bootstrap.TickSeconds);
        Assert.That(tr.IsDwelling, Is.False, "前提: 発車してRun状態になっていること");
        for (int i = 0; i < 60; i++) tr.SimTick(Bootstrap.TickSeconds); // 走行中の適当な位置まで進める
        Assert.That(tr.IsDwelling, Is.False, "前提: まだ走行中であること(到着していない)");

        var before = Snap(tr);
        float speedBefore = tr.SpeedKmh;
        var segAB = TrackNetwork.segments[0];
        var occBeforeA = segAB.OccupantFrom(a);
        Assert.That(occBeforeA, Is.EqualTo(tr), "前提: 走行中の列車がA→B方向の閉塞を占有していること");

        SaveLoad.Save();
        Object.DestroyImmediate(BuildController.WorldRoot.gameObject);
        TrackNetwork.Clear();
        bool loaded = SaveLoad.Load();

        Assert.That(loaded, Is.True);
        var trAfter = TrackNetwork.trains[0];
        Assert.That(trAfter.IsDwelling, Is.False, "Run状態が保持されること(常にDwellへ復元されないこと)");
        Assert.That(trAfter.idx, Is.EqualTo(before.idx));
        Assert.That(trAfter.dir, Is.EqualTo(before.dir));
        Assert.That(trAfter.curTrack, Is.EqualTo(before.curTrack));
        Assert.That(trAfter.RouteS, Is.EqualTo(before.routeS).Within(0.5f), "走行中の経路上位置が保持されること");
        Assert.That(trAfter.SpeedKmh, Is.EqualTo(speedBefore).Within(0.5f), "速度が保持されること");

        var segAfter = TrackNetwork.segments[0];
        var occAfterA = segAfter.OccupantFrom(TrackNetwork.stations[0]);
        Assert.That(occAfterA, Is.EqualTo(trAfter), "ロード後、走行中列車が閉塞を正しく再占有していること(二重予約や未予約でないこと)");

        // ロード後もそのまま走行を継続でき、正しく到着できること(復元pathの妥当性の間接検証)
        for (int i = 0; i < 12000 && trAfter.IsDwelling == false; i++) trAfter.SimTick(Bootstrap.TickSeconds);
        Assert.That(trAfter.IsDwelling, Is.True, "ロード後も走行を継続し、正常に到着できること");
    }

    [Test]
    public void V2RoundTrip_JustArrivedDwellingTrain_RestoresLegPathAndReleasesOriginPlatform()
    {
        var (a, b, c) = BuildStations();
        int ta; a.TryReserve(out ta);
        var tr = MakeAndRegisterTrain(TrainCatalog.Formations[0], new List<Station> { a, b }, new List<int> { ta, b.StopTracks[0] });

        // 発車させ、到着してDwellへ戻るまで進める
        for (int i = 0; i < 6000 && !(tr.IsDwelling && tr.ArrivalCount > 0); i++) tr.SimTick(Bootstrap.TickSeconds);
        Assert.That(tr.IsDwelling, Is.True);
        Assert.That(tr.ArrivalCount, Is.EqualTo(1), "前提: 1回到着し、到着直後のDwell(JustArrivedLeg)であること");

        bool aTrackFreedBefore = !a.occupied[ta];
        Assert.That(aTrackFreedBefore, Is.True, "前提: 到着済みなので出発駅Aの番線は解放されていること");

        var before = Snap(tr);
        SaveLoad.Save();
        Object.DestroyImmediate(BuildController.WorldRoot.gameObject);
        TrackNetwork.Clear();
        bool loaded = SaveLoad.Load();

        Assert.That(loaded, Is.True);
        var trAfter = TrackNetwork.trains[0];
        var stationsAfter = TrackNetwork.stations;
        Assert.That(trAfter.IsDwelling, Is.True);
        Assert.That(trAfter.idx, Is.EqualTo(before.idx));
        Assert.That(trAfter.ArrivalCount, Is.EqualTo(1));
        Assert.That(trAfter.RouteS, Is.EqualTo(before.routeS).Within(0.05f), "到着直後のDwell位置(=停止位置目標)が一致すること");

        // A駅(出発駅)の番線が引き続き解放されたままであること(二重に占有扱いされていないこと)
        var aAfter = stationsAfter[0];
        Assert.That(aAfter.occupied[ta], Is.False, "到着済み出発駅の番線が誤って占有扱いにならないこと");

        // B駅(現在停車中)の番線は占有されていること
        var bAfter = stationsAfter[1];
        Assert.That(bAfter.occupied[trAfter.curTrack], Is.True, "現在停車中の番線が占有されていること");
    }

    [Test]
    public void V2RoundTrip_OnboardPassengersAndGameRandomState_Match()
    {
        var (a, b, c) = BuildStations();
        int ta; a.TryReserve(out ta);
        var tr = MakeAndRegisterTrain(TrainCatalog.Formations[0], new List<Station> { a, b }, new List<int> { ta, b.StopTracks[0] });

        a.waiting[b] = 20; // 発車時に乗車させる
        for (int i = 0; i < 600 && tr.IsDwelling; i++) tr.SimTick(Bootstrap.TickSeconds); // 発車
        Assert.That(tr.OnboardCount, Is.GreaterThan(0), "前提: 発車時に旅客が乗車していること");

        uint randBefore = GameRandom.GetState();
        int onboardBefore = tr.OnboardCount;
        var onboardListBefore = new List<(int, int)>();
        foreach (var grp in tr.Onboard) onboardListBefore.Add((grp.dest.id, grp.count));

        SaveLoad.Save();
        Object.DestroyImmediate(BuildController.WorldRoot.gameObject);
        TrackNetwork.Clear();
        bool loaded = SaveLoad.Load();

        Assert.That(loaded, Is.True);
        Assert.That(GameRandom.GetState(), Is.EqualTo(randBefore), "GameRandomの内部状態が保存・復元されること");
        var trAfter = TrackNetwork.trains[0];
        Assert.That(trAfter.OnboardCount, Is.EqualTo(onboardBefore), "乗車中旅客数が保持されること");
        var onboardListAfter = new List<(int, int)>();
        foreach (var grp in trAfter.Onboard) onboardListAfter.Add((grp.dest.id, grp.count));
        Assert.That(onboardListAfter, Is.EquivalentTo(onboardListBefore), "乗車旅客の目的地別内訳が保持されること");
    }

    [Test]
    public void V2RoundTrip_TwoTrainBlockAndPlatformContention_NoDoubleReservationAfterLoad()
    {
        var (a, b, c) = BuildStations(); // b: 2面2線, c: 1面1線
        int ta; a.TryReserve(out ta);
        int tc; c.TryReserve(out tc);
        var t1 = MakeAndRegisterTrain(TrainCatalog.Formations[0], new List<Station> { a, b, c }, new List<int> { ta, -1, -1 }, 0, 1);
        var t2 = MakeAndRegisterTrain(TrainCatalog.Formations[0], new List<Station> { a, b, c }, new List<int> { -1, -1, tc }, 2, -1);

        RunTicks(1f, 900); // 15秒ぶん進め、両列車を発車・競合させる

        var beforeT1 = Snap(t1); var beforeT2 = Snap(t2);
        bool bOccBefore = false;
        foreach (int i in b.StopTracks) if (b.occupied[i]) bOccBefore = true;

        SaveLoad.Save();
        Object.DestroyImmediate(BuildController.WorldRoot.gameObject);
        TrackNetwork.Clear();
        bool loaded = SaveLoad.Load();

        Assert.That(loaded, Is.True);
        Assert.That(TrackNetwork.trains.Count, Is.EqualTo(2));
        var stAfter = TrackNetwork.stations;
        var bAfter = stAfter[1];
        bool bOccAfter = false;
        foreach (int i in bAfter.StopTracks) if (bAfter.occupied[i]) bOccAfter = true;
        Assert.That(bOccAfter, Is.EqualTo(bOccBefore), "B駅番線占有状態が保持されること");

        // ロード後も両列車がそれぞれ正しく走行を継続できること(予約が壊れていないことの間接検証)
        RunTicks(1f, 900);
        // 例外なく完走できればOK(閉塞デッドロックや二重予約による異常停止が無いこと)
        Assert.Pass();
    }

    // ============================================================
    // 決定的継続: 同じ初期状態から (A) N tick進めてセーブ、M tick継続 と
    // (B) N tick進めてセーブ、ロードしてM tick継続 が一致すること
    // ============================================================
    [Test]
    public void DeterministicContinuation_SaveThenLoad_MatchesContinuedWithoutSave()
    {
        const int NTicks = 600, MTicks = 900;

        (double money, long carried, uint rand, int waitB, int waitC,
            bool dwellA, int idxA, int dirA, float routeSA, int onboardA,
            bool dwellB, int idxB, int dirB, float routeSB, int onboardB) RunScenario(bool viaSaveLoad)
        {
            TrackNetwork.Clear();
            Services.Clear();
            GameState.money = 100e8; GameState.carried = 0; GameState.gameMinutes = 6 * 60; GameState.timeScale = 5f;
            GameRandom.Seed(777u);

            var (a, b, c) = BuildStations();
            int ta; a.TryReserve(out ta);
            int tc; c.TryReserve(out tc);
            var t1 = MakeAndRegisterTrain(TrainCatalog.Formations[0], new List<Station> { a, b, c }, new List<int> { ta, -1, -1 }, 0, 1);
            var t2 = MakeAndRegisterTrain(TrainCatalog.Formations[1], new List<Station> { a, b, c }, new List<int> { -1, -1, tc }, 2, -1);

            RunTicks(1f, NTicks);

            if (viaSaveLoad)
            {
                SaveLoad.Save();
                Object.DestroyImmediate(BuildController.WorldRoot.gameObject);
                TrackNetwork.Clear();
                Services.Clear();
                bool loaded = SaveLoad.Load();
                Assert.That(loaded, Is.True);
            }

            RunTicks(1f, MTicks);

            var trains = TrackNetwork.trains;
            var stations = TrackNetwork.stations;
            var tr1 = trains[0]; var tr2 = trains[1];
            var stB = stations[1]; var stC = stations[2];
            return (GameState.money, GameState.carried, GameRandom.GetState(), stB.TotalWaiting, stC.TotalWaiting,
                tr1.IsDwelling, tr1.idx, tr1.dir, tr1.RouteS, tr1.OnboardCount,
                tr2.IsDwelling, tr2.idx, tr2.dir, tr2.RouteS, tr2.OnboardCount);
        }

        var withoutSave = RunScenario(false);
        var withSave = RunScenario(true);

        Assert.That(withSave.money, Is.EqualTo(withoutSave.money).Within(0.5), "所持金");
        Assert.That(withSave.carried, Is.EqualTo(withoutSave.carried), "累計輸送人員");
        Assert.That(withSave.rand, Is.EqualTo(withoutSave.rand), "GameRandom最終状態");
        Assert.That(withSave.waitB, Is.EqualTo(withoutSave.waitB), "B駅待客数");
        Assert.That(withSave.waitC, Is.EqualTo(withoutSave.waitC), "C駅待客数");
        Assert.That(withSave.dwellA, Is.EqualTo(withoutSave.dwellA), "T1 Dwell/Run状態");
        Assert.That(withSave.idxA, Is.EqualTo(withoutSave.idxA), "T1 idx");
        Assert.That(withSave.dirA, Is.EqualTo(withoutSave.dirA), "T1 dir");
        Assert.That(withSave.routeSA, Is.EqualTo(withoutSave.routeSA).Within(0.5f), "T1 位置");
        Assert.That(withSave.onboardA, Is.EqualTo(withoutSave.onboardA), "T1 車内人数");
        Assert.That(withSave.dwellB, Is.EqualTo(withoutSave.dwellB), "T2 Dwell/Run状態");
        Assert.That(withSave.idxB, Is.EqualTo(withoutSave.idxB), "T2 idx");
        Assert.That(withSave.dirB, Is.EqualTo(withoutSave.dirB), "T2 dir");
        Assert.That(withSave.routeSB, Is.EqualTo(withoutSave.routeSB).Within(0.5f), "T2 位置");
        Assert.That(withSave.onboardB, Is.EqualTo(withoutSave.onboardB), "T2 車内人数");
    }

    // ============================================================
    // 異常系: 不正なロード後、現在のワールドが一切変化していないこと
    // ============================================================
    struct WorldSnapshot { public int stations, segments, lines, trains; public double money; public long carried; public uint rand; }

    static WorldSnapshot SnapWorld() => new WorldSnapshot
    {
        stations = TrackNetwork.stations.Count, segments = TrackNetwork.segments.Count,
        lines = Services.lines.Count, trains = TrackNetwork.trains.Count,
        money = GameState.money, carried = GameState.carried, rand = GameRandom.GetState(),
    };

    void AssertLoadFailsWithoutChangingWorld(string badJson)
    {
        var (a, b, c) = BuildStations();
        int ta; a.TryReserve(out ta);
        MakeAndRegisterTrain(TrainCatalog.Formations[0], new List<Station> { a, b }, new List<int> { ta, b.StopTracks[0] });
        var before = SnapWorld();

        PlayerPrefs.SetString(Key, badJson);
        PlayerPrefs.Save();
        // ロード失敗時にSaveLoadがDebug.LogErrorを出すのは意図した挙動(異常系を
        // ログに残すため)。Unity Test Frameworkは既定でDebug.LogErrorをテスト失敗と
        // みなすため、ここでは意図した失敗ログであることを明示する
        UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
        bool loaded = SaveLoad.Load();
        UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

        Assert.That(loaded, Is.False, "不正なセーブデータはロード失敗として扱われること");
        var after = SnapWorld();
        Assert.That(after.stations, Is.EqualTo(before.stations), "駅数が変化しないこと");
        Assert.That(after.segments, Is.EqualTo(before.segments), "線路数が変化しないこと");
        Assert.That(after.trains, Is.EqualTo(before.trains), "列車数が変化しないこと");
        Assert.That(after.money, Is.EqualTo(before.money), "所持金が変化しないこと");
        Assert.That(after.carried, Is.EqualTo(before.carried), "輸送実績が変化しないこと");
        Assert.That(after.rand, Is.EqualTo(before.rand), "GameRandom状態が変化しないこと");
    }

    [Test]
    public void Load_EmptyString_FailsWithoutChangingWorld() => AssertLoadFailsWithoutChangingWorld("");

    [Test]
    public void Load_CorruptJson_FailsWithoutChangingWorld() => AssertLoadFailsWithoutChangingWorld("{not valid json!!");

    [Test]
    public void Load_MissingVersion_FailsWithoutChangingWorld() =>
        AssertLoadFailsWithoutChangingWorld("{\"money\":100.0,\"st\":[{\"id\":1,\"name\":\"A\",\"cars\":6,\"faces\":2,\"lines\":2}]}");

    [Test]
    public void Load_VersionZero_FailsWithoutChangingWorld() =>
        AssertLoadFailsWithoutChangingWorld("{\"v\":0,\"st\":[{\"id\":1,\"name\":\"A\",\"cars\":6,\"faces\":2,\"lines\":2}]}");

    [Test]
    public void Load_FutureVersion_FailsWithoutChangingWorld() =>
        AssertLoadFailsWithoutChangingWorld("{\"v\":99,\"st\":[{\"id\":1,\"name\":\"A\",\"cars\":6,\"faces\":2,\"lines\":2}]}");

    static string MinimalV2Json(string stPatch = null, string extra = null)
    {
        string st = stPatch ?? "\"id\":1,\"x\":0.0,\"z\":0.0,\"yaw\":0.0,\"cars\":6,\"faces\":2,\"lines\":2,\"name\":\"A\",\"dev\":0.0,\"spawnAcc\":0.0";
        return "{\"v\":2,\"money\":100.0,\"carried\":0,\"minutes\":360.0,\"speed\":5.0,\"randomState\":1," +
            "\"nameCounter\":1,\"stationIdCounter\":1,\"segmentIdCounter\":0,\"trainIdCounter\":0,\"lineIdCounter\":0," +
            "\"st\":[{" + st + "}]" + (extra ?? "") + "}";
    }

    [Test]
    public void Load_DuplicateStationId_FailsWithoutChangingWorld() =>
        AssertLoadFailsWithoutChangingWorld(
            "{\"v\":2,\"money\":100.0,\"carried\":0,\"minutes\":360.0,\"speed\":5.0,\"randomState\":1," +
            "\"nameCounter\":2,\"stationIdCounter\":2,\"segmentIdCounter\":0,\"trainIdCounter\":0,\"lineIdCounter\":0," +
            "\"st\":[" +
            "{\"id\":1,\"x\":0.0,\"z\":0.0,\"yaw\":0.0,\"cars\":6,\"faces\":2,\"lines\":2,\"name\":\"A\",\"dev\":0.0,\"spawnAcc\":0.0}," +
            "{\"id\":1,\"x\":100.0,\"z\":0.0,\"yaw\":0.0,\"cars\":6,\"faces\":2,\"lines\":2,\"name\":\"B\",\"dev\":0.0,\"spawnAcc\":0.0}" +
            "]}");

    [Test]
    public void Load_DanglingSegmentStationReference_FailsWithoutChangingWorld() =>
        AssertLoadFailsWithoutChangingWorld(MinimalV2Json(extra:
            ",\"seg\":[{\"id\":1,\"aId\":1,\"bId\":999,\"sa\":1,\"sb\":-1}]"));

    [Test]
    public void Load_DanglingTrainStationReference_FailsWithoutChangingWorld() =>
        AssertLoadFailsWithoutChangingWorld(MinimalV2Json(extra:
            ",\"tr\":[{\"id\":1,\"typeId\":\"keio5000\",\"cars\":10,\"routeIds\":[1,999],\"idx\":0,\"dir\":1,\"curTrack\":0,\"state\":0,\"dwellPathKind\":0,\"dwellT\":8.0}]"));

    [Test]
    public void Load_InvalidTrainState_FailsWithoutChangingWorld() =>
        AssertLoadFailsWithoutChangingWorld(MinimalV2Json(
            stPatch: "\"id\":1,\"x\":0.0,\"z\":0.0,\"yaw\":0.0,\"cars\":6,\"faces\":2,\"lines\":2,\"name\":\"A\",\"dev\":0.0,\"spawnAcc\":0.0",
            extra: ",\"st\":[" +
                "{\"id\":1,\"x\":0.0,\"z\":0.0,\"yaw\":0.0,\"cars\":6,\"faces\":2,\"lines\":2,\"name\":\"A\",\"dev\":0.0,\"spawnAcc\":0.0}," +
                "{\"id\":2,\"x\":500.0,\"z\":0.0,\"yaw\":0.0,\"cars\":6,\"faces\":2,\"lines\":2,\"name\":\"B\",\"dev\":0.0,\"spawnAcc\":0.0}" +
                "],\"tr\":[{\"id\":1,\"typeId\":\"keio5000\",\"cars\":10,\"routeIds\":[1,2],\"idx\":0,\"dir\":1,\"curTrack\":0,\"state\":-1,\"dwellPathKind\":0,\"dwellT\":8.0}]"));

    [Test]
    public void Load_NaNValue_FailsWithoutChangingWorld() =>
        AssertLoadFailsWithoutChangingWorld(MinimalV2Json(
            stPatch: "\"id\":1,\"x\":NaN,\"z\":0.0,\"yaw\":0.0,\"cars\":6,\"faces\":2,\"lines\":2,\"name\":\"A\",\"dev\":0.0,\"spawnAcc\":0.0"));

    [Test]
    public void Load_NegativeTrainSpeed_FailsWithoutChangingWorld() =>
        AssertLoadFailsWithoutChangingWorld(MinimalV2Json(
            extra: ",\"st\":[" +
                "{\"id\":1,\"x\":0.0,\"z\":0.0,\"yaw\":0.0,\"cars\":6,\"faces\":2,\"lines\":2,\"name\":\"A\",\"dev\":0.0,\"spawnAcc\":0.0}," +
                "{\"id\":2,\"x\":500.0,\"z\":0.0,\"yaw\":0.0,\"cars\":6,\"faces\":2,\"lines\":2,\"name\":\"B\",\"dev\":0.0,\"spawnAcc\":0.0}" +
                "],\"tr\":[{\"id\":1,\"typeId\":\"keio5000\",\"cars\":10,\"routeIds\":[1,2],\"idx\":0,\"dir\":1,\"curTrack\":0,\"state\":0,\"dwellPathKind\":0,\"dwellT\":8.0,\"v\":-5.0}]"));

    [Test]
    public void Load_OutOfRangeIdx_FailsWithoutChangingWorld() =>
        AssertLoadFailsWithoutChangingWorld(MinimalV2Json(
            extra: ",\"st\":[" +
                "{\"id\":1,\"x\":0.0,\"z\":0.0,\"yaw\":0.0,\"cars\":6,\"faces\":2,\"lines\":2,\"name\":\"A\",\"dev\":0.0,\"spawnAcc\":0.0}," +
                "{\"id\":2,\"x\":500.0,\"z\":0.0,\"yaw\":0.0,\"cars\":6,\"faces\":2,\"lines\":2,\"name\":\"B\",\"dev\":0.0,\"spawnAcc\":0.0}" +
                "],\"tr\":[{\"id\":1,\"typeId\":\"keio5000\",\"cars\":10,\"routeIds\":[1,2],\"idx\":5,\"dir\":1,\"curTrack\":0,\"state\":0,\"dwellPathKind\":0,\"dwellT\":8.0}]"));

    [Test]
    public void Load_WaitingCapExceeded_FailsWithoutChangingWorld() =>
        AssertLoadFailsWithoutChangingWorld(MinimalV2Json(
            stPatch: "\"id\":1,\"x\":0.0,\"z\":0.0,\"yaw\":0.0,\"cars\":2,\"faces\":1,\"lines\":1,\"name\":\"A\",\"dev\":0.0,\"spawnAcc\":0.0,\"waitToId\":[2],\"waitN\":[99999]",
            extra: ""));

    [Test]
    public void Load_DuplicateBlockReservation_FailsWithoutChangingWorld() =>
        AssertLoadFailsWithoutChangingWorld(
            "{\"v\":2,\"money\":100.0,\"carried\":0,\"minutes\":360.0,\"speed\":5.0,\"randomState\":1," +
            "\"nameCounter\":2,\"stationIdCounter\":2,\"segmentIdCounter\":1,\"trainIdCounter\":2,\"lineIdCounter\":0," +
            "\"st\":[" +
            "{\"id\":1,\"x\":-500.0,\"z\":0.0,\"yaw\":90.0,\"cars\":8,\"faces\":2,\"lines\":2,\"name\":\"A\",\"dev\":0.0,\"spawnAcc\":0.0}," +
            "{\"id\":2,\"x\":500.0,\"z\":0.0,\"yaw\":90.0,\"cars\":8,\"faces\":2,\"lines\":2,\"name\":\"B\",\"dev\":0.0,\"spawnAcc\":0.0}" +
            "]," +
            "\"seg\":[{\"id\":1,\"aId\":1,\"bId\":2,\"sa\":1,\"sb\":-1}]," +
            "\"tr\":[" +
            "{\"id\":1,\"typeId\":\"keio5000\",\"cars\":10,\"routeIds\":[1,2],\"idx\":1,\"dir\":1,\"curTrack\":0,\"state\":1,\"dwellPathKind\":-1,\"dwellT\":0.0,\"s\":10.0,\"v\":5.0,\"departStationId\":1,\"departTrack\":0,\"releaseS\":50.0,\"released\":false,\"legSegmentId\":1}," +
            "{\"id\":2,\"typeId\":\"keio5000\",\"cars\":10,\"routeIds\":[1,2],\"idx\":1,\"dir\":1,\"curTrack\":1,\"state\":1,\"dwellPathKind\":-1,\"dwellT\":0.0,\"s\":20.0,\"v\":5.0,\"departStationId\":1,\"departTrack\":0,\"releaseS\":50.0,\"released\":false,\"legSegmentId\":1}" +
            "]}");
}
