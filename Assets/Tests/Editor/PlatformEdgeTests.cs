using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// M2-D: ホーム縁(物理線の片側、乗降モード)のEditModeテスト。
// Station側のモード管理・CanBoardAt/CanAlightAt、Train.Board/Arriveとの統合、
// 改築時のモード継承/リセット規則を検証する。
public class PlatformEdgeTests
{
    [SetUp]
    public void SetUp() => TrackNetwork.Clear();

    [TearDown]
    public void TearDown()
    {
        EditModeTestHelpers.DestroyWorldRoot();
        TrackNetwork.Clear();
    }

    static Station Make3Faces2Lines(Vector3 pos, string name) =>
        EditModeTestHelpers.MakeStation(pos, 90, 8, 3, 2, name);

    [Test]
    public void PlatformEdges_3Faces2Lines_AllNormalByDefault()
    {
        var st = Make3Faces2Lines(Vector3.zero, "A");
        Assert.That(st.PlatformEdges.Count, Is.EqualTo(4));
        foreach (var e in st.PlatformEdges)
            Assert.That(e.mode, Is.EqualTo(StationLayout.PlatformEdgeMode.Normal), "新規ホーム縁の初期値は乗降可(既存挙動維持)");
    }

    [Test]
    public void SetPlatformEdgeMode_ChangesOnlyTargetedEdge()
    {
        var st = Make3Faces2Lines(Vector3.zero, "A");
        bool ok = st.SetPlatformEdgeMode(0, 1, StationLayout.PlatformEdgeMode.BoardOnly);
        Assert.That(ok, Is.True);

        int changed = 0;
        foreach (var e in st.PlatformEdges)
        {
            if (e.trackIndex == 0 && e.side == 1) { Assert.That(e.mode, Is.EqualTo(StationLayout.PlatformEdgeMode.BoardOnly)); changed++; }
            else Assert.That(e.mode, Is.EqualTo(StationLayout.PlatformEdgeMode.Normal), "対象外の縁は変化しないこと");
        }
        Assert.That(changed, Is.EqualTo(1));
    }

    [Test]
    public void SetPlatformEdgeMode_UnknownKey_ReturnsFalse()
    {
        var st = Make3Faces2Lines(Vector3.zero, "A");
        Assert.That(st.SetPlatformEdgeMode(99, 1, StationLayout.PlatformEdgeMode.Disabled), Is.False);
    }

    // trackに複数のホーム縁がある場合、どちらか一方でも許可すればCanBoardAt/CanAlightAtは真
    [Test]
    public void CanBoardAndAlight_OrAcrossBothEdgesOfSameTrack()
    {
        var st = Make3Faces2Lines(Vector3.zero, "A"); // track0: platform0(外側)側, platform1(島式)側
        int track = 0;
        // 両方Disabledなら不可
        st.SetPlatformEdgeMode(track, -1, StationLayout.PlatformEdgeMode.Disabled);
        st.SetPlatformEdgeMode(track, 1, StationLayout.PlatformEdgeMode.Disabled);
        Assert.That(st.CanBoardAt(track), Is.False);
        Assert.That(st.CanAlightAt(track), Is.False);

        // 片側だけBoardOnlyに戻せば、乗車は可能・降車は不可のまま
        st.SetPlatformEdgeMode(track, -1, StationLayout.PlatformEdgeMode.BoardOnly);
        Assert.That(st.CanBoardAt(track), Is.True, "片方の縁がBoardOnlyなら乗車可能");
        Assert.That(st.CanAlightAt(track), Is.False, "BoardOnlyの縁は降車を許さない。もう片方はDisabledのまま");

        // もう片方をAlightOnlyにすれば、乗車・降車とも可能になる(要求仕様: 片側BoardOnly+反対側AlightOnlyならその線は両方可)
        st.SetPlatformEdgeMode(track, 1, StationLayout.PlatformEdgeMode.AlightOnly);
        Assert.That(st.CanBoardAt(track), Is.True);
        Assert.That(st.CanAlightAt(track), Is.True);
    }

