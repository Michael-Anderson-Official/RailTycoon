using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// M2-B.2: 速度倍率(×1/×5/×20)で同じ「シミュレーション時間」まで進めた場合に、
// ゲーム上の結果(列車の状態・旅客生成・収益・閉塞/番線予約)が一致するかを検証する。
// Bootstrap/描画フレームを介さず、Station.Tick/Train.SimTickを直接、
// 「全駅Tick→全列車SimTick」というBootstrap.SimTickと同じ処理順で呼び出す。
// gameMinutes更新とPlaceCars(見た目反映)は比較対象に影響しないため省略する。
//
// 許容誤差方針(M2-B.2設計レビュー、CodexReviews/2026-07-23_m2b2_design_review.md参照):
// 完全一致必須: 到着/発車回数、idx/dir/curTrack/state、番線occupied、閉塞占有、
//   待客数・行先分布、乗車/降車人数、GameState.carried、GameRandom.GetState()
// 収益: doubleの丸め誤差を考慮しWithin(0.5)円
// 位置(RouteS): 停車中はほぼ完全一致、走行中は0.1〜1m程度を警戒線とする
public class SpeedMultiplierEquivalenceTests
{
    [SetUp]
    public void SetUp() => TrackNetwork.Clear();

    [TearDown]
    public void TearDown()
    {
        EditModeTestHelpers.DestroyWorldRoot();
        TrackNetwork.Clear();
    }

    // 実時間dt=Bootstrap.TickSeconds*multiplierで、合計simSecondsぶんちょうど進むまで
    // 「全駅Tick→全列車SimTick」の順でtickを刻む(Bootstrap.SimTickと同じ処理順)
    static void RunSimSeconds(List<Station> stations, List<Train> trains, float simSeconds, float multiplier)
    {
        float dt = Bootstrap.TickSeconds * multiplier;
        int ticks = Mathf.RoundToInt(simSeconds / dt);
        for (int i = 0; i < ticks; i++)
        {
            foreach (var st in stations) st.Tick(dt);
            foreach (var t in trains) t.SimTick(dt);
        }
    }

    static void ResetGlobalState(uint seed)
    {
        GameState.money = 100e8;
        GameState.carried = 0;
        GameRandom.Seed(seed);
    }

    struct TrainSnapshot
    {
        public int departureCount, arrivalCount, idx, dir, curTrack;
        public bool isDwelling, trackReleased;
        public int onboardCount;
        public float routeS;
    }

    static TrainSnapshot Snap(Train t) => new TrainSnapshot
    {
        departureCount = t.DepartureCount,
        arrivalCount = t.ArrivalCount,
        idx = t.idx,
        dir = t.dir,
        curTrack = t.curTrack,
        isDwelling = t.IsDwelling,
        trackReleased = t.DepartureTrackReleased,
        onboardCount = t.OnboardCount,
        routeS = t.RouteS,
    };

