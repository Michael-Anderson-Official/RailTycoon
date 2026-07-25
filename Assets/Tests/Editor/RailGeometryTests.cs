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

    // ---- 駅間の線路が途中の駅のホームを貫かないこと ----
    // TrackSegmentは両端の駅しか見ないため、間に別の駅がある区間を直結すると
    // 道床がその駅のホームを踏み抜いて描画される(実機で確認された不具合)。
    // 建設時に弾けるよう、判定側を数値で固定する

    static Station Line(float z, float yaw, string name)
        => EditModeTestHelpers.MakeStation(new Vector3(0, 0, z), yaw, 10, 2, 2, name);

    static Station CrossedBy(Station a, Station b)
    {
        int bestSa = 1, bestSb = 1;
        float best = float.MaxValue;
        for (int sa = -1; sa <= 1; sa += 2)
            for (int sb = -1; sb <= 1; sb += 2)
            {
                float d = Vector3.Distance(a.End(sa), b.End(sb));
                if (d < best) { best = d; bestSa = sa; bestSb = sb; }
            }
        return new TrackSegment { a = a, b = b, signA = bestSa, signB = bestSb }.FindStationCrossedByBed();
    }

    [Test]
    public void SegmentCrossingAnotherStation_IsDetected()
    {
        var a = Line(-1500, 0, "A");
        var b = Line(0, 0, "B");
        var c = Line(1500, 0, "C");

        Assert.That(CrossedBy(a, c), Is.EqualTo(b),
            "間にある駅Bを貫く区間は、その駅を返して建設を拒否できること");
    }

    [Test]
    public void SegmentBetweenAdjacentStations_IsAllowed()
    {
        var a = Line(-1500, 0, "A");
        var b = Line(0, 0, "B");
        var c = Line(1500, 0, "C");

        Assert.That(CrossedBy(a, b), Is.Null, "隣接駅同士の正常な区間を誤検出しないこと");
        Assert.That(CrossedBy(b, c), Is.Null);
    }

    [Test]
    public void SegmentPassingBesideAStation_IsAllowed()
    {
        var a = Line(-1500, 0, "A");
        // 線から十分横へ外れた駅は、通り沿いにあっても貫通ではない
        EditModeTestHelpers.MakeStation(new Vector3(260, 0, 0), 0, 10, 2, 2, "Beside");
        var c = Line(1500, 0, "C");

        Assert.That(CrossedBy(a, c), Is.Null, "脇に逸れた駅を誤検出しないこと");
    }

    [Test]
    public void SegmentBetweenMisalignedStations_IsAllowed()
    {
        var a = EditModeTestHelpers.MakeStation(new Vector3(0, 0, -900), 25, 10, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(400, 0, 900), -15, 10, 2, 2, "B");

        Assert.That(CrossedBy(a, b), Is.Null, "駅の向きが接続方向とズレていても誤検出しないこと");
    }

    // ---- 駅構内の道床がホームへ食い込まないこと(実メッシュでの検証) ----
    // 数値(StationLayout)だけの検証では取りこぼす。実際に生成されたメッシュの頂点で見る。
    // **端を線路に接続していること**が必須: 未接続だとスロートの収束自体が起きず、
    // この不具合は再現しない(以前の検証がこれを取りこぼしていた)

    [TestCase(2, 2)]
    [TestCase(2, 3)]
    [TestCase(2, 4)]
    [TestCase(3, 2)]
    [TestCase(1, 2)]
    [TestCase(4, 8)]
    public void ConnectedStation_TrackBedDoesNotIntrudeIntoPlatform(int faces, int lines)
    {
        var a = EditModeTestHelpers.MakeStation(Vector3.zero, 0, 10, faces, lines, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 900), 0, 10, faces, lines, "B");
        EditModeTestHelpers.Connect(a, b);
        a.RebuildTrackVisual();

        float platLen = a.cars * StationLayout.CarLength;
        var tw = a.transform.Find("TrackWork");
        Assert.That(tw, Is.Not.Null);

        float worst = 0f;
        foreach (var name in new[] { "Ballast", "Tie", "Rail" })
        {
            var t = tw.Find(name);
            if (t == null) continue;
            var mf = t.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            foreach (var v in mf.sharedMesh.vertices)
            {
                var lp = a.transform.InverseTransformPoint(t.TransformPoint(v));
                if (Mathf.Abs(lp.z) > platLen * 0.5f) continue;   // ホーム本体の範囲だけ見る
                foreach (var p in a.layout.platforms)
                {
                    float visualW = Mathf.Max(2.6f, p.y - 0.02f);
                    worst = Mathf.Max(worst, visualW * 0.5f - Mathf.Abs(lp.x - p.x));
                }
            }
        }
        Assert.That(worst, Is.LessThanOrEqualTo(0.005f),
            "駅構内の道床・枕木・レールがホーム躯体へ食い込まないこと(食い込み" + worst.ToString("F2") + "m)");
    }

    [Test]
    public void TrainLegStaysOnTrackCentreThroughPlatform()
    {
        // レールを真っ直ぐに保っても、走行経路が同じように保たれていないと
        // 列車だけ内側へ寄ってレールから外れて見える
        var a = EditModeTestHelpers.MakeStation(Vector3.zero, 0, 10, 2, 4, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 900), 0, 10, 2, 4, "B");
        var seg = EditModeTestHelpers.Connect(a, b);

        int track = a.layout.stopTracks[0];
        float off = a.layout.trackOffsets[track];
        var leg = Train.BuildLeg(a, track, seg.SignAt(a), b, b.layout.stopTracks[0], seg.SignAt(b), 100f);

        float maxDev = 0f;
        foreach (var p in leg)
        {
            var lp = a.transform.InverseTransformPoint(p);
            if (Mathf.Abs(lp.z) > a.cars * StationLayout.CarLength * 0.5f) continue;
            maxDev = Mathf.Max(maxDev, Mathf.Abs(lp.x - off));
        }
        Assert.That(maxDev, Is.LessThanOrEqualTo(0.02f),
            "ホーム区間では走行経路が線路中心から外れないこと(ズレ" + maxDev.ToString("F2") + "m)");
    }

    // ---- 駅を建てる位置の当たり判定 ----
    // 既存の駅や既設の線路へ重ねて建てられてしまうと、ホームや道床がめり込んで描画される

    // TrackNetworkへ登録しない、建設プレビュー相当の駅を作る
    Station MakeCandidate(Vector3 pos, float yaw, int cars = 10, int faces = 2, int lines = 2)
    {
        var go = new GameObject("Candidate");
        go.transform.SetParent(BuildController.WorldRoot, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        var st = go.AddComponent<Station>();
        st.preview = true;
        st.cars = cars; st.faces = faces; st.lines = lines; st.stationName = "(予定)";
        st.Build();
        roots.Add(go);
        return st;
    }

    [Test]
    public void StationPlacement_OverlappingAnotherStation_IsRejected()
    {
        EditModeTestHelpers.MakeStation(Vector3.zero, 0, 10, 2, 2, "A");
        var candidate = MakeCandidate(new Vector3(12, 0, 30), 0);

        Assert.That(BuildController.DescribePlacementObstruction(candidate, null),
            Is.Not.Null, "既存駅と重なる位置には建てられないこと");
    }

    [Test]
    public void StationPlacement_ClearOfEverything_IsAllowed()
    {
        EditModeTestHelpers.MakeStation(Vector3.zero, 0, 10, 2, 2, "A");
        var candidate = MakeCandidate(new Vector3(0, 0, 450), 0);

        Assert.That(BuildController.DescribePlacementObstruction(candidate, null),
            Is.Null, "十分離れた位置は建てられること");
    }

    [Test]
    public void StationPlacement_OnExistingTrack_IsRejected()
    {
        var a = EditModeTestHelpers.MakeStation(Vector3.zero, 0, 10, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 900), 0, 10, 2, 2, "B");
        EditModeTestHelpers.Connect(a, b);
        var candidate = MakeCandidate(new Vector3(0, 0, 450), 0);

        Assert.That(BuildController.DescribePlacementObstruction(candidate, null),
            Is.Not.Null, "既設線路の上には建てられないこと");
    }

    // ---- 線路単体の撤去 ----
    // 誤って敷いた線路(途中の駅を貫通してしまったもの等)を消せるようにするため、
    // 線路モードで繋がっている2駅を選び直すと撤去できる

    [Test]
    public void RemoveSegment_DeletesTrackAndRebuildsStationEnds()
    {
        var a = EditModeTestHelpers.MakeStation(Vector3.zero, 0, 10, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 900), 0, 10, 2, 2, "B");
        var seg = EditModeTestHelpers.Connect(a, b);
        var go = new GameObject("BC");
        roots.Add(go);
        var bc = go.AddComponent<BuildController>();

        Assert.That(TrackNetwork.Connected(a, b), Is.True);
        bc.RemoveSegment(seg);

        Assert.That(TrackNetwork.segments, Has.No.Member(seg), "線路が台帳から消えること");
        Assert.That(TrackNetwork.Connected(a, b), Is.False, "撤去後は接続が切れていること");
    }

    [Test]
    public void RemoveSegment_LeavesUnrelatedRunningTrainsUndisturbed()
    {
        // 別々の連結成分にする(A-Bを撤去してもC-Dの列車には何の関係も無い)
        var a = EditModeTestHelpers.MakeStation(Vector3.zero, 0, 10, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 900), 0, 10, 2, 2, "B");
        var seg = EditModeTestHelpers.Connect(a, b);
        var c = EditModeTestHelpers.MakeStation(new Vector3(4000, 0, 0), 0, 10, 2, 2, "C");
        var d = EditModeTestHelpers.MakeStation(new Vector3(4000, 0, 900), 0, 10, 2, 2, "D");
        EditModeTestHelpers.Connect(c, d);

        var fm = TrainCatalog.Formations[0];
        int trackC;
        c.TryReserve(out trackC);
        var tgo = new GameObject("Train");
        tgo.transform.SetParent(BuildController.WorldRoot, false);
        var train = tgo.AddComponent<Train>();
        TrackNetwork.trains.Add(train);
        train.Init(fm, new List<Station> { c, d }, new List<int> { trackC, d.StopTracks[0] });
        float tick = Bootstrap.TickSeconds;
        for (int i = 0; i < 1200 && train.IsDwelling; i++) train.SimTick(tick);
        Assert.That(train.IsDwelling, Is.False, "前提: この列車は走行中であること");
        float sBefore = train.RouteS;

        var go = new GameObject("BC");
        roots.Add(go);
        go.AddComponent<BuildController>().RemoveSegment(seg);

        Assert.That(train.IsDwelling, Is.False, "無関係な列車が停車状態へ戻されないこと");
        Assert.That(train.RouteS, Is.EqualTo(sBefore).Within(0.001f),
            "無関係な列車の走行位置が動かされないこと");
    }

    // 巡回運転の「閉じる区間」を撤去しても、経路が成立している限り列車は
    // 迂回経路(通過駅対応のFindPath)で走り続けられ、行き詰まらないこと
    [Test]
    public void RemoveSegment_ClosingLegOfCyclicRoute_TrainStillDeparts()
    {
        // A-B-C-A の三角形。route=[A,B,C]をcyclicで走らせ、閉じるC-Aを撤去する
        var a = EditModeTestHelpers.MakeStation(new Vector3(0, 0, -900), 0, 10, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 900), 0, 10, 2, 2, "B");
        var c = EditModeTestHelpers.MakeStation(new Vector3(1800, 0, 0), 90, 10, 2, 2, "C");
        EditModeTestHelpers.Connect(a, b);
        EditModeTestHelpers.Connect(b, c);
        var closing = EditModeTestHelpers.Connect(c, a);

        var fm = TrainCatalog.Formations[0];
        int trackA;
        a.TryReserve(out trackA);
        var tgo = new GameObject("Train");
        tgo.transform.SetParent(BuildController.WorldRoot, false);
        var train = tgo.AddComponent<Train>();
        TrackNetwork.trains.Add(train);
        train.Init(fm, new List<Station> { a, b, c },
            new List<int> { trackA, b.StopTracks[0], c.StopTracks[0] });
        Assert.That(train.cyclic, Is.True, "前提: 末尾-先頭が繋がっているので巡回運転になる");

        var go = new GameObject("BC");
        roots.Add(go);
        go.AddComponent<BuildController>().RemoveSegment(closing);

        Assert.That(train, Is.Not.Null, "経路自体は成立しているので列車は撤去されないこと");
        Assert.That(TrackNetwork.Connected(c, a), Is.False, "前提: 閉じる区間の直結は無くなっている");
        Assert.That(TrackNetwork.FindPath(c, a), Is.Not.Null,
            "末尾→先頭はB経由で到達可能なままであること(だから行き詰まらない)");

        // 末尾(C)まで進めてから、迂回経路で発車できることを実際に走らせて確かめる
        float tick = Bootstrap.TickSeconds;
        int departsBefore = train.DepartureCount;
        for (int i = 0; i < 400000 && train.DepartureCount < departsBefore + 3; i++) train.SimTick(tick);
        Assert.That(train.DepartureCount, Is.GreaterThanOrEqualTo(departsBefore + 3),
            "閉じる区間が無くても発車を繰り返せること(どこかで詰まらないこと)");
    }

    [Test]
    public void StationPlacement_BesideExistingTrack_IsAllowed()
    {
        var a = EditModeTestHelpers.MakeStation(Vector3.zero, 0, 10, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 900), 0, 10, 2, 2, "B");
        EditModeTestHelpers.Connect(a, b);
        var candidate = MakeCandidate(new Vector3(220, 0, 450), 0);

        Assert.That(BuildController.DescribePlacementObstruction(candidate, null),
            Is.Null, "線路の脇へ十分離して建てるのは許可されること");
    }
}