    [Test]
    public void CanBoardAndAlight_TrackWithNoEdges_DefaultsToDisallowed()
    {
        var st = Make3Faces2Lines(Vector3.zero, "A");
        // ホーム縁が1つも無いtrackは乗降不可(実装後レビューでCodex CLIが指摘: 不正な
        // trackIndexを誤って許可しないための防御。実際のゲームプレイではcurTrackは
        // 常にStopTracksの一部=必ず1つ以上ホーム縁を持つため、通常は到達しない)
        Assert.That(st.CanBoardAt(99), Is.False);
        Assert.That(st.CanAlightAt(99), Is.False);
    }

    // ==================== 改築時のモード継承/リセット ====================

    [Test]
    public void Build_CarsOnlyChange_PreservesEdgeModes()
    {
        var st = Make3Faces2Lines(Vector3.zero, "A");
        st.SetPlatformEdgeMode(0, -1, StationLayout.PlatformEdgeMode.Disabled);

        st.cars = 10; // faces/linesはそのまま
        st.Build();

        bool found = false;
        foreach (var e in st.PlatformEdges)
            if (e.trackIndex == 0 && e.side == -1) { Assert.That(e.mode, Is.EqualTo(StationLayout.PlatformEdgeMode.Disabled)); found = true; }
        Assert.That(found, Is.True, "cars変更のみの再構築ではホーム縁モードが維持されること");
    }

    [Test]
    public void Build_FacesOrLinesChange_ResetsAllEdgesToNormal()
    {
        var st = Make3Faces2Lines(Vector3.zero, "A");
        st.SetPlatformEdgeMode(0, -1, StationLayout.PlatformEdgeMode.Disabled);

        st.faces = 2; st.lines = 2; // 構成変更
        st.Build();

        foreach (var e in st.PlatformEdges)
            Assert.That(e.mode, Is.EqualTo(StationLayout.PlatformEdgeMode.Normal), "面/線数が変わる改築では全ホーム縁がNormalへリセットされること");
    }

    // ==================== Trainとの統合(乗降ゲート・二重計上防止) ====================

    static Train MakeTrainAt(Station from, Station to, out int fromTrack)
    {
        from.TryReserve(out fromTrack);
        var go = new GameObject("Train");
        go.transform.SetParent(BuildController.WorldRoot, false);
        var t = go.AddComponent<Train>();
        t.id = ++TrackNetwork.trainIdCounter;
        TrackNetwork.trains.Add(t);
        t.Init(TrainCatalog.Formations[0], new List<Station> { from, to }, new List<int> { fromTrack, to.StopTracks[0] });
        return t;
    }

    [Test]
    public void Board_DisabledOnBothEdges_PreventsBoardingButKeepsDepartureWorking()
    {
        var a = Make3Faces2Lines(new Vector3(-800, 0, 0), "A");
        var b = Make3Faces2Lines(new Vector3(800, 0, 0), "B");
        EditModeTestHelpers.Connect(a, b);
        a.waiting[b] = 10;

        var tr = MakeTrainAt(a, b, out int fromTrack);
        // 出発駅Aの現在track両縁を使用停止にする(スナップショットしてから変更。
        // PlatformEdgesを反復しながらSetPlatformEdgeModeで同じリストを書き換えると
        // List<T>の列挙が無効になるため)
        var fromTrackSides = new List<int>();
        foreach (var e in a.PlatformEdges) if (e.trackIndex == fromTrack) fromTrackSides.Add(e.side);
        foreach (int side in fromTrackSides) a.SetPlatformEdgeMode(fromTrack, side, StationLayout.PlatformEdgeMode.Disabled);

        for (int i = 0; i < 600 && tr.IsDwelling; i++) tr.SimTick(Bootstrap.TickSeconds);

        Assert.That(tr.IsDwelling, Is.False, "使用停止でも発車・閉塞・番線予約自体は妨げられないこと");
        Assert.That(tr.OnboardCount, Is.EqualTo(0), "乗車可能なホーム縁が無いため乗車0人");
        Assert.That(a.waiting.ContainsKey(b) && a.waiting[b] == 10, Is.True, "待客はそのまま残ること");
    }

