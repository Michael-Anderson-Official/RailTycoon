using NUnit.Framework;
using UnityEngine;

// Station.Buildが生成する実景用の視覚層を検証する。
// 番線・停止位置のテストとは分離し、必要な構成物が再構築後も一意に存在することを見る。
public class StationVisualTests
{
    [SetUp]
    public void SetUp() => TrackNetwork.Clear();

    [TearDown]
    public void TearDown()
    {
        EditModeTestHelpers.DestroyWorldRoot();
        TrackNetwork.Clear();
    }

    static Station MakeStation(int cars = 10, int faces = 2, int lines = 4)
        => EditModeTestHelpers.MakeStation(Vector3.zero, 0, cars, faces, lines, "高幡中央");

    static Mesh AssertMesh(Station station, string name)
    {
        var child = station.transform.Find(name);
        Assert.That(child, Is.Not.Null, name + "が生成されること");
        var filter = child.GetComponent<MeshFilter>();
        Assert.That(filter, Is.Not.Null);
        Assert.That(filter.sharedMesh.vertexCount, Is.GreaterThan(0), name + "に実メッシュがあること");
        return filter.sharedMesh;
    }

    [Test]
    public void Build_CreatesLayeredPlatformsCanopyFurnitureAndStationHouse()
    {
        var station = MakeStation();

        AssertMesh(station, "PlatformBase");
        AssertMesh(station, "PlatformSurface");
        AssertMesh(station, "PlatformEdge");
        AssertMesh(station, "TactilePaving");
        AssertMesh(station, "WarningLine");
        AssertMesh(station, "Drainage");
        AssertMesh(station, "CanopyRoof");
        AssertMesh(station, "Metalwork");
        AssertMesh(station, "Lighting");
        AssertMesh(station, "Furniture");
        AssertMesh(station, "StationSigns");
        AssertMesh(station, "House");
        AssertMesh(station, "Glass");
    }

    [Test]
    public void Build_CreatesDoubleSidedStationNameSignsAndRenameUpdatesThem()
    {
        var station = MakeStation(cars: 6, faces: 2, lines: 2);

        int signTextCount = 0;
        foreach (var tm in station.GetComponentsInChildren<TextMesh>())
        {
            if (!tm.gameObject.name.StartsWith("StationSignText_")) continue;
            signTextCount++;
            Assert.That(tm.text, Is.EqualTo("高幡中央"));
        }
        Assert.That(signTextCount, Is.EqualTo(station.faces * 2),
            "短いホームは各面1基・両面表示の駅名標を持つこと");

        station.stationName = "新高幡";
        station.UpdateLabel();
        foreach (var tm in station.GetComponentsInChildren<TextMesh>())
            if (tm.gameObject.name.StartsWith("StationSignText_"))
                Assert.That(tm.text, Is.EqualTo("新高幡"));
    }

    [Test]
    public void Rebuild_ReplacesVisualLayerWithoutDuplicateRoots()
    {
        var station = MakeStation();
        var firstWarningMaterial = station.transform.Find("WarningLine")
            .GetComponent<MeshRenderer>().sharedMaterial;
        station.cars = 8;
        station.Build();
        var rebuiltWarningMaterial = station.transform.Find("WarningLine")
            .GetComponent<MeshRenderer>().sharedMaterial;

        Assert.That(rebuiltWarningMaterial, Is.SameAs(firstWarningMaterial),
            "駅プレビュー再構築のたびに警戒線Materialを増やさないこと");

        string[] roots =
        {
            "PlatformBase", "PlatformSurface", "PlatformEdge", "TactilePaving", "WarningLine",
            "Drainage", "CanopyRoof", "Metalwork", "Lighting", "Furniture",
            "StationSigns", "House", "Glass",
        };
        foreach (string root in roots)
        {
            int count = 0;
            for (int i = 0; i < station.transform.childCount; i++)
                if (station.transform.GetChild(i).name == root) count++;
            Assert.That(count, Is.EqualTo(1), root + "が再構築後に重複しないこと");
        }
    }

    // ---- ホーム端の絞り ----
    // 実物のホームは終端で線路の収束に合わせて細くなる。ただし列車が停まる範囲
    // (編成長=platLen)を削ってしまうとホームとの隙間が広がるので、そこは全幅を保つ

    [TestCase(2, 2)]
    [TestCase(1, 2)]
    [TestCase(2, 4)]
    public void PlatformEnds_TaperBeyondTrainStoppingRange(int faces, int lines)
    {
        var station = MakeStation(cars: 10, faces: faces, lines: lines);
        var mesh = AssertMesh(station, "PlatformBase");
        float platLen = station.cars * StationLayout.CarLength;
        var p = station.layout.platforms[0];

        float fullHalf = 0f, tipHalf = float.MaxValue, maxAbsZ = 0f;
        foreach (var v in mesh.vertices)
        {
            // 最初のホームだけを見る(左右で対称なので1枚で十分)
            if (Mathf.Abs(v.x - p.x) > p.y) continue;
            float half = Mathf.Abs(v.x - p.x);
            maxAbsZ = Mathf.Max(maxAbsZ, Mathf.Abs(v.z));
            if (Mathf.Abs(v.z) <= platLen * 0.5f + 0.01f) fullHalf = Mathf.Max(fullHalf, half);
            if (Mathf.Abs(v.z) >= station.HalfLen - 0.4f) tipHalf = Mathf.Min(tipHalf, half);
        }

        Assert.That(fullHalf, Is.EqualTo((p.y - 0.02f) * 0.5f).Within(0.05f),
            "列車が停まる範囲(編成長)は全幅を保つこと");
        Assert.That(tipHalf, Is.LessThan(fullHalf - 0.5f), "ホーム端が絞られていること");
        Assert.That(maxAbsZ, Is.GreaterThan(platLen * 0.5f + 0.5f),
            "絞った端部は本体より先(駅端側)へ伸びること");
        Assert.That(maxAbsZ, Is.LessThanOrEqualTo(station.HalfLen + 0.01f),
            "ホームが駅端(スロートの始まり)を越えないこと");
    }
}
