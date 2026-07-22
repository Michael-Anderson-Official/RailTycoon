using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// Station.Tick(乗客発生)の決定性テスト。GameRandomのseedと、
// TrackNetwork.stations登録順に基づく安定した列挙により、
// 同じ初期状態・同じ手順なら同じ乗客発生結果になることを検証する。
public class PassengerGenerationTests
{
    [SetUp]
    public void SetUp()
    {
        TrackNetwork.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        EditModeTestHelpers.DestroyWorldRoot();
        TrackNetwork.Clear();
        GameRandom.Seed(0x9E3779B9u);
    }

    // 3駅(中心+到達可能2駅)を作り、中心駅で複数tick分の乗客を発生させて
    // 待ち客の内訳(dictionary)を返す
    static Dictionary<string, int> RunScenario(uint seed)
    {
        var center = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 0), 0, 6, 2, 2, "中心");
        var a = EditModeTestHelpers.MakeStation(new Vector3(-900, 0, 0), 0, 6, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(900, 0, 300), 0, 6, 2, 2, "B");
        EditModeTestHelpers.Connect(center, a);
        EditModeTestHelpers.Connect(center, b);

        GameRandom.Seed(seed);
        for (int i = 0; i < 50; i++) center.Tick(5f); // 5分×50回 = 250分ぶんの乗客発生を積算

        var result = new Dictionary<string, int>();
        foreach (var kv in center.waiting) result[kv.Key.stationName] = kv.Value;
        return result;
    }

    [Test]
    public void SameSeed_SameSetup_ProducesIdenticalPassengerCounts()
    {
        var first = RunScenario(2026u);

        TrackNetwork.Clear();
        EditModeTestHelpers.DestroyWorldRoot();

        var second = RunScenario(2026u);

        Assert.That(second.Keys, Is.EquivalentTo(first.Keys), "同じseedなら行き先の集合が一致すること");
        foreach (var key in first.Keys)
            Assert.That(second[key], Is.EqualTo(first[key]), $"駅[{key}]行きの人数が一致すること");
    }

    [Test]
    public void DifferentSeed_MayProduceDifferentDistribution()
    {
        var a = RunScenario(1u);
        TrackNetwork.Clear();
        EditModeTestHelpers.DestroyWorldRoot();
        var b = RunScenario(2u);

        bool sameTotals = a.OrderBy(x => x.Key).SequenceEqual(b.OrderBy(x => x.Key));
        Assert.That(sameTotals, Is.False, "異なるseedなら(高確率で)行き先分布が変わること");
    }

    [Test]
    public void PassengerGeneration_UsesGameRandom_NotUnityEngineRandom()
    {
        // UnityEngine.Randomの状態を大きく変えても、GameRandomをseedし直せば
        // 結果が変わらないことで、依存していないことを確認する
        Random.InitState(1);
        Random.Range(0, 100000);
        var withUnityRandomA = RunScenario(555u);

        TrackNetwork.Clear();
        EditModeTestHelpers.DestroyWorldRoot();

        Random.InitState(999999);
        for (int i = 0; i < 1000; i++) Random.value.GetHashCode();
        var withUnityRandomB = RunScenario(555u);

        Assert.That(withUnityRandomB.Keys, Is.EquivalentTo(withUnityRandomA.Keys));
        foreach (var key in withUnityRandomA.Keys)
            Assert.That(withUnityRandomB[key], Is.EqualTo(withUnityRandomA[key]),
                "UnityEngine.Randomの状態を変えても結果が変わらないこと(GameRandomのみに依存)");
    }
}