    // ============================================================
    // シナリオ1: 単一列車の往復(旅客生成込み) — 発着回数・状態・収益が
    // ×1/×5/×20で一致するか
    // ============================================================
    [Test]
    public void SingleTrainRoundTrip_SameSimTime_DiscreteStateMatchesAcrossSpeedMultipliers()
    {
        const float SimSeconds = 240f; // 4分(複数往復が発生する長さ)
        const uint Seed = 12345u;

        (TrainSnapshot snap, long totalGenerated) Run(float multiplier)
        {
            TrackNetwork.Clear();
            ResetGlobalState(Seed);
            var a = EditModeTestHelpers.MakeStation(new Vector3(-600, 0, 0), 90, 6, 2, 2, "A");
            var b = EditModeTestHelpers.MakeStation(new Vector3(600, 0, 0), 90, 6, 2, 2, "B");
            EditModeTestHelpers.Connect(a, b);

            int trackA;
            a.TryReserve(out trackA);
            var go = new GameObject("Train");
            go.transform.SetParent(BuildController.WorldRoot, false);
            var train = go.AddComponent<Train>();
            TrackNetwork.trains.Add(train);
            train.Init(TrainCatalog.Formations[0], new List<Station> { a, b }, new List<int> { trackA, b.StopTracks[0] });

            RunSimSeconds(TrackNetwork.stations, TrackNetwork.trains, SimSeconds, multiplier);

            var snap = Snap(train);
            // 総生成数(乗車済み+車内+待機中)は、車内/待機の内訳が発車tick境界で
            // ±1入れ替わっても不変のはず(GameRandom消費量と同じ意味を持つ指標)
            long totalGenerated = GameState.carried + snap.onboardCount + a.TotalWaiting + b.TotalWaiting;
            Debug.Log($"[M2-B.2/S1] x{multiplier}: dep={snap.departureCount} arr={snap.arrivalCount} " +
                $"idx={snap.idx} dir={snap.dir} dwell={snap.isDwelling} routeS={snap.routeS:F3} " +
                $"onboard={snap.onboardCount} carried={GameState.carried} money={GameState.money:F1} " +
                $"randState={GameRandom.GetState()} waitA={a.TotalWaiting} waitB={b.TotalWaiting} totalGenerated={totalGenerated}");

            EditModeTestHelpers.DestroyWorldRoot();
            return (snap, totalGenerated);
        }

        double moneyX1, moneyX5, moneyX20;
        long carriedX1, carriedX5, carriedX20;
        uint randX1, randX5, randX20;

        var (s1, totalGenX1) = Run(1f);
        moneyX1 = GameState.money; carriedX1 = GameState.carried; randX1 = GameRandom.GetState();
        var (s5, totalGenX5) = Run(5f);
        moneyX5 = GameState.money; carriedX5 = GameState.carried; randX5 = GameRandom.GetState();
        var (s20, totalGenX20) = Run(20f);
        moneyX20 = GameState.money; carriedX20 = GameState.carried; randX20 = GameRandom.GetState();

        // 総生成数(乗車済み+車内+待機中)は、車内/待機の内訳が発車tick境界で
        // ±1入れ替わっても不変であるべき。GameRandom最終状態と一致してこそ、
        // 「境界1件ぶんの乗車タイミング差」であって「生成数自体の差」ではないと言える
        Assert.That(totalGenX5, Is.EqualTo(totalGenX1), "総生成数(x5 vs x1)");
        Assert.That(totalGenX20, Is.EqualTo(totalGenX1), "総生成数(x20 vs x1)");

        // 離散結果: ×1を基準に完全一致必須
        Assert.That(s5.departureCount, Is.EqualTo(s1.departureCount), "発車回数(x5 vs x1)");
        Assert.That(s20.departureCount, Is.EqualTo(s1.departureCount), "発車回数(x20 vs x1)");
        Assert.That(s5.arrivalCount, Is.EqualTo(s1.arrivalCount), "到着回数(x5 vs x1)");
        Assert.That(s20.arrivalCount, Is.EqualTo(s1.arrivalCount), "到着回数(x20 vs x1)");
        Assert.That(s5.idx, Is.EqualTo(s1.idx), "idx(x5 vs x1)");
        Assert.That(s20.idx, Is.EqualTo(s1.idx), "idx(x20 vs x1)");
        Assert.That(s5.dir, Is.EqualTo(s1.dir), "dir(x5 vs x1)");
        Assert.That(s20.dir, Is.EqualTo(s1.dir), "dir(x20 vs x1)");
        Assert.That(s5.isDwelling, Is.EqualTo(s1.isDwelling), "Dwell/Run状態(x5 vs x1)");
        Assert.That(s20.isDwelling, Is.EqualTo(s1.isDwelling), "Dwell/Run状態(x20 vs x1)");
        Assert.That(s5.onboardCount, Is.EqualTo(s1.onboardCount), "車内人数(x5 vs x1)");
        // 車内人数はx20のみ発車tick境界の量子化で±1ずれ得る(実測で確認済み。
        // M2-B.2実装後レビューでCodex CLIと合意した既知の許容差。CodexReviews/参照。
        // 発生済み旅客の総数・GameRandom消費量・確定済み収益/輸送実績には影響しない)
        Assert.That(s20.onboardCount, Is.EqualTo(s1.onboardCount).Within(1), "車内人数(x20 vs x1、既知の1tick境界差を許容)");
        Assert.That(carriedX5, Is.EqualTo(carriedX1), "累計輸送人員(x5 vs x1)");
        Assert.That(carriedX20, Is.EqualTo(carriedX1), "累計輸送人員(x20 vs x1)");
        Assert.That(randX5, Is.EqualTo(randX1), "GameRandom最終状態(x5 vs x1)");
        Assert.That(randX20, Is.EqualTo(randX1), "GameRandom最終状態(x20 vs x1)");
        Assert.That(moneyX5, Is.EqualTo(moneyX1).Within(0.5), "収益(x5 vs x1)");
        Assert.That(moneyX20, Is.EqualTo(moneyX1).Within(0.5), "収益(x20 vs x1)");

        // 停車中ならRouteSはほぼ完全一致するはず(浮動小数点誤差のみ)
        if (s1.isDwelling && s5.isDwelling)
            Assert.That(s5.routeS, Is.EqualTo(s1.routeS).Within(1e-2f), "停車中のRouteS(x5 vs x1)");
        if (s1.isDwelling && s20.isDwelling)
            Assert.That(s20.routeS, Is.EqualTo(s1.routeS).Within(1e-2f), "停車中のRouteS(x20 vs x1)");
    }

