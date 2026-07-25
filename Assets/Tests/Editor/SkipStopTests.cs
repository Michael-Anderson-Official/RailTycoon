using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// 通過駅(スキップストップ)対応のEditModeテスト。route(停車駅のみ)には通過駅を
// 含めず、Train.TryDepartが隣接しない停車駅間をTrackNetwork.FindPath経由で
// 見つけたホップ列(通過駅)へ内部的に分解する設計になっている。ここでは、
// 通過駅では停車・乗降が一切発生しないこと、通過駅の番線は動的に(固定でなく)
// 空きを探して確保・解放されること、素朴な連結による幾何の折り返しが無いこと、
// 途中駅の確保に失敗した場合に全て巻き戻ることを確認する
public class SkipStopTests
{
    [SetUp]
    public void SetUp() => TrackNetwork.Clear();

    [TearDown]
    public void TearDown()
    {
        EditModeTestHelpers.DestroyWorldRoot();
        TrackNetwork.Clear();
    }

    static (Station a, Station b, Station c, Train train) MakeSkipStopTrain(bool cars10 = false)
    {
        var a = EditModeTestHelpers.MakeStation(new Vector3(-6000, 0, 0), 90, 6, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 0), 90, 6, 2, 2, "B");
        var c = EditModeTestHelpers.MakeStation(new Vector3(6000, 0, 0), 90, 6, 2, 2, "C");
        EditModeTestHelpers.Connect(a, b);
        EditModeTestHelpers.Connect(b, c);

