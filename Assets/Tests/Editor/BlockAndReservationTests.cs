using NUnit.Framework;
using UnityEngine;

// 閉塞(TrackSegment.TryEnter/Leave)・左側通行・番線予約(Station.TryReserveFor)のEditModeテスト。
// 既存の手動バッチテスト BlockTest.cs の3条件をAssertベースに移行したもの。
public class BlockAndReservationTests
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
    }

    [Test]
    public void EastboundPath_KeepsLeftOfCenterline()
    {
        // A(西)→B(東)、どちらも軸は東西(yaw90)
        var a = EditModeTestHelpers.MakeStation(new Vector3(-700, 0, 0), 90, 6, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(700, 0, 0), 90, 6, 2, 2, "B");
        var seg = EditModeTestHelpers.Connect(a, b);

        int trA = a.layout.stopTracks[0];
        int trB = b.layout.stopTracks[0];
        var path = Train.BuildLeg(a, trA, seg.SignAt(a), b, trB, seg.SignAt(b), 60f);
        var cum = RailKit.Cumulative(path);
        RailKit.Sample(path, cum, cum[cum.Length - 1] * 0.5f, out Vector3 mid, out _);

        // 東行き(A→B)の経路中間は中心線の北(+z)側=左側にあるはず
        Assert.That(mid.z, Is.GreaterThan(1f), "左側通行: 経路中間が中心線より北(左側)にあること");
    }

    [Test]
    public void TryReserveFor_SelectsLeftHandPlatform()
    {
        var a = EditModeTestHelpers.MakeStation(new Vector3(-700, 0, 0), 90, 6, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(700, 0, 0), 90, 6, 2, 2, "B");
        var seg = EditModeTestHelpers.Connect(a, b);

        bool reserved = b.TryReserveFor(seg.SignAt(b), out int track);
        Assert.That(reserved, Is.True);

        float off = b.layout.trackOffsets[track];
        // Bの進行方向はローカル-enterSign*z。左側はローカルx符号=enterSign
        Assert.That(Mathf.Sign(off), Is.EqualTo((float)seg.SignAt(b)),
            "進行方向に対して左側のホーム線が選ばれること");

        b.Release(track);
    }

    [Test]
    public void TrackSegment_Block_RejectsSameDirectionDuplicateEntry()
    {
        var a = EditModeTestHelpers.MakeStation(new Vector3(-700, 0, 0), 90, 6, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(700, 0, 0), 90, 6, 2, 2, "B");
        var seg = EditModeTestHelpers.Connect(a, b);

        var t1 = MakeTrainStub("T1");
        var t2 = MakeTrainStub("T2");

        Assert.That(seg.TryEnter(a, t1), Is.True, "1台目のA→B進入は許可されるはず");
        Assert.That(seg.TryEnter(a, t2), Is.False, "同一方向への2台目の進入は拒否されるはず(1閉塞1列車)");
    }

    [Test]
    public void TrackSegment_Block_AllowsOppositeDirectionSimultaneously()
    {
        var a = EditModeTestHelpers.MakeStation(new Vector3(-700, 0, 0), 90, 6, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(700, 0, 0), 90, 6, 2, 2, "B");
        var seg = EditModeTestHelpers.Connect(a, b);

        var t1 = MakeTrainStub("T1");
        var t2 = MakeTrainStub("T2");

        Assert.That(seg.TryEnter(a, t1), Is.True);
        Assert.That(seg.TryEnter(b, t2), Is.True, "逆方向(B→A)は別枠なので同時に許可されるはず");
    }

    [Test]
    public void TrackSegment_Block_ReleasesAfterLeave()
    {
        var a = EditModeTestHelpers.MakeStation(new Vector3(-700, 0, 0), 90, 6, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(700, 0, 0), 90, 6, 2, 2, "B");
        var seg = EditModeTestHelpers.Connect(a, b);

        var t1 = MakeTrainStub("T1");
        var t2 = MakeTrainStub("T2");

        seg.TryEnter(a, t1);
        Assert.That(seg.TryEnter(a, t2), Is.False, "解放前は拒否されること(前提確認)");

        seg.Leave(a, t1);
        Assert.That(seg.TryEnter(a, t2), Is.True, "Leave後は同方向でも進入できること");
    }

    static Train MakeTrainStub(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(BuildController.WorldRoot, false);
        return go.AddComponent<Train>();
    }
}
