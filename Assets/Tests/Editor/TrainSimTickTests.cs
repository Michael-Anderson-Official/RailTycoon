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

    // ×20相当(simDt=tick*20)の粗いtickでも、列車がA-B間の線路範囲を大きく逸脱しない
    // (=粗大なオーバーシュート/テレポートが起きない)ことを確認する。
    // 理論上の停止時最大オーバーシュートは d*dt²/2 ≈ 6.17cm(京王5000系・×20相当、
    // Codex CLIレビューで数式検証済み)。ただしA-B2駅だけを直結するとTrain.Init()の
    // cyclic判定によりA⇄B往復になり、任意のtick数後に列車が走行中か停車中か・
    // どちらへ向かっているかを一意に特定できないため、本テストでは「停止目標付近で
    // 静止していること」ではなく「線路の敷設範囲(+余裕)を逸脱していないこと」という
    // より頑健な粗大故障検出で判定する
    [Test]
    public void SimTick_AtHighTimeScale_StaysWithinTrackBounds()
    {
        const float StationX = 4000f;
        var a = EditModeTestHelpers.MakeStation(new Vector3(-StationX, 0, 0), 90, 10, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(StationX, 0, 0), 90, 10, 2, 2, "B");
        EditModeTestHelpers.Connect(a, b);

        var fm = TrainCatalog.Formations[0]; // 京王5000系、maxSpeedKmh=110, decelKmhs=4.0
        int trackA;
        a.TryReserve(out trackA);
        var go = new GameObject("Train");
        go.transform.SetParent(BuildController.WorldRoot, false);
        var train = go.AddComponent<Train>();
        train.Init(fm, new List<Station> { a, b }, new List<int> { trackA, b.StopTracks[0] });

        float bigDt = Bootstrap.TickSeconds * 20f; // ×20相当の1tickぶんのシミュレーション秒数
        var lead = go.transform.GetChild(0);
        // 8000m区間をA⇄B往復させながら、毎tick位置がスロート分の余裕(+300m)を超えて
        // 逸脱しないことを継続的に確認する(単発の最終位置だけでなく全tickで検査することで、
        // 走行中の一時的な吹き飛びも見逃さない)
        for (int i = 0; i < 2000; i++)
        {
            train.SimTick(bigDt);
            train.PlaceCars();
            Assert.That(lead.position.x, Is.InRange(-StationX - 300f, StationX + 300f),
                $"tick {i}: 粗いtickでも線路の敷設範囲(X)を大きく逸脱しないこと");
            Assert.That(Mathf.Abs(lead.position.z), Is.LessThan(50f),
                $"tick {i}: 粗いtickでも線路の敷設範囲(Z、直線区間の中心線からの逸脱)を大きく逸脱しないこと");
        }
    }
}
