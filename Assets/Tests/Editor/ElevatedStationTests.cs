using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// 高架駅(階による立体化)の検証。
// 高さは連続値ではなく階(Station.level)で持ち、駅のメッシュは全てローカル座標で
// 作られるため、transform.position.yを上げるだけで駅ごと持ち上がる設計になっている。
// ここで止めたい退行は3つ:
//   (A) 駅間の縦断が非対称になり、到着駅で線路が折れる
//   (B) 高低差のある区間で、描いたレールと列車の走行経路がズレる
//   (C) 階が違うのに干渉と判定され、立体交差が作れない/高架下を線路が通れない
public class ElevatedStationTests
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
        GameRandom.Seed(777u);
    }

    [TearDown]
    public void TearDown()
    {
        EditModeTestHelpers.DestroyWorldRoot();
        TrackNetwork.Clear();
        Services.Clear();
        SaveLoad.suppress = true;
        GameState.money = 100e8;
        PlayerPrefs.DeleteKey(Key);
        if (hadRealSave) PlayerPrefs.SetString(Key, realSaveBackup);
        PlayerPrefs.Save();
    }

    // ---- 階の定義 ----

    [Test]
    public void Level_LiftsTheWholeStationByOneFloorHeight()
    {
        var st = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 0), 0, 6, 2, 2, "高架", level: 2);
        Assert.That(st.transform.position.y,
            Is.EqualTo(RailDimensions.FloorHeight * 2f).Within(0.001f),
            "Build()が階に応じた高さを確定させること(位置Yを渡し忘れても落ちない)");
        Assert.That(st.End(1).y, Is.EqualTo(st.transform.position.y).Within(0.001f),
            "スロート端も駅と同じ高さにあること");

        // ホーム面は駅ローカルの高さのまま持ち上がる
        var surface = st.transform.Find("PlatformSurface").GetComponent<MeshFilter>().sharedMesh;
        Assert.That(surface.bounds.max.y + st.transform.position.y,
            Is.EqualTo(RailDimensions.PlatformTop + st.transform.position.y).Within(0.001f));
    }

    [Test]
    public void ElevatedStation_GrowsPiersDownToTheGround()
    {
        var st = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 0), 0, 6, 2, 2, "高架", level: 1);
        var via = st.transform.Find("Viaduct");
        Assert.That(via, Is.Not.Null, "高架駅には桁と橋脚が生成されること");
        var mesh = via.GetComponent<MeshFilter>().sharedMesh;
        float worldBottom = st.transform.position.y + mesh.bounds.min.y;
        Assert.That(worldBottom, Is.EqualTo(0f).Within(0.05f), "橋脚が地面まで届くこと");
        Assert.That(st.transform.position.y + mesh.bounds.max.y,
            Is.LessThanOrEqualTo(st.transform.position.y + 0.001f), "桁がレール基面より上へ出ないこと");
    }

    [Test]
    public void ElevatedStation_ColliderDoesNotReachDownOverALowerStation()
    {
        // 当たり判定を橋脚ぶん下へ伸ばすと、立体交差で真下の地上駅が選べなくなる
        var high = EditModeTestHelpers.MakeStation(Vector3.zero, 0, 6, 2, 2, "高架", level: 1);
        var col = high.GetComponent<BoxCollider>();
        float worldBottom = high.transform.position.y + col.center.y - col.size.y * 0.5f;
        Assert.That(worldBottom, Is.GreaterThan(RailDimensions.LevelClearance),
            "高架駅の当たり判定が地上まで降りてこないこと(下の駅を隠さない)");
    }

    [Test]
    public void GroundStation_HasNoViaduct()
    {
        var st = EditModeTestHelpers.MakeStation(Vector3.zero, 0, 6, 2, 2, "地上");
        Assert.That(st.transform.Find("Viaduct"), Is.Null);
        Assert.That(st.transform.position.y, Is.EqualTo(0f).Within(0.001f));
    }

    // ---- (A) 縦断形状 ----

    [Test]
    public void VerticalProfile_LevelsOffAtBothEndsAndStaysWithinMaxGrade()
    {
        // Δh=16m(3階)を結ぶのに必要な水平距離は 1.5*16/0.035 = 686m。余裕を持って1600m
        var a = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 0), 0, 8, 2, 2, "地上");
        var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 1600), 0, 8, 2, 2, "高架", level: 2);
        var seg = EditModeTestHelpers.Connect(a, b);
        var c = seg.CenterPoints();

        Assert.That(c[0].y, Is.EqualTo(a.End(seg.SignAt(a)).y).Within(0.01f));
        Assert.That(c[c.Count - 1].y, Is.EqualTo(b.End(seg.SignAt(b)).y).Within(0.01f));

        var grades = new List<float>();
        for (int i = 1; i < c.Count; i++)
        {
            float flat = Vector2.Distance(new Vector2(c[i - 1].x, c[i - 1].z), new Vector2(c[i].x, c[i].z));
            if (flat < 1e-3f) continue;
            grades.Add((c[i].y - c[i - 1].y) / flat);
        }
        Assert.That(grades.Count, Is.GreaterThan(10));

        float maxG = 0f, minG = float.MaxValue;
        foreach (var g in grades) { maxG = Mathf.Max(maxG, g); minG = Mathf.Min(minG, g); }
        Assert.That(minG, Is.GreaterThanOrEqualTo(-1e-4f), "途中で下らないこと(単調に上る)");
        Assert.That(maxG, Is.LessThanOrEqualTo(RailDimensions.MaxGrade + 1e-3f),
            "最大勾配が上限35‰を超えないこと(実際は" + (maxG * 1000f).ToString("F1") + "‰)");

        // 両端の勾配がほぼ0であること。ここが非対称だと到着駅で線路が折れる
        Assert.That(Mathf.Abs(grades[0]), Is.LessThan(0.002f),
            "出発側で水平に接すること(" + (grades[0] * 1000f).ToString("F2") + "‰)");
        Assert.That(Mathf.Abs(grades[grades.Count - 1]), Is.LessThan(0.002f),
            "到着側でも水平に接すること(" + (grades[grades.Count - 1] * 1000f).ToString("F2") + "‰)");
    }

    [Test]
    public void FlatSegment_KeepsEveryPointAtTheStationHeight()
    {
        // 同じ階どうしなら縦断は完全に平ら(既存の平地の挙動を変えないことの確認も兼ねる)
        var a = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 0), 0, 8, 2, 2, "A", level: 1);
        var b = EditModeTestHelpers.MakeStation(new Vector3(300, 0, 1200), 30, 8, 2, 2, "B", level: 1);
        var seg = EditModeTestHelpers.Connect(a, b);
        float h = RailDimensions.HeightOfLevel(1);
        foreach (var p in seg.CenterPoints())
            Assert.That(p.y, Is.EqualTo(h).Within(0.001f));
        foreach (var p in seg.SideCentre(TrackSegment.TrackOffset))
            Assert.That(p.y, Is.EqualTo(h).Within(0.001f), "左右へオフセットしても高さが変わらないこと");
    }

    [Test]
    public void ElevatedSegment_GetsAViaductAndAGroundOneDoesNot()
    {
        var a = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 0), 0, 8, 2, 2, "A", level: 1);
        var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 1200), 0, 8, 2, 2, "B", level: 1);
        var seg = EditModeTestHelpers.Connect(a, b);
        seg.Build(BuildController.WorldRoot);
        var via = seg.go.transform.Find("Viaduct");
        Assert.That(via, Is.Not.Null, "高架どうしを結ぶ区間には桁が生成されること");
        var mesh = via.GetComponent<MeshFilter>().sharedMesh;
        Assert.That(mesh.bounds.min.y, Is.EqualTo(0f).Within(0.05f), "橋脚が地面まで届くこと");
        Assert.That(mesh.bounds.max.y,
            Is.LessThanOrEqualTo(RailDimensions.HeightOfLevel(1) + 0.001f),
            "桁がレール基面より上へ出ないこと");

        var c = EditModeTestHelpers.MakeStation(new Vector3(3000, 0, 0), 0, 8, 2, 2, "C");
        var d = EditModeTestHelpers.MakeStation(new Vector3(3000, 0, 1200), 0, 8, 2, 2, "D");
        var flat = EditModeTestHelpers.Connect(c, d);
        flat.Build(BuildController.WorldRoot);
        Assert.That(flat.go.transform.Find("Viaduct"), Is.Null, "地上区間には桁を作らないこと");
    }

    // ---- (B) 描いたレールと走行経路の一致(高低差込み) ----

    [Test]
    public void TrainPath_FollowsTheDrawnCentreLineOnAGradient()
    {
        var a = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 0), 0, 10, 2, 2, "地上");
        var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 1600), 0, 10, 2, 2, "高架", level: 2);
        var seg = EditModeTestHelpers.Connect(a, b);
        a.RebuildTrackVisual(); b.RebuildTrackVisual();

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

        foreach (int ta in new[] { a.layout.stopTracks[0], a.layout.stopTracks[a.layout.stopTracks.Count - 1] })
            foreach (int tb in new[] { b.layout.stopTracks[0], b.layout.stopTracks[b.layout.stopTracks.Count - 1] })
            {
                var leg = Train.BuildLeg(a, ta, seg.SignAt(a), b, tb, seg.SignAt(b), 100f);
                string what = "発" + ta + "→着" + tb + ": ";
                float worst = 0f, worstY = 0f;
                foreach (var p in leg)
                {
                    float best = float.MaxValue;
                    foreach (var r in rails) best = Mathf.Min(best, DistanceToPolyline(p, r));
                    worst = Mathf.Max(worst, best);
                    worstY = Mathf.Max(worstY, Mathf.Abs(p.y - HeightOnPolylines(p, rails)));
                }
                Assert.That(worst, Is.LessThan(0.05f),
                    what + "勾配上でも走行経路が中心線から外れないこと(最大" + worst.ToString("F3") + "m)");
                Assert.That(worstY, Is.LessThan(0.05f),
                    what + "高さ方向にもズレないこと(最大" + worstY.ToString("F3") + "m)");

                float longest = 0f;
                for (int i = 0; i + 1 < leg.Count; i++)
                    longest = Mathf.Max(longest, Vector3.Distance(leg[i], leg[i + 1]));
                Assert.That(longest, Is.LessThan(5f), what + "経路が途中で飛ばないこと");
            }
    }

    // ---- (C) 立体交差 ----

    [Test]
    public void GradeSeparation_AllowsOverlappingFootprintsWhenLevelsDiffer()
    {
        // 十字に交差する配置。同じ階なら干渉、階が違えば干渉しない
        var ground = EditModeTestHelpers.MakeStation(Vector3.zero, 0, 8, 2, 2, "地上");
        var crossSame = EditModeTestHelpers.MakeStation(Vector3.zero, 90, 8, 2, 2, "同じ階");
        Assert.That(Station.FootprintsOverlap(ground, crossSame, 8f), Is.True,
            "同じ階で重なっていれば従来どおり干渉と判定すること");

        var crossHigh = EditModeTestHelpers.MakeStation(Vector3.zero, 90, 8, 2, 2, "高架", level: 1);
        Assert.That(Station.FootprintsOverlap(ground, crossHigh, 8f), Is.False,
            "階が違えば平面が重なっていても立体交差として許可すること");
        Assert.That(RailDimensions.FloorHeight,
            Is.GreaterThanOrEqualTo(RailDimensions.LevelClearance),
            "1階ぶんの高さが立体交差の判定閾値を下回っていないこと");
    }

    [Test]
    public void TrackPassesUnderAnElevatedStationWithoutBeingBlocked()
    {
        // 高架駅の真下を、地上の2駅を結ぶ線路が横切る配置
        var over = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 0), 0, 8, 2, 2, "高架", level: 1);
        var a = EditModeTestHelpers.MakeStation(new Vector3(-1500, 0, 0), 90, 8, 2, 2, "西");
        var b = EditModeTestHelpers.MakeStation(new Vector3(1500, 0, 0), 90, 8, 2, 2, "東");
        var seg = new TrackSegment { a = a, b = b, signA = 1, signB = -1 };
        Assert.That(seg.FindStationCrossedByBed(), Is.Null,
            "高架駅の下は通り抜けられること");

        // 同じ配置でも高架駅を地上に降ろせば従来どおり貫通と判定される
        over.level = 0;
        over.Build();
        Assert.That(seg.FindStationCrossedByBed(), Is.EqualTo(over),
            "地上なら従来どおりホームを貫くと判定すること");
    }

    [Test]
    public void OverlappingStations_AreBothSelectableByTappingAgain()
    {
        // 立体交差で重ねた駅は、上からのレイでは高架駅が必ず手前に来る。
        // 同じ場所を続けてタップしたら下の駅へ切り替わること
        var bcGo = new GameObject("BC");
        var bc = bcGo.AddComponent<BuildController>();
        try
        {
            var ground = EditModeTestHelpers.MakeStation(Vector3.zero, 0, 6, 2, 2, "地上");
            var high = EditModeTestHelpers.MakeStation(Vector3.zero, 90, 6, 2, 2, "高架", level: 1);
            Physics.SyncTransforms();

            var ray = new Ray(new Vector3(0, 500f, 0), Vector3.down);
            var first = bc.PickStation(ray);
            var second = bc.PickStation(ray);
            var third = bc.PickStation(ray);

            Assert.That(first, Is.EqualTo(high), "まず手前(高架)が選ばれること");
            Assert.That(second, Is.EqualTo(ground), "続けてタップすると下の地上駅へ切り替わること");
            Assert.That(third, Is.EqualTo(high), "さらにタップすると先頭へ戻ること");
        }
        finally { Object.DestroyImmediate(bcGo); }
    }

    [Test]
    public void PlatformAreaContains_IgnoresPointsFarAboveOrBelow()
    {
        var st = EditModeTestHelpers.MakeStation(Vector3.zero, 0, 8, 2, 2, "駅");
        var onPlatform = st.transform.TransformPoint(new Vector3(st.layout.platforms[0].x, 0, 0));
        Assert.That(st.PlatformAreaContains(onPlatform, 0f), Is.True);
        Assert.That(st.PlatformAreaContains(onPlatform + Vector3.up * RailDimensions.FloorHeight, 0f),
            Is.False, "1階ぶん上を通る線路はホームに当たらないこと");
    }

    // ---- 勾配制限 ----

    [Test]
    public void RequiredHorizontalDistance_MatchesTheActualMaximumGrade()
    {
        // BuildController.TapTrackStationが使う式 1.5*Δh/MaxGrade が、
        // 実際に生成される縦断の最大勾配と一致することを確かめる
        float dh = RailDimensions.FloorHeight;                 // 8m(2階)
        float need = 1.5f * dh / RailDimensions.MaxGrade;      // 約343m

        var a = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 0), 0, 4, 1, 1, "地上");
        var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, need + a.HalfLen + b_HalfLenGuess()), 0,
            4, 1, 1, "高架", level: 1);
        var seg = EditModeTestHelpers.Connect(a, b);
        var c = seg.CenterPoints();

        float maxG = 0f;
        for (int i = 1; i < c.Count; i++)
        {
            float flat = Vector2.Distance(new Vector2(c[i - 1].x, c[i - 1].z), new Vector2(c[i].x, c[i].z));
            if (flat < 1e-3f) continue;
            maxG = Mathf.Max(maxG, Mathf.Abs(c[i].y - c[i - 1].y) / flat);
        }
        // スロート端どうしの距離は上で足した分よりさらに離れるので、上限は必ず満たす
        Assert.That(maxG, Is.LessThanOrEqualTo(RailDimensions.MaxGrade + 1e-3f),
            "必要距離ぶん離せば上限を満たすこと(" + (maxG * 1000f).ToString("F1") + "‰)");
    }

    // 駅の半長は建設前に確定しないので、テスト用に十分な余裕を取る
    static float b_HalfLenGuess() => 4 * StationLayout.CarLength * 0.5f + StationLayout.ThroatLen * 2f;

    [Test]
    public void Rebuild_AppliesTheNewLevelButRefusesToMakeConnectedTrackTooSteep()
    {
        var bcGo = new GameObject("BC");
        var bc = bcGo.AddComponent<BuildController>();
        try
        {
            // 近い2駅(スロート端どうし約400m)。8mなら343m必要で通るが、24mには686m要る
            var a = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 0), 0, 4, 1, 1, "A");
            var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 700), 0, 4, 1, 1, "B");
            var seg = EditModeTestHelpers.Connect(a, b);
            float flat = Vector2.Distance(
                new Vector2(a.End(seg.SignAt(a)).x, a.End(seg.SignAt(a)).z),
                new Vector2(b.End(seg.SignAt(b)).x, b.End(seg.SignAt(b)).z));
            Assert.That(flat, Is.GreaterThan(1.5f * RailDimensions.FloorHeight / RailDimensions.MaxGrade),
                "この配置は2階なら繋げられる距離であること(テスト前提)");
            Assert.That(flat, Is.LessThan(1.5f * RailDimensions.FloorHeight * 3f / RailDimensions.MaxGrade),
                "この配置は4階には足りない距離であること(テスト前提)");

            Assert.That(bc.RebuildStation(a, 4, 1, 1, level: 3), Is.False,
                "急勾配になる建て替えは拒否すること");
            Assert.That(a.level, Is.EqualTo(0), "拒否したら階は変わらないこと");

            Assert.That(bc.RebuildStation(a, 4, 1, 1, level: 1), Is.True);
            Assert.That(a.level, Is.EqualTo(1), "建て替えで階が反映されること");
            Assert.That(a.transform.position.y,
                Is.EqualTo(RailDimensions.HeightOfLevel(1)).Within(0.001f));

            Assert.That(bc.RebuildStation(a, 6, 1, 1), Is.True);
            Assert.That(a.level, Is.EqualTo(1), "階を省略した建て替えでは現在の階を維持すること");
        }
        finally { Object.DestroyImmediate(bcGo); }
    }

    [Test]
    public void Rebuild_MeasuresTheGradeWithTheNewStationLength()
    {
        // 両数を増やすとスロート端が相手側へ寄り、水平距離が縮む。
        // 現在のEnd()で測っていると、階と両数の同時変更が検査をすり抜ける
        var bcGo = new GameObject("BC");
        var bc = bcGo.AddComponent<BuildController>();
        try
        {
            float need = 1.5f * RailDimensions.FloorHeight / RailDimensions.MaxGrade;   // 約343m
            var a = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 0), 0, 2, 1, 1, "A");
            // 2両のままなら余裕で足りるが、10両にすると端が寄って足りなくなる距離に置く
            float gapAt2 = need + 30f;
            float zB = a.HalfLen + StationLayout.ThroatLen + gapAt2
                + StationLayout.Length(2) * 0.5f + StationLayout.ThroatLen;
            var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, zB), 0, 2, 1, 1, "B");
            var seg = EditModeTestHelpers.Connect(a, b);

            float shrink = (StationLayout.Length(10) - StationLayout.Length(2)) * 0.5f;
            Assert.That(gapAt2 - shrink, Is.LessThan(need),
                "10両化で必要距離を割り込む配置であること(テスト前提)");

            Assert.That(bc.RebuildStation(a, 10, 1, 1, level: 1), Is.False,
                "建て替え後の駅長で測れば急勾配になるので拒否すること");
            Assert.That(a.level, Is.EqualTo(0));
            Assert.That(a.cars, Is.EqualTo(2), "拒否したら両数も変わらないこと");

            Assert.That(bc.RebuildStation(a, 2, 1, 1, level: 1), Is.True,
                "駅長を変えなければ同じ階変更が通ること");
        }
        finally { Object.DestroyImmediate(bcGo); }
    }

    // ---- セーブ v5 ----

    [Test]
    public void SaveV5_RoundTripsStationLevel()
    {
        var a = EditModeTestHelpers.MakeStation(new Vector3(-2000, 0, 0), 90, 6, 2, 2, "地上");
        var b = EditModeTestHelpers.MakeStation(new Vector3(2000, 0, 0), 90, 6, 2, 2, "高架", level: 2);
        EditModeTestHelpers.Connect(a, b);
        SaveLoad.Save();

        string raw = PlayerPrefs.GetString(Key);
        Assert.That(raw, Does.Contain("\"v\":5"), "保存は常に最新版(v5)で行われること");
        Assert.That(raw, Does.Contain("\"level\":2"), "階が保存されること");

        EditModeTestHelpers.DestroyWorldRoot();
        TrackNetwork.Clear();
        Assert.That(SaveLoad.Load(), Is.True);

        Station loadedHigh = null, loadedGround = null;
        foreach (var st in TrackNetwork.stations)
        {
            if (st.stationName == "高架") loadedHigh = st;
            if (st.stationName == "地上") loadedGround = st;
        }
        Assert.That(loadedHigh, Is.Not.Null);
        Assert.That(loadedHigh.level, Is.EqualTo(2));
        Assert.That(loadedHigh.transform.position.y,
            Is.EqualTo(RailDimensions.HeightOfLevel(2)).Within(0.001f),
            "復元時に階に応じた高さへ戻ること");
        Assert.That(loadedGround.level, Is.EqualTo(0));
        Assert.That(loadedGround.transform.position.y, Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void V4Save_LoadsAsGroundLevel()
    {
        // levelの概念が無いv4セーブは全駅が地上として読める(後方互換)
        var a = EditModeTestHelpers.MakeStation(new Vector3(-2000, 0, 0), 90, 6, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(2000, 0, 0), 90, 6, 2, 2, "B");
        EditModeTestHelpers.Connect(a, b);
        SaveLoad.Save();

        // v5のJSONからlevelフィールドを落とし、versionをv4へ落として「v4のセーブ」を作る
        string v4 = PlayerPrefs.GetString(Key)
            .Replace("\"v\":5", "\"v\":4")
            .Replace(",\"level\":0", "");
        Assert.That(v4, Does.Not.Contain("\"level\""));
        PlayerPrefs.SetString(Key, v4);

        EditModeTestHelpers.DestroyWorldRoot();
        TrackNetwork.Clear();
        Assert.That(SaveLoad.Load(), Is.True, "v4セーブが読めること");
        Assert.That(TrackNetwork.stations.Count, Is.EqualTo(2));
        foreach (var st in TrackNetwork.stations)
        {
            Assert.That(st.level, Is.EqualTo(0));
            Assert.That(st.transform.position.y, Is.EqualTo(0f).Within(0.001f));
        }
    }

    [Test]
    public void ElevatedStation_CostsMoreThanTheSameStationOnTheGround()
    {
        double ground = GameState.StationCost(8, 2, 2, 0);
        double second = GameState.StationCost(8, 2, 2, 1);
        double third = GameState.StationCost(8, 2, 2, 2);
        Assert.That(second, Is.GreaterThan(ground));
        Assert.That(third, Is.GreaterThan(second));
        Assert.That(GameState.StationCost(8, 2, 2), Is.EqualTo(ground),
            "階を省略した場合は従来どおり地上価格になること");
    }

    // ---- 補助 ----

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

    // 経路点に最も近い中心線上の点の高さ。高さ方向のズレだけを取り出して測るのに使う
    static float HeightOnPolylines(Vector3 p, List<List<Vector3>> lines)
    {
        float best = float.MaxValue, y = p.y;
        foreach (var line in lines)
            for (int i = 0; i + 1 < line.Count; i++)
            {
                Vector3 s0 = line[i], e = line[i + 1], d = e - s0;
                float len2 = d.sqrMagnitude;
                float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(p - s0, d) / len2);
                Vector3 q = s0 + d * t;
                float dist = Vector3.Distance(p, q);
                if (dist < best) { best = dist; y = q.y; }
            }
        return y;
    }
}