        var fm = TrainCatalog.Formations[0];
        int trackA;
        a.TryReserve(out trackA);
        var go = new GameObject("Train");
        go.transform.SetParent(BuildController.WorldRoot, false);
        var train = go.AddComponent<Train>();
        // route=[A,C]。Bは経路に含まれない=通過駅
        train.Init(fm, new List<Station> { a, c }, new List<int> { trackA, c.StopTracks[0] });
        return (a, b, c, train);
    }

    // ---- 折返し(同じ駅へ戻る系統) ----
    // 経路に同じ駅を2回入れられ、往路と復路で別の番線を指定できること

    // 実際の配車はBuildItinerary経由で経路を組む。Train.Initを直接呼ぶ検証だけでは
    // この経路を通らないため、末尾が削られて復路の番線指定が消える不具合を見逃した
    [Test]
    public void BuildItinerary_KeepsReturnStopWhenItsTrackDiffers()
    {
        var a = EditModeTestHelpers.MakeStation(Vector3.zero, 0, 10, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 1400), 0, 10, 2, 2, "B");
        EditModeTestHelpers.Connect(a, b);
        int outbound = a.layout.stopTracks[0];
        int inbound = a.layout.stopTracks[a.layout.stopTracks.Count - 1];
        Assert.That(outbound, Is.Not.EqualTo(inbound));

        var line = new ServiceLine
        {
            id = 1,
            route = new List<Station> { a, b, a },
            tracks = new List<int> { outbound, b.StopTracks[0], inbound },
        };
        BuildController.BuildItinerary(new List<ServiceLine> { line },
            out var route, out var tracks, out _);

        Assert.That(route.Count, Is.EqualTo(3), "折返しの復路が削られないこと");
        Assert.That(route[2], Is.EqualTo(a));
        Assert.That(tracks[2], Is.EqualTo(inbound), "復路の番線指定が残ること");
    }

    // 番線まで同じ末尾は従来どおり重複として削り、巡回運転にする
    [Test]
    public void BuildItinerary_TrimsReturnStopWhenTrackIsIdentical()
    {
        var a = EditModeTestHelpers.MakeStation(Vector3.zero, 0, 10, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 1400), 0, 10, 2, 2, "B");
        EditModeTestHelpers.Connect(a, b);
        int t = a.layout.stopTracks[0];

        var line = new ServiceLine
        {
            id = 1,
            route = new List<Station> { a, b, a },
            tracks = new List<int> { t, b.StopTracks[0], t },
        };
        BuildController.BuildItinerary(new List<ServiceLine> { line },
            out var route, out var tracks, out _);

        Assert.That(route.Count, Is.EqualTo(2), "同じ番線で戻る末尾は重複として削ること");
        Assert.That(tracks.Count, Is.EqualTo(2));
    }

    [Test]
    public void TurnbackRoute_ReturnsToSameStationOnTheSpecifiedTrack()
    {
        var a = EditModeTestHelpers.MakeStation(Vector3.zero, 0, 10, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 1400), 0, 10, 2, 2, "B");
        EditModeTestHelpers.Connect(a, b);

        int outbound = a.layout.stopTracks[0];
        int inbound = a.layout.stopTracks[a.layout.stopTracks.Count - 1];
        Assert.That(outbound, Is.Not.EqualTo(inbound), "この検証には2番線以上のAが必要");

        var fm = TrainCatalog.Formations[0];
        a.TryReserveSpecific(outbound);
        var go = new GameObject("Turnback");
        go.transform.SetParent(BuildController.WorldRoot, false);
        var train = go.AddComponent<Train>();
        TrackNetwork.trains.Add(train);
        // A(往路番線) → B → A(復路番線)
        train.Init(fm, new List<Station> { a, b, a },
            new List<int> { outbound, b.StopTracks[0], inbound });

        Assert.That(train.cyclic, Is.False, "起点と終点が同じ駅なら折返し運転になること");

        float tick = Bootstrap.TickSeconds;
        var arrivals = new List<(string station, int track)>();
        int seen = 0;
        for (int i = 0; i < 400000 && arrivals.Count < 2; i++)
        {
            train.SimTick(tick);
            if (train.ArrivalCount > seen)
            {
                seen = train.ArrivalCount;
                arrivals.Add((train.route[train.idx].stationName, train.curTrack));
            }
        }

        Assert.That(arrivals.Count, Is.GreaterThanOrEqualTo(2), "B経由でAへ戻ってくること");
        Assert.That(arrivals[0].station, Is.EqualTo("B"));
        Assert.That(arrivals[1].station, Is.EqualTo("A"), "同じ駅へ戻れること");
        Assert.That(arrivals[1].track, Is.EqualTo(inbound),
            "復路は往路と別の、指定した番線へ入ること");
    }

    [Test]
    public void PassThroughStation_NeverDwellsOrBoardsAlights()
    {
        var (a, b, c, train) = MakeSkipStopTrain();
        b.waiting[c] = 5; // Bで待っている、Cへ行きたい乗客(通過駅では乗せてはいけない)
        int bDevBefore = b.DevLevel;

        float tick = Bootstrap.TickSeconds;
        for (int i = 0; i < 120000; i++)
        {
            train.SimTick(tick);
            if (train.ArrivalCount >= 1) break;
        }

        // 通過駅Bはrouteに含まれない(route=[A,C])ため、idxが指す「現在/直前駅」は常にA
        // またはCのみで、Bでの到着(Arrive)・乗降(Board/OnDeparture)は構造上発生し得ない。
        // ここではその帰結として、Bの待ち客・発展レベルが変化しないことを確認する
        Assert.That(train.ArrivalCount, Is.EqualTo(1), "A→Cへ正常に到着できること");
        Assert.That(b.waiting.ContainsKey(c) && b.waiting[c] == 5, Is.True, "通過駅Bでは乗車処理が起きず、待ち客数が変化しないこと");
        Assert.That(b.DevLevel, Is.EqualTo(bDevBefore), "通過駅Bでは発車(OnDeparture)が起きず、発展レベルが変化しないこと");
    }

    [Test]
    public void PassThroughStation_ReservesThenReleasesTrack()
    {
        var (a, b, c, train) = MakeSkipStopTrain();
        int bTrack = b.StopTracks[0];

        float tick = Bootstrap.TickSeconds;
        bool sawOccupied = false, sawReleasedAfterOccupied = false;
        for (int i = 0; i < 120000; i++)
        {
            train.SimTick(tick);
            if (b.occupied[bTrack]) sawOccupied = true;
            if (sawOccupied && !b.occupied[bTrack]) sawReleasedAfterOccupied = true;
            if (train.ArrivalCount >= 1) break;
        }

        Assert.That(sawOccupied, Is.True, "通過中は駅Bの番線を確保していること");
        Assert.That(sawReleasedAfterOccupied, Is.True, "通過し終えたら駅Bの番線を解放すること(他列車が使えるように)");
        Assert.That(b.occupied[bTrack], Is.False, "到着後は駅Bの番線が残らず解放されていること");
    }

    [Test]
    public void PassThroughStation_PicksFreeTrackNotDefault()
    {
        var a = EditModeTestHelpers.MakeStation(new Vector3(-6000, 0, 0), 90, 6, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 0), 90, 6, 2, 2, "B");
        var c = EditModeTestHelpers.MakeStation(new Vector3(6000, 0, 0), 90, 6, 2, 2, "C");
        var segAB = EditModeTestHelpers.Connect(a, b);
        EditModeTestHelpers.Connect(b, c);

        // Bの「自然な」左側優先番線を先に塞いでおく(別の列車が停車中という想定)
        int enterSignAtB = segAB.SignAt(b);
        int prefTrack = -1;
        foreach (int t in b.StopTracks) if (Mathf.Sign(b.layout.trackOffsets[t]) == enterSignAtB) { prefTrack = t; break; }
        if (prefTrack < 0) prefTrack = b.StopTracks[0];
        int altTrack = -1;
        foreach (int t in b.StopTracks) if (t != prefTrack) { altTrack = t; break; }
        Assert.That(altTrack, Is.Not.EqualTo(-1), "このテストには2番線以上のBが必要");
        b.TryReserveSpecific(prefTrack);

        var fm = TrainCatalog.Formations[0];
        int trackA;
        a.TryReserve(out trackA);
        var go = new GameObject("Train");
        go.transform.SetParent(BuildController.WorldRoot, false);
        var train = go.AddComponent<Train>();
        train.Init(fm, new List<Station> { a, c }, new List<int> { trackA, c.StopTracks[0] });

        float tick = Bootstrap.TickSeconds;
        bool usedAltTrack = false;
        for (int i = 0; i < 120000; i++)
        {
            train.SimTick(tick);
            if (b.occupied[altTrack]) { usedAltTrack = true; break; }
            if (train.ArrivalCount >= 1) break;
        }

        Assert.That(usedAltTrack, Is.True, "塞がっている番線ではなく、空いている番線を動的に選んで通過すること");
    }

    [Test]
    public void MultiHopPath_GeometryIsMonotonicThroughPassStation()
    {
        var (a, b, c, train) = MakeSkipStopTrain();
        var segAB = TrackNetwork.Find(a, b);
        var segBC = TrackNetwork.Find(b, c);
        int trackA = a.StopTracks[0];
        int bTrack = b.StopTracks[0];

        var waypoints = new List<(Station st, int track, int enterSign, int exitSign)>
        {
            (a, trackA, 0, segAB.SignAt(a)),
            (b, bTrack, segAB.SignAt(b), segBC.SignAt(b)),
            (c, c.StopTracks[0], segBC.SignAt(c), 0),
        };
        var path = Train.BuildMultiLeg(waypoints, train.HalfTrain);

        // 素朴にBuildLegを複数連結すると、通過駅Bの前後でhalfTrain(数十〜100m超)分
        // 行って戻る折り返しが混入する。進行方向(+X)に対してそのような後退が
        // 無い(=単調に近い)ことを確認する
        float maxBacktrack = 0f, prevX = float.NegativeInfinity;
        foreach (var p in path)
        {
            if (p.x < prevX) maxBacktrack = Mathf.Max(maxBacktrack, prevX - p.x);
            prevX = Mathf.Max(prevX, p.x);
        }
        Assert.That(maxBacktrack, Is.LessThan(5f), "通過駅を挟んだ経路の連結に、素朴な連結による折り返し(halfTrain規模の後退)が無いこと");
    }

    [Test]
    public void TryDepart_SkipStop_RollsBackAllClaimsOnIntermediateFailure()
    {
        var a = EditModeTestHelpers.MakeStation(new Vector3(-6000, 0, 0), 90, 6, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 0), 90, 6, 2, 2, "B");
        var c = EditModeTestHelpers.MakeStation(new Vector3(6000, 0, 0), 90, 6, 2, 2, "C");
        var segAB = EditModeTestHelpers.Connect(a, b);
        EditModeTestHelpers.Connect(b, c);
        foreach (int t in b.StopTracks) b.TryReserveSpecific(t); // Bの全番線を塞ぐ

        var fm = TrainCatalog.Formations[0];
        int trackA;
        a.TryReserve(out trackA);
        var go = new GameObject("Train");
        go.transform.SetParent(BuildController.WorldRoot, false);
        var train = go.AddComponent<Train>();
        train.Init(fm, new List<Station> { a, c }, new List<int> { trackA, c.StopTracks[0] });

        float tick = Bootstrap.TickSeconds;
        for (int i = 0; i < 600; i++) train.SimTick(tick); // 十分な回数リトライさせる

        Assert.That(train.DepartureCount, Is.EqualTo(0), "経由駅Bの番線が全て塞がっていれば発車できないこと");
        Assert.That(train.IsDwelling, Is.True);
        Assert.That(segAB.OccupantFrom(a), Is.Null, "発車に失敗した際、A-B区間の閉塞を漏れなく巻き戻していること");
    }
}
