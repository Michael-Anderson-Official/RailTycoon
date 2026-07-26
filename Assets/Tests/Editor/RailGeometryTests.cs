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

    // ---- 走行経路が実際に敷かれたレールの上を通ること ----
    // 保証は2段に分けて確かめる。
    //  (A) 描画されたレールメッシュが、駅が保持する中心線どおりに敷かれている
    //  (B) 走行経路がその中心線の上を通る
    // (A)+(B)で「走行経路は描画されたレールの上」が言える。
    // (B)だけだと、描画側が中心線と無関係な位置へ敷かれる退行を検出できない
    // (Codexレビューでの指摘)。
    // なお(A)を「経路点から最寄りのレール頂点までの距離」で測ってはいけない。
    // レール頂点はホーム区間で数十m間隔しかなく、頂点間の中点で最大25m程度になる。
    // 中心線の各点に対してはレール頂点が必ず生成されるので、そちらを起点に測る

    static float DistanceToPolyline(Vector3 p, List<Vector3> line)
    {
        float best = float.MaxValue;
        for (int i = 0; i + 1 < line.Count; i++)
        {
            Vector3 s0 = line[i], e = line[i + 1], d = e - s0;
            float len2 = d.sqrMagnitude;
            float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(p - s0, d) / len2);
            best = Mathf.Min(best, Vector3.Distance(p, s0 + d * t));
        }
        return best;
    }

    static List<Vector3> MeshPointsOf(Transform t)
    {
        var pts = new List<Vector3>();
        if (t == null) return pts;
        var mf = t.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return pts;
        foreach (var v in mf.sharedMesh.vertices) pts.Add(t.TransformPoint(v));
        return pts;
    }

    [TestCase(2, 2)]
    [TestCase(2, 4)]
    [TestCase(4, 8)]
    public void DrawnRailMesh_FollowsTheStoredCentreLine(int faces, int lines)
    {
        var a = EditModeTestHelpers.MakeStation(Vector3.zero, 0, 10, faces, lines, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 1400), 0, 10, faces, lines, "B");
        EditModeTestHelpers.Connect(a, b);
        a.RebuildTrackVisual();

        var railPts = MeshPointsOf(a.transform.Find("TrackWork/Rail"));
        Assert.That(railPts.Count, Is.GreaterThan(0), "レールメッシュが生成されていること");

        // 中心線の各点にはレール頂点が軌間の半分だけ離れて生成される。
        // 描画が中心線から外れたらこの距離が崩れる
        // ホーム区間だけを見る。スロートには渡り線が同じメッシュへ描かれており、
        // 斜めの分岐が他の線の中心線近くを通るため「最寄り頂点=軌間の半分」が成り立たない
        float platHalf = a.cars * StationLayout.CarLength * 0.5f;
        // レールは幅を持つスラブなので、頂点は中心から軌間±(レール半幅)に出る。
        // 底部フランジの半幅0.075を含めた許容を見込む
        const float railHalfWidth = 0.075f;
        float worst = 0f;
        int checkedPts = 0;
        for (int i = 0; i < a.layout.trackOffsets.Length; i++)
        {
            var centre = a.TrackCentreLocal(i);
            Assert.That(centre, Is.Not.Null);
            foreach (var lp in centre)
            {
                if (Mathf.Abs(lp.z) > platHalf) continue;
                checkedPts++;
                var w = a.transform.TransformPoint(lp);
                float best = float.MaxValue;
                foreach (var r in railPts)
                {
                    float dx = w.x - r.x, dz = w.z - r.z;
                    best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
                }
                worst = Mathf.Max(worst, Mathf.Abs(best - RailKit.Gauge));
            }
        }
        Assert.That(checkedPts, Is.GreaterThan(0), "ホーム区間の中心線点があること");
        Assert.That(worst, Is.LessThanOrEqualTo(railHalfWidth + 0.01f),
            "レールが中心線から軌間の半分だけ離れて敷かれていること(ズレ" + worst.ToString("F3") + "m)");
    }

    [TestCase(2, 2)]
    [TestCase(2, 4)]
    [TestCase(4, 8)]
    public void TrainLegRunsOnTheStoredCentreLine(int faces, int lines)
    {
        var a = EditModeTestHelpers.MakeStation(Vector3.zero, 0, 10, faces, lines, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 1400), 0, 10, faces, lines, "B");
        var seg = EditModeTestHelpers.Connect(a, b);
        a.RebuildTrackVisual(); b.RebuildTrackVisual();

        // 中心線(駅構内の各線 + 駅間の左右)と、渡り線のS字を許容経路とする
        var rails = new List<List<Vector3>>();
        foreach (var st in new[] { a, b })
            for (int i = 0; i < st.layout.trackOffsets.Length; i++)
            {
                var loc = st.TrackCentreLocal(i);
                if (loc == null) continue;
                var w = new List<Vector3>();
                foreach (var p in loc) w.Add(st.transform.TransformPoint(p));
                rails.Add(w);
            }
        rails.Add(seg.SideCentre(TrackSegment.TrackOffset));
        rails.Add(seg.SideCentre(-TrackSegment.TrackOffset));
        foreach (var st in new[] { a, b })
            foreach (int sign in new[] { -1, 1 })
            {
                float cz = sign * (st.HalfLen + StationLayout.ThroatLen - StationLayout.LeadLen * 0.5f);
                float d = RailKit.CrossoverHalfLength;
                float off = RailDimensions.MainTrackOffset;
                rails.Add(RailKit.CrossoverPath(
                    st.transform.TransformPoint(new Vector3(off, 0, cz - sign * d)),
                    st.transform.TransformPoint(new Vector3(-off, 0, cz + sign * d)),
                    st.Axis * sign));
                rails.Add(RailKit.CrossoverPath(
                    st.transform.TransformPoint(new Vector3(-off, 0, cz - sign * d)),
                    st.transform.TransformPoint(new Vector3(off, 0, cz + sign * d)),
                    st.Axis * sign));
            }

        // 出発側・到着側それぞれで、渡り線が要る番線と要らない番線の全組み合わせを見る
        // (到着側の渡り線を通る組み合わせを外すと、到着経路の点順が壊れる不具合を見逃す)
        foreach (int ta in new[] { a.layout.stopTracks[0], a.layout.stopTracks[a.layout.stopTracks.Count - 1] })
            foreach (int tb in new[] { b.layout.stopTracks[0], b.layout.stopTracks[b.layout.stopTracks.Count - 1] })
            {
                var leg = Train.BuildLeg(a, ta, seg.SignAt(a), b, tb, seg.SignAt(b), 100f);
                string what = "発" + ta + "→着" + tb + ": ";

                // ホーム部とスロート(渡り線)部で許容を分ける。
                // ホーム部は厳密に中心線の上でなければならない(ここが狂うと列車が
                // ホームへ乗り上げる。2026-07-26に実機で発生した)。
                // スロート部は、描かれた渡り線が公称値±2.3から引かれているのに対し、
                // 平滑化(Chaikin)後の番線中心線はその位置でまだ±2.3へ収束しきって
                // いない(2面4線の外側でz=129のときx=-3.04)。この描画上の食い違いは
                // 以前からあり、経路が±2.3へ潰されていたため隠れていた。別途直す
                float worstPlatform = 0f, worstThroat = 0f;
                foreach (var p in leg)
                {
                    float best = float.MaxValue;
                    foreach (var r in rails) best = Mathf.Min(best, DistanceToPolyline(p, r));
                    bool inPlatform =
                        Mathf.Abs(a.transform.InverseTransformPoint(p).z) <= a.HalfLen ||
                        Mathf.Abs(b.transform.InverseTransformPoint(p).z) <= b.HalfLen;
                    if (inPlatform) worstPlatform = Mathf.Max(worstPlatform, best);
                    else worstThroat = Mathf.Max(worstThroat, best);
                }
                Assert.That(worstPlatform, Is.LessThan(0.05f),
                    what + "ホーム部で走行経路が中心線から外れないこと(最大"
                    + worstPlatform.ToString("F3") + "m)");
                Assert.That(worstThroat, Is.LessThan(0.40f),
                    what + "スロート部の食い違いが既知の範囲に収まること(最大"
                    + worstThroat.ToString("F3") + "m)");

                // 点順が壊れると経路が飛ぶ。連続していることを区間長で見る
                float longest = 0f;
                for (int i = 0; i + 1 < leg.Count; i++)
                    longest = Mathf.Max(longest, Vector3.Distance(leg[i], leg[i + 1]));
                Assert.That(longest, Is.LessThan(5f),
                    what + "経路が途中で飛ばないこと(最大区間長" + longest.ToString("F1") + "m)");
            }
    }

    // ---- 渡り線の分岐形状 ----
    // 直線の対角線で引くと、直進本線との付け根で線路が10°折れる。
    // 実物のように両端が本線へ接するS字で開くこと

    [Test]
    public void CrossoverPath_LeavesAndJoinsMainTrackSmoothly()
    {
        Vector3 dir = Vector3.forward;
        const float half = RailDimensions.MainTrackOffset, d = 13f;
        var perp = Vector3.Cross(Vector3.up, dir).normalized;
        var from = -perp * half - dir * d;
        var to = perp * half + dir * d;

        var curve = RailKit.CrossoverPath(from, to, dir);
        Assert.That(curve.Count, Is.GreaterThan(4));

        // 端点は本線上の分岐点と厳密に一致すること(ずれるとレールが途切れる)
        Assert.That(Vector3.Distance(curve[0], from), Is.LessThan(0.01f));
        Assert.That(Vector3.Distance(curve[curve.Count - 1], to), Is.LessThan(0.01f));

        // 付け根で本線とほぼ同じ向きに出入りすること(直線対角線なら約10°折れる)
        float angStart = Vector3.Angle((curve[1] - curve[0]).normalized, dir);
        float angEnd = Vector3.Angle((curve[curve.Count - 1] - curve[curve.Count - 2]).normalized, dir);
        float straightAngle = Vector3.Angle((to - from).normalized, dir);
        Assert.That(angStart, Is.LessThan(3f), "分岐の入口が本線に接すること");
        Assert.That(angEnd, Is.LessThan(3f), "分岐の出口が本線に接すること");
        Assert.That(angStart, Is.LessThan(straightAngle * 0.5f),
            "直線の対角線より明確に滑らかであること");

        // 途中はちゃんと開いていること(まっすぐなだけでは渡り線にならない)
        float maxAngle = 0f;
        for (int i = 0; i + 1 < curve.Count; i++)
            maxAngle = Mathf.Max(maxAngle, Vector3.Angle((curve[i + 1] - curve[i]).normalized, dir));
        Assert.That(maxAngle, Is.GreaterThan(3f));
        Assert.That(maxAngle, Is.LessThan(20f), "分岐角が急すぎないこと");
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