    [Test]
    public void Arrive_AlightDisabled_KeepsPassengersOnboardAndDoesNotEarnFare()
    {
        var a = Make3Faces2Lines(new Vector3(-800, 0, 0), "A");
        var b = Make3Faces2Lines(new Vector3(800, 0, 0), "B");
        EditModeTestHelpers.Connect(a, b);
        a.waiting[b] = 5;

        var tr = MakeTrainAt(a, b, out int fromTrack);
        // 到着駅Bの停車予定track両縁を降車禁止(乗車専用)にしておく(スナップショットしてから変更)
        int destTrack = b.StopTracks[0];
        var destTrackSides = new List<int>();
        foreach (var e in b.PlatformEdges) if (e.trackIndex == destTrack) destTrackSides.Add(e.side);
        foreach (int side in destTrackSides) b.SetPlatformEdgeMode(destTrack, side, StationLayout.PlatformEdgeMode.BoardOnly);

        double moneyBefore = GameState.money;
        long carriedBefore = GameState.carried;
        for (int i = 0; i < 6000 && !(tr.IsDwelling && tr.ArrivalCount > 0); i++) tr.SimTick(Bootstrap.TickSeconds);

        Assert.That(tr.ArrivalCount, Is.EqualTo(1));
        Assert.That(tr.OnboardCount, Is.EqualTo(5), "降車不可なので乗客は車内に残ること");
        Assert.That(GameState.money, Is.EqualTo(moneyBefore), "運賃収受が行われないこと");
        Assert.That(GameState.carried, Is.EqualTo(carriedBefore), "輸送実績に加算されないこと");
    }

    // ==================== 改築時の在線列車の安全性 ====================

    [Test]
    public void RebuildStation_WithTrainDwelling_ResyncsSafelyAndResetsEdges()
    {
        var a = Make3Faces2Lines(new Vector3(-800, 0, 0), "A");
        var b = Make3Faces2Lines(new Vector3(800, 0, 0), "B");
        EditModeTestHelpers.Connect(a, b);
        var bc = new GameObject("BC").AddComponent<BuildController>();
        a.id = ++TrackNetwork.stationIdCounter; // BuildController.RebuildStationは実駅想定なのでid付与
        b.id = ++TrackNetwork.stationIdCounter;

        var tr = MakeTrainAt(a, b, out int fromTrack);
        a.SetPlatformEdgeMode(fromTrack, -1, StationLayout.PlatformEdgeMode.Disabled);
        GameState.money = 100e9; // 建て替え費用を賄う

        bool ok = bc.RebuildStation(a, cars: 10, faces: 2, lines: 2); // 構成変更(3面2線→2面2線)
        Assert.That(ok, Is.True);

        Assert.That(a.faces, Is.EqualTo(2));
        foreach (var e in a.PlatformEdges)
            Assert.That(e.mode, Is.EqualTo(StationLayout.PlatformEdgeMode.Normal), "構成が変わる改築ではホーム縁がNormalへリセットされること");
        // ResyncToNetwork経由で列車が新レイアウトの有効な番線へ復帰していること
        Assert.That(tr.curTrack, Is.InRange(0, a.occupied.Length - 1));
        Assert.That(a.occupied[tr.curTrack], Is.True, "列車が新レイアウトの番線を正しく占有していること");
    }

    // 両側にホームがあっても、同じ旅客・運賃・輸送実績が二重に計上されないことの確認。
    // 3面2線の中央(島式)駅を経由させ、両側の縁が両方Normalのままでも1回しか
    // 乗降処理が走らないことを、乗車後の車内人数・待客の減り方から検証する
    [Test]
    public void Board_BothEdgesNormal_DoesNotDoubleCountPassengers()
    {
        var a = Make3Faces2Lines(new Vector3(-800, 0, 0), "A");
        var b = Make3Faces2Lines(new Vector3(800, 0, 0), "B");
        EditModeTestHelpers.Connect(a, b);
        a.waiting[b] = 7;

        var tr = MakeTrainAt(a, b, out int fromTrack);
        // fromTrackの両縁がNormal(既定)のままであることを前提に発車させる
        for (int i = 0; i < 600 && tr.IsDwelling; i++) tr.SimTick(Bootstrap.TickSeconds);

        Assert.That(tr.OnboardCount, Is.EqualTo(7), "両側の縁がNormalでも乗車人数は1回分(7人)だけ");
        Assert.That(a.waiting.ContainsKey(b), Is.False, "待客は1回分だけ減ること(二重減算されない)");
    }
}
