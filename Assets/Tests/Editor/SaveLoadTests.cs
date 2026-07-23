using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// SaveLoadの基本的なシリアライズ往復(セーブ→全消去→ロード)のEditModeテスト。
// PlayerPrefsの"railtycoon_save"キーは実ゲームの本セーブと共有のため、
// テスト前後で退避・復元し、実際のプレイヤーのセーブデータを壊さないようにする。
public class SaveLoadTests
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

        PlayerPrefs.DeleteKey(Key);
        if (hadRealSave) PlayerPrefs.SetString(Key, realSaveBackup);
        PlayerPrefs.Save();
    }

    [Test]
    public void SaveThenLoad_RestoresStationsSegmentsTrainsAndMoney()
    {
        var a = EditModeTestHelpers.MakeStation(new Vector3(-500, 0, 0), 10, 10, 2, 4, "駅1");
        var b = EditModeTestHelpers.MakeStation(new Vector3(600, 0, 200), -30, 6, 1, 2, "駅2");
        TrackNetwork.nameCounter = 2;
        var seg = EditModeTestHelpers.Connect(a, b);
        a.ForceDev(2.5f);
        a.waiting[b] = 42;
        GameState.money = 55.5e8;
        GameState.carried = 1234;

        Assert.That(a.TryReserve(out int track), Is.True);
        var trGo = new GameObject("Train");
        trGo.transform.SetParent(BuildController.WorldRoot, false);
        var tr = trGo.AddComponent<Train>();
        tr.id = ++TrackNetwork.trainIdCounter; // M2-C: id=0の列車はSaveLoad.Saveから除外されるため必須
        TrackNetwork.trains.Add(tr); // SaveLoad.SaveはTrackNetwork.trainsを列挙するため明示登録が必要
        tr.Init(TrainCatalog.Formations[0], new List<Station> { a, b },
            new List<int> { track, b.StopTracks[0] });

        SaveLoad.Save();

        // 全消去
        Object.DestroyImmediate(BuildController.WorldRoot.gameObject);
        TrackNetwork.Clear();
        GameState.money = 0;
        GameState.carried = 0;

        bool loaded = SaveLoad.Load();
        var trains = Object.FindObjectsByType<Train>(FindObjectsSortMode.None);

        Assert.That(loaded, Is.True);
        Assert.That(TrackNetwork.stations.Count, Is.EqualTo(2));
        Assert.That(TrackNetwork.segments.Count, Is.EqualTo(1));
        Assert.That(trains.Length, Is.EqualTo(1));
        Assert.That(GameState.money, Is.EqualTo(55.5e8).Within(1));
        Assert.That(GameState.carried, Is.EqualTo(1234));

        var s0 = TrackNetwork.stations[0];
        Assert.That(s0.stationName, Is.EqualTo("駅1"));
        Assert.That(s0.cars, Is.EqualTo(10));
        Assert.That(s0.faces, Is.EqualTo(2));
        Assert.That(s0.lines, Is.EqualTo(4));
        Assert.That(s0.dev, Is.EqualTo(2.5f).Within(0.01f));
        Assert.That(s0.TotalWaiting, Is.EqualTo(42));
    }

    [Test]
    public void Load_WithNoSaveData_ReturnsFalseAndLeavesStateUntouched()
    {
        // SetUpでKeyを消しているので、この時点でセーブは存在しない
        GameState.money = 12.3e8;

        bool loaded = SaveLoad.Load();

        Assert.That(loaded, Is.False);
        Assert.That(GameState.money, Is.EqualTo(12.3e8), "ロード失敗時に既存の状態を書き換えないこと");
    }

    [Test]
    public void Save_WhenSuppressed_DoesNotWriteToPlayerPrefs()
    {
        EditModeTestHelpers.MakeStation(new Vector3(0, 0, 0), 0, 6, 2, 2, "駅1");
        SaveLoad.suppress = true;

        SaveLoad.Save();

        Assert.That(PlayerPrefs.HasKey(Key), Is.False, "suppress=true中はSaveが書き込みを行わないこと");
    }
}
