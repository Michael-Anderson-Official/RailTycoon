using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// 線路・ホーム・車両を同じ実寸基準で組めていることを検証する。
// 見た目の退行を「なんとなく」ではなく、接触と隙間の数値で止める。
public class RailGeometryTests
{
    readonly List<GameObject> roots = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (var root in roots)
            if (root != null) Object.DestroyImmediate(root);
        roots.Clear();
        EditModeTestHelpers.DestroyWorldRoot();
        TrackNetwork.Clear();
    }

    [Test]
    public void KeioGauge_Is1372Millimetres()
    {
        Assert.That(RailKit.Gauge * 2f, Is.EqualTo(1.372f).Within(0.0001f));
    }

    [TestCase(1, 1)]
    [TestCase(1, 2)]
    [TestCase(2, 2)]
    [TestCase(2, 4)]
    [TestCase(3, 2)]
    [TestCase(4, 8)]
    public void PlatformFaces_KeepEightyMillimetreGapFromVehicle(int faces, int lines)
    {
        var layout = StationLayout.Compute(faces, lines);
        foreach (var edge in layout.edges)
        {
            var platform = layout.platforms[edge.platformIndex];
            float faceX = platform.x - edge.side * platform.y * 0.5f;
            float centerToFace = Mathf.Abs(layout.trackOffsets[edge.trackIndex] - faceX);
            float vehicleGap = centerToFace - RailDimensions.CarBodyHalfWidth;
            Assert.That(centerToFace,
                Is.EqualTo(RailDimensions.TrackCenterToPlatformFace).Within(0.001f));
            Assert.That(vehicleGap,
                Is.EqualTo(RailDimensions.PlatformHorizontalGap).Within(0.001f));
            Assert.That(centerToFace - RailDimensions.StationBedHalfWidth,
                Is.GreaterThan(0.10f), "駅構内の道床肩がホームへ食い込まないこと");
        }
    }

    [Test]
    public void PlatformAndVehicleFloor_AreNearlyLevel()
    {
        float vehicleFloor = TrainVisual.BogieRootY + TrainVisual.FloorLocalY;
        Assert.That(vehicleFloor,
            Is.EqualTo(RailKit.RailTop + RailDimensions.VehicleFloorAboveRail).Within(0.001f));
        Assert.That(vehicleFloor - RailDimensions.PlatformTop,
            Is.EqualTo(0.01f).Within(0.001f), "車両床をホームより10mmだけ高くする");
    }

    [Test]
    public void StationSurfaceMesh_ReachesSharedPlatformTop()
    {
        var station = EditModeTestHelpers.MakeStation(
            Vector3.zero, 0f, 6, 2, 2, "寸法確認");
        var surface = station.transform.Find("PlatformSurface")
            .GetComponent<MeshFilter>().sharedMesh;
        Assert.That(surface.bounds.max.y,
            Is.EqualTo(RailDimensions.PlatformTop).Within(0.001f));
    }

    [Test]
    public void PlacedTrain_WheelsSitOnRailAndBogiesRemainOnSampledPath()
    {
        var root = new GameObject("TrainGeometryRoot");
        roots.Add(root);
        var cars = TrainVisual.BuildCars(root.transform, TrainCatalog.Formations[0]);
        var path = new List<Vector3>
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(0f, 0f, 120f),
        };
        var cumulative = RailKit.Cumulative(path);
        Train.PlaceCarsStatic(cars, path, cumulative, 110f);

        var first = cars[0];
        Assert.That(first.body.position.y,
            Is.EqualTo(TrainVisual.BogieRootY).Within(0.001f));
        Assert.That(first.bogieF.position.y,
            Is.EqualTo(TrainVisual.BogieRootY).Within(0.001f),
            "bodyを動かした後も前台車が二重移動しないこと");
        Assert.That(first.bogieR.position.y,
            Is.EqualTo(TrainVisual.BogieRootY).Within(0.001f),
            "bodyを動かした後も後台車が二重移動しないこと");

        var wheelMesh = first.bogieF.Find("BogieMesh")
            .GetComponent<MeshFilter>().sharedMesh;
        float wheelBottom = first.bogieF.position.y + wheelMesh.bounds.min.y;
        Assert.That(wheelBottom, Is.EqualTo(RailKit.RailTop).Within(0.015f));
    }

    [Test]
    public void CabPose_KeepsEyeHeightRelativeToRaisedVehicleBody()
    {
        var station = EditModeTestHelpers.MakeStation(
            Vector3.zero, 0f, 2, 1, 1, "運転台確認");
        var trainGo = new GameObject("CabPoseTrain");
        trainGo.transform.SetParent(BuildController.WorldRoot, false);
        var train = trainGo.AddComponent<Train>();
        train.Init(TrainCatalog.Formations[TrainCatalog.Formations.Count - 2],
            new List<Station> { station }, new List<int> { 0 });

        train.CabPose(out Vector3 eye, out _);

        Assert.That(eye.y,
            Is.EqualTo(TrainVisual.BogieRootY + TrainVisual.CabEyeLocalY).Within(0.001f),
            "車体rootを持ち上げても運転士目線が前面窓に追従すること");
    }

    [TestCase(390f / 844f)]
    [TestCase(16f / 9f)]
    public void NetworkFrameDistance_KeepsWideBoundsInsidePortraitAndLandscapeFov(float aspect)
    {
        var bounds = new Bounds(Vector3.zero, new Vector3(2400f, 0f, 700f));
        const float fov = 60f;
        float distance = CameraRig.RequiredFrameDistance(
            bounds, 52f, 0f, fov, aspect);
        var rotation = Quaternion.Euler(52f, 0f, 0f);
        Vector3 right = rotation * Vector3.right;
        Vector3 up = rotation * Vector3.up;
        Vector3 forward = rotation * Vector3.forward;
        float tanV = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
        float tanH = tanV * aspect;

        for (int ix = -1; ix <= 1; ix += 2)
            for (int iz = -1; iz <= 1; iz += 2)
            {
                Vector3 rel = Vector3.Scale(bounds.extents, new Vector3(ix, 0f, iz));
                float depth = distance + Vector3.Dot(rel, forward);
                Assert.That(depth, Is.GreaterThan(0f));
                Assert.That(Mathf.Abs(Vector3.Dot(rel, right)) / (depth * tanH),
                    Is.LessThanOrEqualTo(CameraRig.NetworkFrameFill + 0.0001f));
                Assert.That(Mathf.Abs(Vector3.Dot(rel, up)) / (depth * tanV),
                    Is.LessThanOrEqualTo(CameraRig.NetworkFrameFill + 0.0001f));
            }
    }
}
