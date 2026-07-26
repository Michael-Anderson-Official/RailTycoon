using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// 駅を短く建て替えると、その駅を使っている長い編成がホームからはみ出す。
// DispatchTrainは「この駅は6両対応なので10両は停まれません」を配車の瞬間にしか
// 見ないため、建て替え側にも同じ基準が要る。
// これを開けておくと、10両が停車中の駅を2両対応へ縮められ、車体が前後へ65mずつ
// はみ出して頭端駅では車止めを突き抜けた見た目になる(2026-07-26にユーザーが報告)。
public class StationShrinkTests
{
    [SetUp]
    public void SetUp()
    {
        TrackNetwork.Clear();
        Services.Clear();
        GameState.money = 1000e9;
    }

    [TearDown]
    public void TearDown()
    {
        EditModeTestHelpers.DestroyWorldRoot();
        TrackNetwork.Clear();
        Services.Clear();
    }

    static (BuildController bc, Station a, Station b, Train train) Setup(int stationCars)
    {
        var bcGo = new GameObject("BC");
        var bc = bcGo.AddComponent<BuildController>();
        var a = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 0), 0, stationCars, 1, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 2500), 0, stationCars, 1, 2, "B");
        EditModeTestHelpers.Connect(a, b);
        a.RebuildTrackVisual(); b.RebuildTrackVisual();

        var go = new GameObject("Train");
        go.transform.SetParent(BuildController.WorldRoot, false);
        var train = go.AddComponent<Train>();
        train.id = ++TrackNetwork.trainIdCounter;
        TrackNetwork.trains.Add(train);
        train.Init(TrainCatalog.Formations[0], new List<Station> { a, b },
            new List<int> { a.StopTracks[0], b.StopTracks[0] });
        return (bc, a, b, train);
    }

    // 車体中心がホーム端からどれだけ出ているか
    static float Overhang(Train train, Station st)
    {
        float minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (Transform car in train.transform)
        {
            if (!car.name.StartsWith("Car")) continue;
            float z = st.transform.InverseTransformPoint(car.position).z;
            minZ = Mathf.Min(minZ, z); maxZ = Mathf.Max(maxZ, z);
        }
        return Mathf.Max(0f, Mathf.Max(maxZ - st.HalfLen, -minZ - st.HalfLen));
    }

    [Test]
    public void ShrinkingAStationBelowATrainsLength_IsRefused()
    {
        var (bc, a, _, train) = Setup(10);
        Assert.That(train.fm.cars, Is.EqualTo(10), "10両編成であること(テスト前提)");
        Assert.That(Overhang(train, a), Is.EqualTo(0f).Within(0.5f), "建て替え前ははみ出していないこと");

        int carsBefore = a.cars;
        double moneyBefore = GameState.money;

        Assert.That(bc.RebuildStation(a, 2, 1, 2), Is.False,
            "10両が使う駅を2両対応へ縮める建て替えは拒否すること");
        Assert.That(a.cars, Is.EqualTo(carsBefore), "拒否したら両数は変わらないこと");
        Assert.That(GameState.money, Is.EqualTo(moneyBefore).Within(1.0),
            "拒否したら費用も動かないこと");
        Assert.That(Overhang(train, a), Is.EqualTo(0f).Within(0.5f),
            "列車がホームからはみ出さないこと");
    }

    [Test]
    public void ShrinkingToExactlyTheTrainLength_IsAllowed()
    {
        var (bc, a, _, train) = Setup(10);
        Assert.That(bc.RebuildStation(a, train.fm.cars, 1, 2), Is.True,
            "編成長ちょうどまでは縮められること");
        Assert.That(a.cars, Is.EqualTo(train.fm.cars));
    }

    [Test]
    public void ShrinkingAStationNoTrainUses_IsAllowed()
    {
        var (bc, _, _, _) = Setup(10);
        // 経路に含まれない別の駅は自由に縮められる
        var lone = EditModeTestHelpers.MakeStation(new Vector3(3000, 0, 0), 0, 10, 1, 2, "孤立");
        Assert.That(bc.RebuildStation(lone, 2, 1, 2), Is.True);
        Assert.That(lone.cars, Is.EqualTo(2));
    }

    [Test]
    public void EnlargingAStation_IsAlwaysAllowed()
    {
        var (bc, a, _, _) = Setup(10);
        Assert.That(bc.RebuildStation(a, 10, 2, 4), Is.True, "拡張は妨げないこと");
        Assert.That(a.lines, Is.EqualTo(4));
    }
}
