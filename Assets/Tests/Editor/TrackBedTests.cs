using NUnit.Framework;
using UnityEngine;

// バラスト／スラブ軌道の生成差とセーブ互換を検証する。
// PlayerPrefsは実ゲームと共有するため、テスト前後で必ず退避・復元する。
public class TrackBedTests
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
        GameRandom.Seed(2468u);
    }

    [TearDown]
    public void TearDown()
    {
        EditModeTestHelpers.DestroyWorldRoot();
        TrackNetwork.Clear();
        Services.Clear();
        SaveLoad.suppress = true;

        PlayerPrefs.DeleteKey(Key);
        if (hadRealSave) PlayerPrefs.SetString(Key, realSaveBackup);
        PlayerPrefs.Save();
    }

    static TrackSegment MakeSegment(TrackBedType type)
    {
        var a = EditModeTestHelpers.MakeStation(new Vector3(-700, 0, 0), 90, 6, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(700, 0, 0), 90, 6, 2, 2, "B");
        return EditModeTestHelpers.Connect(a, b, type);
    }

    static void AssertMeshExists(Transform parent, string name)
    {
        var child = parent.Find(name);
        Assert.That(child, Is.Not.Null, name + "メッシュが存在すること");
        var filter = child.GetComponent<MeshFilter>();
        Assert.That(filter, Is.Not.Null);
        Assert.That(filter.sharedMesh.vertexCount, Is.GreaterThan(0));
    }

    [Test]
    public void Build_Ballast_CreatesBallastShoulderAndSleepers()
    {
        var seg = MakeSegment(TrackBedType.Ballast);

        AssertMeshExists(seg.go.transform, "Ballast");
        AssertMeshExists(seg.go.transform, "Tie");
        AssertMeshExists(seg.go.transform, "Rail");
        Assert.That(seg.go.transform.Find("Slab"), Is.Null);
        Assert.That(seg.go.transform.Find("SlabDetail"), Is.Null);
    }

    [Test]
    public void Build_Slab_CreatesConcreteBedSupportsAndFasteners()
    {
        var seg = MakeSegment(TrackBedType.Slab);

        AssertMeshExists(seg.go.transform, "Slab");
        AssertMeshExists(seg.go.transform, "SlabSupport");
        AssertMeshExists(seg.go.transform, "SlabDetail");
        AssertMeshExists(seg.go.transform, "Rail");
        Assert.That(seg.go.transform.Find("Ballast"), Is.Null);
        Assert.That(seg.go.transform.Find("Tie"), Is.Null);
    }

    [Test]
    public void SaveThenLoad_RestoresSlabTypeAndVisual()
    {
        MakeSegment(TrackBedType.Slab);
        SaveLoad.Save();

        Object.DestroyImmediate(BuildController.WorldRoot.gameObject);
        TrackNetwork.Clear();
        Assert.That(SaveLoad.Load(), Is.True);

        Assert.That(TrackNetwork.segments.Count, Is.EqualTo(1));
        var loaded = TrackNetwork.segments[0];
        Assert.That(loaded.bedType, Is.EqualTo(TrackBedType.Slab));
        AssertMeshExists(loaded.go.transform, "Slab");
        Assert.That(loaded.go.transform.Find("Ballast"), Is.Null);
    }

    [Test]
    public void Load_LegacyV4WithoutBedType_DefaultsToBallast()
    {
        MakeSegment(TrackBedType.Ballast);
        SaveLoad.Save();
        string json = PlayerPrefs.GetString(Key);
        string legacy = json.Replace(",\"bedType\":0", "");
        Assert.That(legacy, Is.Not.EqualTo(json), "テスト前提: bedTypeフィールドを除去できること");
        PlayerPrefs.SetString(Key, legacy);

        Object.DestroyImmediate(BuildController.WorldRoot.gameObject);
        TrackNetwork.Clear();
        Assert.That(SaveLoad.Load(), Is.True);

        Assert.That(TrackNetwork.segments.Count, Is.EqualTo(1));
        Assert.That(TrackNetwork.segments[0].bedType, Is.EqualTo(TrackBedType.Ballast));
        AssertMeshExists(TrackNetwork.segments[0].go.transform, "Ballast");
    }
}
