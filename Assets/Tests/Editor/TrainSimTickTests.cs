using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// Train.SimTick(固定tickへ移設した移動ロジック)のEditModeテスト。
// 速度・加減速の物理式そのものはUpdate()から一切変更せずSimTick()へ移設しただけなので
// (差分はメソッドの入れ物のみ)、ここでは「配線が壊れていないこと」(発車→加速→
// 妥当な速度域での走行)を確認する。実時間待機は行わず、SimTick呼出回数で進行させる。
public class TrainSimTickTests
{
    [SetUp]
    public void SetUp() => TrackNetwork.Clear();

    [TearDown]
    public void TearDown()
    {
        EditModeTestHelpers.DestroyWorldRoot();
        TrackNetwork.Clear();
    }

    [Test]
    public void SimTick_AcceleratesAndStaysWithinTopSpeed()
    {
        var a = EditModeTestHelpers.MakeStation(new Vector3(-3000, 0, 0), 90, 10, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(3000, 0, 0), 90, 10, 2, 2, "B");
        EditModeTestHelpers.Connect(a, b);

        var fm = TrainCatalog.Formations[0]; // 京王5000系10両, maxSpeedKmh=110
        float vmax = fm.type.maxSpeedKmh / 3.6f;

        int trackA;
        a.TryReserve(out trackA);
        var go = new GameObject("Train");
        go.transform.SetParent(BuildController.WorldRoot, false);
        var train = go.AddComponent<Train>();
        train.Init(fm, new List<Station> { a, b }, new List<int> { trackA, b.StopTracks[0] });

        var lead = go.transform.GetChild(0); // PlaceCarsStaticは先頭からcarTs[0]を配置する
        float tick = Bootstrap.TickSeconds;

        // Dwell(8秒)+加速の助走を十分に消化: 60秒ぶん(3600tick)
        Vector3 posBefore = Vector3.zero;
        bool captured = false;
        for (int i = 0; i < 3600; i++)
        {
            train.SimTick(tick); // timeScale=1相当
            if (i == 3599) { train.PlaceCars(); posBefore = lead.position; captured = true; }
        }
        Assert.That(captured, Is.True);

        // さらに10秒(600tick)進めて、その間の平均速度を測る
        for (int i = 0; i < 600; i++) train.SimTick(tick);
        train.PlaceCars();
        Vector3 posAfter = lead.position;

        float dist = Vector3.Distance(posBefore, posAfter);
        float avgSpeed = dist / (600 * tick);

        Assert.That(avgSpeed, Is.LessThanOrEqualTo(vmax * 1.05f), "最高速度を超えないこと");
        Assert.That(avgSpeed, Is.GreaterThanOrEqualTo(vmax * 0.5f), "60秒走行後には十分に加速していること");
    }
}