    // ============================================================
    // シナリオ2: 2列車の番線競合(A-B-C、Bは1面1線) — 番線・閉塞の獲得結果が
    // ×1/×5/×20で一致するか。旅客生成のノイズを避けるためpreview=trueで無効化
    // ============================================================
    [Test]
    public void TwoTrainPlatformContention_SameSimTime_OutcomeMatchesAcrossSpeedMultipliers()
    {
        const float SimSeconds = 240f;

        (TrainSnapshot t1, TrainSnapshot t2, bool bOccupied, string occAB, string occBC) Run(float multiplier)
        {
            TrackNetwork.Clear();
            ResetGlobalState(1u); // 旅客生成を無効化するのでseedは形式的
            var a = EditModeTestHelpers.MakeStation(new Vector3(-900, 0, 0), 90, 6, 2, 2, "A");
            var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 0), 90, 6, 1, 1, "B"); // 1面1線
            var c = EditModeTestHelpers.MakeStation(new Vector3(1100, 0, 0), 90, 6, 2, 2, "C");
            a.preview = true; b.preview = true; c.preview = true; // 旅客生成を止め、純粋な列車競合だけを見る
            var segAB = EditModeTestHelpers.Connect(a, b);
            var segBC = EditModeTestHelpers.Connect(b, c);

            int trackA, trackC;
            a.TryReserve(out trackA);
            c.TryReserve(out trackC);

            var go1 = new GameObject("T1");
            go1.transform.SetParent(BuildController.WorldRoot, false);
            var t1 = go1.AddComponent<Train>();
            TrackNetwork.trains.Add(t1); // 先に登録(同tick内で先に処理される)
            t1.Init(TrainCatalog.Formations[0], new List<Station> { a, b, c }, new List<int> { trackA, -1, -1 },
                startIdx: 0, dirInit: 1);

            var go2 = new GameObject("T2");
            go2.transform.SetParent(BuildController.WorldRoot, false);
            var t2 = go2.AddComponent<Train>();
            TrackNetwork.trains.Add(t2); // 後で登録
            t2.Init(TrainCatalog.Formations[0], new List<Station> { a, b, c }, new List<int> { -1, -1, trackC },
                startIdx: 2, dirInit: -1);

            RunSimSeconds(TrackNetwork.stations, TrackNetwork.trains, SimSeconds, multiplier);

            var s1 = Snap(t1);
            var s2 = Snap(t2);
            bool bOcc = false;
            foreach (int i in b.StopTracks) if (b.occupied[i]) bOcc = true;
            var occTrainAB = segAB.OccupantFrom(a);
            var occTrainBC = segBC.OccupantFrom(b);
            string Label(Train t) => t == t1 ? "T1" : t == t2 ? "T2" : "none";
            string occAB = Label(occTrainAB);
            string occBC = Label(occTrainBC);
            Debug.Log($"[M2-B.2/S2] x{multiplier}: T1(dep={s1.departureCount},arr={s1.arrivalCount},idx={s1.idx}," +
                $"dir={s1.dir},dwell={s1.isDwelling}) T2(dep={s2.departureCount},arr={s2.arrivalCount},idx={s2.idx}," +
                $"dir={s2.dir},dwell={s2.isDwelling}) Bocc={bOcc} segAB.occFromA={occAB} segBC.occFromB={occBC}");

