using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// 駅を建て替え/撤去したとき、その駅を**通過するだけ**の列車も経路を作り直すこと。
// 走行中の経路(BuildMultiLeg)は通過駅の構内線形をそのまま取り込むため、取り残すと
// 古い番線位置のまま走り続け、新しいホームの上を走ってしまう。
// 2026-07-26にユーザーが実機で発見(相対式→島式の建て替えで、通過列車が
// 番線±2.30のまま新しい島式ホーム(x∈[-4,4])の中を走っていた)。
//
// 測り方の注意: Train.SimTickは論理状態しか進めない。車体の位置は
// Bootstrap.RunFrameが呼ぶPlaceCars()で初めて反映される。**両方を回すこと**。
// これを忘れると初期配置しか測れず、走行中の不具合を丸ごと見逃す(実際に見逃した)。
public class StationRebuildResyncTests
{
    [SetUp]
    public void SetUp()
    {
        TrackNetwork.Clear();
        Services.Clear();
        GameState.money = 100000e8;
    }

    [TearDown]
    public void TearDown()
    {
        EditModeTestHelpers.DestroyWorldRoot();
        TrackNetwork.Clear();
        Services.Clear();
    }

    static (BuildController bc, Station a, Station b, Station c, Train train) Setup(int faces, int lines)
    {
        var bcGo = new GameObject("BC");
        var bc = bcGo.AddComponent<BuildController>();
        var a = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 0), 0, 10, faces, lines, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 1600), 0, 10, faces, lines, "B");
        var c = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 3200), 0, 10, faces, lines, "C");
        EditModeTestHelpers.Connect(a, b);
        EditModeTestHelpers.Connect(b, c);
        a.RebuildTrackVisual(); b.RebuildTrackVisual(); c.RebuildTrackVisual();

        var go = new GameObject("Train");
        go.transform.SetParent(BuildController.WorldRoot, false);
        var train = go.AddComponent<Train>();
        train.id = ++TrackNetwork.trainIdCounter;
        TrackNetwork.trains.Add(train);
        // Bは経路に入れない=通過駅
        train.Init(TrainCatalog.Formations[0], new List<Station> { a, c },
            new List<int> { a.StopTracks[0], c.StopTracks[0] });
        return (bc, a, b, c, train);
    }

    // tick分だけ進めつつ、B構内を通る間の最悪の食い込み深さを返す
    static float RunAndMeasure(Train train, Station b, int ticks, int rebuildAt,
        System.Action rebuild)
    {
        float worst = 0f;
        for (int t = 0; t < ticks; t++)
        {
            if (t == rebuildAt && rebuild != null) rebuild();
            train.SimTick(Bootstrap.TickSeconds);
            train.PlaceCars();          // ← これが無いと車体が動かず何も測れない
            if (t % 4 != 0) continue;

            foreach (Transform car in train.transform)
            {
                if (!car.name.StartsWith("Car")) continue;
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    Vector3 p = car.position + car.right * (sgn * RailDimensions.CarBodyHalfWidth);
                    if (!b.PlatformAreaContains(p, 0f)) continue;
                    var loc = b.transform.InverseTransformPoint(p);
                    foreach (var pl in b.layout.platforms)
                    {
                        float visualW = Mathf.Max(2.6f, pl.y - 0.02f);
                        float d = visualW * 0.5f - Mathf.Abs(loc.x - pl.x);
                        if (d > worst) worst = d;
                    }
                }
            }
        }
        return worst;
    }

    [Test]
    public void RebuildingATransitStation_KeepsThePassingTrainOffThePlatform()
    {
        // まず建て替えをしない場合の食い込み量を測る。曲線区間では車体が
        // ホーム縁へ数cm寄るのが元々の挙動なので、0と比べるのではなく
        // 「建て替えても悪化しないこと」を見る
        float baseline;
        var (bc0, _, b0, _, train0) = Setup(1, 2);   // 島式のまま走らせる
        try { baseline = RunAndMeasure(train0, b0, 9000, -1, null); }
        finally { Object.DestroyImmediate(bc0.gameObject); }

        TearDown();
        SetUp();

        var (bc, _, b, _, train) = Setup(2, 2);   // 相対式(番線±2.30)
        try
        {
            // 発車後に島式(ホームx∈[-4,4]、番線±5.48)へ建て替える。
            // 経路が古いままなら、列車は±2.30=新ホームの真上を走り、
            // 食い込みは車体幅ぶん(1m超)になる
            float worst = RunAndMeasure(train, b, 9000, 1200, () =>
            {
                Assert.That(bc.RebuildStation(b, 10, 1, 2), Is.True, "建て替えが成功すること");
                b.RebuildTrackVisual();
            });
            Assert.That(worst, Is.LessThanOrEqualTo(baseline + 0.01f),
                "建て替えても食い込みが悪化しないこと(建て替えあり" + worst.ToString("F2") +
                "m / 建て替えなし" + baseline.ToString("F2") + "m)");
        }
        finally { Object.DestroyImmediate(bc.gameObject); }
    }

    [Test]
    public void RebuildingATransitStation_MovesThePassingTrainToTheNewTrack()
    {
        var (bc, _, b, _, train) = Setup(2, 2);
        try
        {
            for (int t = 0; t < 1200; t++) { train.SimTick(Bootstrap.TickSeconds); train.PlaceCars(); }
            Assert.That(bc.RebuildStation(b, 10, 1, 2), Is.True);
            b.RebuildTrackVisual();

            // 建て替え後にB構内を通るとき、必ず新しい番線(±5.48)の上にいること
            bool sawInside = false;
            for (int t = 0; t < 12000; t++)
            {
                train.SimTick(Bootstrap.TickSeconds);
                train.PlaceCars();
                if (t % 4 != 0) continue;
                foreach (Transform car in train.transform)
                {
                    if (car.name != "Car0") continue;
                    if (Mathf.Abs(car.position.z - 1600f) > 60f) continue;
                    sawInside = true;
                    float best = float.MaxValue;
                    foreach (float off in b.layout.trackOffsets)
                        best = Mathf.Min(best, Mathf.Abs(car.position.x - off));
                    Assert.That(best, Is.LessThan(0.3f),
                        "建て替え後の番線に乗っていること(最寄り番線まで" + best.ToString("F2") + "m)");
                }
            }
            Assert.That(sawInside, Is.True, "列車がB構内を通過したこと(テスト前提)");
        }
        finally { Object.DestroyImmediate(bc.gameObject); }
    }

    [Test]
    public void PassesThrough_CoversTransitStationsNotOnlyStops()
    {
        var (bc, a, b, c, train) = Setup(2, 2);
        try
        {
            // 発車してBへ向かう脚を作らせる
            for (int t = 0; t < 1200; t++) { train.SimTick(Bootstrap.TickSeconds); train.PlaceCars(); }
            Assert.That(train.RouteHas(b), Is.False, "Bは停車駅ではないこと(テスト前提)");
            Assert.That(train.PassesThrough(b), Is.True, "通過駅も関わりありと判定すること");
            Assert.That(train.PassesThrough(a), Is.True, "停車駅も従来どおり真");
            Assert.That(train.PassesThrough(c), Is.True);
        }
        finally { Object.DestroyImmediate(bc.gameObject); }
    }

    [Test]
    public void RebuildingATransitStation_DoesNotLeakTrackReservations()
    {
        var (bc, _, b, _, train) = Setup(2, 2);
        try
        {
            for (int t = 0; t < 1200; t++) { train.SimTick(Bootstrap.TickSeconds); train.PlaceCars(); }
            Assert.That(bc.RebuildStation(b, 10, 1, 2), Is.True);
            b.RebuildTrackVisual();
            // 再同期でA駅へ戻るので、通過駅Bの予約は残っていてはいけない
            foreach (bool occupied in b.occupied)
                Assert.That(occupied, Is.False, "通過駅の番線予約が漏れないこと");
        }
        finally { Object.DestroyImmediate(bc.gameObject); }
    }

    [Test]
    public void RemovingATransitStation_FoldsUpTheStrandedTrain()
    {
        // 通過駅を撤去すると経路が分断される。走れなくなった列車と系統は畳むこと。
        // 残すと、発車できないまま居座る列車と到達不能な系統が残る
        // (実装後レビューでCodex CLIが指摘)
        var (bc, a, b, c, train) = Setup(2, 2);
        try
        {
            for (int t = 0; t < 1200; t++) { train.SimTick(Bootstrap.TickSeconds); train.PlaceCars(); }
            Assert.That(TrackNetwork.FindPath(a, c), Is.Not.Null, "撤去前はA→Cが繋がっていること");

            double before = GameState.money;
            bc.RemoveStation(b);

            Assert.That(TrackNetwork.FindPath(a, c), Is.Null, "撤去でA→Cが分断されること(テスト前提)");
            Assert.That(TrackNetwork.trains.Contains(train), Is.False,
                "走れなくなった列車が畳まれること");
            Assert.That(GameState.money, Is.GreaterThan(before),
                "撤去した列車ぶんの払い戻しが計上されること");
        }
        finally { Object.DestroyImmediate(bc.gameObject); }
    }

    [Test]
    public void RemovingAStationOffTheRoute_KeepsARunnableTrain()
    {
        // 撤去しても経路が繋がったままなら列車は残す
        var (bc, a, _, c, train) = Setup(2, 2);
        try
        {
            var d = EditModeTestHelpers.MakeStation(new Vector3(600, 0, 4800), 30, 10, 2, 2, "D");
            EditModeTestHelpers.Connect(c, d);
            d.RebuildTrackVisual(); c.RebuildTrackVisual();
            for (int t = 0; t < 1200; t++) { train.SimTick(Bootstrap.TickSeconds); train.PlaceCars(); }

            bc.RemoveStation(d);   // 経路(A→C)には無関係な駅
            Assert.That(TrackNetwork.FindPath(a, c), Is.Not.Null);
            Assert.That(TrackNetwork.trains.Contains(train), Is.True,
                "無関係な撤去では列車を消さないこと");
        }
        finally { Object.DestroyImmediate(bc.gameObject); }
    }

    [Test]
    public void RebuildingAStationTheTrainStopsAt_StillWorks()
    {
        // 既存の挙動(停車駅の建て替え)が壊れていないこと
        var (bc, a, _, _, train) = Setup(2, 2);
        try
        {
            Assert.That(bc.RebuildStation(a, 10, 2, 4), Is.True);
            a.RebuildTrackVisual();
            Assert.That(train.curTrack, Is.InRange(0, a.occupied.Length - 1));
            for (int t = 0; t < 3000; t++) { train.SimTick(Bootstrap.TickSeconds); train.PlaceCars(); }
        }
        finally { Object.DestroyImmediate(bc.gameObject); }
    }
}
