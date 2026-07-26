using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// 曲線・分岐の速度制限。これが無いと渡り線(分岐)を80km/h超で通過してしまう。
// 実物の渡り線は、この寸法(26mで4.6m振る)なら8番分岐器相当で25km/h程度が上限。
// 2026-07-26にユーザーが実機で指摘(「分岐の前なのに70km/h出している」)。
public class SpeedLimitTests
{
    [SetUp]
    public void SetUp()
    {
        TrackNetwork.Clear();
        Services.Clear();
        GameRandom.Seed(777u);
    }

    [TearDown]
    public void TearDown()
    {
        EditModeTestHelpers.DestroyWorldRoot();
        TrackNetwork.Clear();
        Services.Clear();
    }

    static Train Run(int fromTrackIndex, int toTrackIndex, out Station a, out Station b)
    {
        a = EditModeTestHelpers.MakeStation(Vector3.zero, 0, 10, 2, 2, "A");
        b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 6000), 0, 10, 2, 2, "B");
        EditModeTestHelpers.Connect(a, b);
        a.RebuildTrackVisual(); b.RebuildTrackVisual();

        var go = new GameObject("Train");
        go.transform.SetParent(BuildController.WorldRoot, false);
        var train = go.AddComponent<Train>();
        train.id = ++TrackNetwork.trainIdCounter;
        TrackNetwork.trains.Add(train);
        train.Init(TrainCatalog.Formations[0], new List<Station> { a, b },
            new List<int> { a.StopTracks[fromTrackIndex], b.StopTracks[toTrackIndex] });
        return train;
    }

    // 到着駅の渡り線あたり(駅端の手前)を通る間の最高速度
    static float MaxSpeedNearCrossover(Train train, Station b)
    {
        float worst = 0f;
        float czCross = b.HalfLen + StationLayout.ThroatLen - StationLayout.LeadLen * 0.5f;
        float lo = 6000f - czCross - RailKit.CrossoverHalfLength;
        float hi = 6000f - czCross + RailKit.CrossoverHalfLength;
        for (int i = 0; i < 40000; i++)
        {
            train.SimTick(Bootstrap.TickSeconds);
            train.PlaceCars();
            Transform c0 = null;
            foreach (Transform ch in train.transform) if (ch.name == "Car0") { c0 = ch; break; }
            if (c0 != null && c0.position.z >= lo && c0.position.z <= hi)
                worst = Mathf.Max(worst, train.SpeedKmh);
            if (train.IsDwelling && train.ArrivalCount > 0) break;
        }
        return worst;
    }

    [Test]
    public void CrossingAturnout_IsSpeedLimited()
    {
        // 発着で番線の側が変わる=渡り線を渡る
        var train = Run(0, 1, out _, out var b);
        float peak = MaxSpeedNearCrossover(train, b);
        Assert.That(peak, Is.GreaterThan(1f), "そもそも走っていること(テスト前提)");
        Assert.That(peak, Is.LessThan(40f),
            "分岐を渡る区間は40km/h未満に抑えること(実測" + peak.ToString("F0") + "km/h)");
    }

    [Test]
    public void RunningStraightThrough_IsNotSlowedDown()
    {
        // 同じ側の番線どうし=渡り線を渡らないので、制限を掛けてはいけない
        var train = Run(0, 0, out _, out var b);
        float peak = MaxSpeedNearCrossover(train, b);
        Assert.That(peak, Is.GreaterThan(80f),
            "直進では速度を落とさないこと(実測" + peak.ToString("F0") + "km/h)");
    }

    [Test]
    public void Acceleration_MatchesTheRatedFigure()
    {
        // 起動加速度3.3km/h/s(=0.92m/s²)どおりに出ていること。
        // 距離で見る(時間には停車時間が混じるため)
        var train = Run(0, 0, out _, out _);
        float startS = -1f;
        for (int i = 0; i < 40000; i++)
        {
            train.SimTick(Bootstrap.TickSeconds);
            if (startS < 0f && train.SpeedKmh > 0.1f) startS = train.RouteS;
            if (train.SpeedKmh >= 40f)
            {
                float d = train.RouteS - startS;
                float v = 40f / 3.6f;
                float aAvg = v * v / (2f * d);
                Assert.That(aAvg, Is.GreaterThan(0.75f),
                    "0→40km/hの平均加速度が公称の8割を下回らないこと(実測" +
                    aAvg.ToString("F2") + "m/s² / 公称" +
                    (TrainCatalog.Formations[0].type.Accel).ToString("F2") + ")");
                return;
            }
        }
        Assert.Fail("40km/hに達しなかった");
    }

    [Test]
    public void Acceleration_HoldsTheRatedFigureUpToTheBaseSpeed()
    {
        // 定トルク域(最高速度の半分あたり)までは起動加速度がほぼそのまま出ること。
        // 以前は最初から鈍らせていたため、低速から実車より遅かった
        var train = Run(0, 0, out _, out _);
        float startS = -1f;
        float rated = TrainCatalog.Formations[0].type.Accel;
        for (int i = 0; i < 60000; i++)
        {
            train.SimTick(Bootstrap.TickSeconds);
            if (startS < 0f && train.SpeedKmh > 0.1f) startS = train.RouteS;
            if (train.SpeedKmh >= 50f)   // 110km/hの半分弱=定トルク域の内側
            {
                float d = train.RouteS - startS;
                float v = 50f / 3.6f;
                float aAvg = v * v / (2f * d);
                Assert.That(aAvg, Is.GreaterThan(rated * 0.9f),
                    "定トルク域では公称の9割以上を保つこと(実測" + aAvg.ToString("F2") +
                    " / 公称" + rated.ToString("F2") + "m/s²)");
                return;
            }
        }
        Assert.Fail("50km/hに達しなかった");
    }

    [Test]
    public void TopSpeed_IsReachedWithinAReasonableDistance()
    {
        // 最高速度まで伸びること。以前は頭打ちが強すぎて何kmも走らないと出なかった
        var a = EditModeTestHelpers.MakeStation(Vector3.zero, 0, 10, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 20000), 0, 10, 2, 2, "B");
        EditModeTestHelpers.Connect(a, b);
        a.RebuildTrackVisual(); b.RebuildTrackVisual();
        var go = new GameObject("T");
        go.transform.SetParent(BuildController.WorldRoot, false);
        var tr = go.AddComponent<Train>();
        tr.id = ++TrackNetwork.trainIdCounter;
        TrackNetwork.trains.Add(tr);
        var fm = TrainCatalog.Formations[0];   // 京王5000系 最高110km/h
        tr.Init(fm, new List<Station> { a, b }, new List<int> { a.StopTracks[0], b.StopTracks[0] });

        float startS = -1f;
        for (int i = 0; i < 200000; i++)
        {
            tr.SimTick(Bootstrap.TickSeconds);
            if (startS < 0f && tr.SpeedKmh > 0.1f) startS = tr.RouteS;
            if (tr.SpeedKmh >= 100f)
            {
                float d = tr.RouteS - startS;
                Assert.That(d, Is.LessThan(750f),
                    "0→100km/hが750m以内であること(実測" + d.ToString("F0") + "m)");
                return;
            }
        }
        Assert.Fail("100km/hに達しなかった");
    }

    [Test]
    public void SpeedLimit_IsIndependentOfTheSpeedMultiplier()
    {
        // 制限は位置だけの関数。速度倍率を変えても同じ場所で同じ速度になること
        float PeakAt(float scale)
        {
            TrackNetwork.Clear(); Services.Clear();
            EditModeTestHelpers.DestroyWorldRoot();
            GameState.timeScale = scale;
            var t = Run(0, 1, out _, out var bb);
            return MaxSpeedNearCrossover(t, bb);
        }
        float before = GameState.timeScale;
        try
        {
            float p1 = PeakAt(1f);
            float p20 = PeakAt(20f);
            Assert.That(p20, Is.EqualTo(p1).Within(1.0f),
                "×1と×20で分岐通過速度が一致すること(" + p1.ToString("F1") + " / " +
                p20.ToString("F1") + ")");
        }
        finally { GameState.timeScale = before; }
    }
}