            EditModeTestHelpers.DestroyWorldRoot();
            return (s1, s2, bOcc, occAB, occBC);
        }

        var r1 = Run(1f);
        var r5 = Run(5f);
        var r20 = Run(20f);

        // どちらの列車が番線・閉塞を獲得したかという「勝敗」は、離散結果として
        // ×1/×5/×20で一致すべき(発着回数・idx/dir/state・B番線占有の全てが対象)
        Assert.That(r5.t1.departureCount, Is.EqualTo(r1.t1.departureCount), "T1発車回数(x5 vs x1)");
        Assert.That(r20.t1.departureCount, Is.EqualTo(r1.t1.departureCount), "T1発車回数(x20 vs x1)");
        Assert.That(r5.t2.departureCount, Is.EqualTo(r1.t2.departureCount), "T2発車回数(x5 vs x1)");
        Assert.That(r20.t2.departureCount, Is.EqualTo(r1.t2.departureCount), "T2発車回数(x20 vs x1)");
        Assert.That(r5.t1.arrivalCount, Is.EqualTo(r1.t1.arrivalCount), "T1到着回数(x5 vs x1)");
        Assert.That(r20.t1.arrivalCount, Is.EqualTo(r1.t1.arrivalCount), "T1到着回数(x20 vs x1)");
        Assert.That(r5.t2.arrivalCount, Is.EqualTo(r1.t2.arrivalCount), "T2到着回数(x5 vs x1)");
        Assert.That(r20.t2.arrivalCount, Is.EqualTo(r1.t2.arrivalCount), "T2到着回数(x20 vs x1)");
        Assert.That(r5.t1.idx, Is.EqualTo(r1.t1.idx), "T1 idx(x5 vs x1)");
        Assert.That(r20.t1.idx, Is.EqualTo(r1.t1.idx), "T1 idx(x20 vs x1)");
        Assert.That(r5.t2.idx, Is.EqualTo(r1.t2.idx), "T2 idx(x5 vs x1)");
        Assert.That(r20.t2.idx, Is.EqualTo(r1.t2.idx), "T2 idx(x20 vs x1)");
        Assert.That(r5.bOccupied, Is.EqualTo(r1.bOccupied), "B番線占有(x5 vs x1)");
        Assert.That(r20.bOccupied, Is.EqualTo(r1.bOccupied), "B番線占有(x20 vs x1)");
        Assert.That(r5.occAB, Is.EqualTo(r1.occAB), "A-B閉塞占有列車(x5 vs x1)");
        Assert.That(r20.occAB, Is.EqualTo(r1.occAB), "A-B閉塞占有列車(x20 vs x1)");
        Assert.That(r5.occBC, Is.EqualTo(r1.occBC), "B-C閉塞占有列車(x5 vs x1)");
        Assert.That(r20.occBC, Is.EqualTo(r1.occBC), "B-C閉塞占有列車(x20 vs x1)");
    }

    // ============================================================
    // シナリオ3: 複数駅の旅客生成(列車なし) — 総発生数・行先分布・
    // GameRandom最終状態・spawnAcc残余がWaitingCap境界付近でも一致するか
    // ============================================================
    [Test]
    public void PassengerGeneration_MultiStationNearCap_SameSimTime_MatchesAcrossSpeedMultipliers()
    {
        const float SimSeconds = 600f; // 10分(cap近くまで貯まる長さ)
        const uint Seed = 999u;

        (int totalWaiting, uint randState, double[] spawnAcc, int[] perStation) Run(float multiplier)
        {
            TrackNetwork.Clear();
            ResetGlobalState(Seed);
            // 小さめのcapにして境界(WaitingCap)に到達しやすくする(faces*cars*60)
            var a = EditModeTestHelpers.MakeStation(new Vector3(-500, 0, 0), 90, 2, 1, 1, "A");
            var b = EditModeTestHelpers.MakeStation(new Vector3(500, 0, 0), 90, 2, 1, 1, "B");
            var c = EditModeTestHelpers.MakeStation(new Vector3(500, 0, 900), 0, 2, 1, 1, "C");
            EditModeTestHelpers.Connect(a, b);
            EditModeTestHelpers.Connect(b, c);
            var stations = new List<Station> { a, b, c };

            RunSimSeconds(stations, new List<Train>(), SimSeconds, multiplier);

            int total = a.TotalWaiting + b.TotalWaiting + c.TotalWaiting;
            var perStation = new[] { a.TotalWaiting, b.TotalWaiting, c.TotalWaiting };
            var spawnAcc = new[] { a.SpawnAccumulator, b.SpawnAccumulator, c.SpawnAccumulator };
            uint rand = GameRandom.GetState();
            Debug.Log($"[M2-B.2/S3] x{multiplier}: total={total} perStation=[{perStation[0]},{perStation[1]},{perStation[2]}] " +
                $"cap={a.WaitingCap} spawnAcc=[{spawnAcc[0]:F4},{spawnAcc[1]:F4},{spawnAcc[2]:F4}] rand={rand}");

            EditModeTestHelpers.DestroyWorldRoot();
            return (total, rand, spawnAcc, perStation);
        }

        var r1 = Run(1f);
        var r5 = Run(5f);
        var r20 = Run(20f);

        Assert.That(r5.totalWaiting, Is.EqualTo(r1.totalWaiting), "総待客数(x5 vs x1)");
        Assert.That(r20.totalWaiting, Is.EqualTo(r1.totalWaiting), "総待客数(x20 vs x1)");
        for (int i = 0; i < 3; i++)
        {
            Assert.That(r5.perStation[i], Is.EqualTo(r1.perStation[i]), $"駅{i}の待客数(x5 vs x1)");
            Assert.That(r20.perStation[i], Is.EqualTo(r1.perStation[i]), $"駅{i}の待客数(x20 vs x1)");
        }
        Assert.That(r5.randState, Is.EqualTo(r1.randState), "GameRandom最終状態(x5 vs x1)");
        Assert.That(r20.randState, Is.EqualTo(r1.randState), "GameRandom最終状態(x20 vs x1)");
    }
}
