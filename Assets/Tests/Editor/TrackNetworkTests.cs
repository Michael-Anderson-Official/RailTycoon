using NUnit.Framework;
using UnityEngine;

// TrackNetworkの接続・非接続判定と到達可能性(Reachable)のEditModeテスト
public class TrackNetworkTests
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
    public void Connected_DirectlyLinkedStations_ReturnsTrue()
    {
        var a = EditModeTestHelpers.MakeStation(new Vector3(-400, 0, 0), 0, 6, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(500, 0, 120), 30, 6, 2, 2, "B");
        EditModeTestHelpers.Connect(a, b);

        Assert.That(TrackNetwork.Connected(a, b), Is.True);
        Assert.That(TrackNetwork.Connected(b, a), Is.True, "接続判定は対称のはず");
    }

    [Test]
    public void Connected_UnlinkedStations_ReturnsFalse()
    {
        var a = EditModeTestHelpers.MakeStation(new Vector3(-400, 0, 0), 0, 6, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(500, 0, 120), 30, 6, 2, 2, "B");
        // Connect()を呼ばない = 未接続

        Assert.That(TrackNetwork.Connected(a, b), Is.False);
    }

    [Test]
    public void Connected_ThirdStation_NotDirectlyLinked_ReturnsFalse()
    {
        var a = EditModeTestHelpers.MakeStation(new Vector3(-600, 0, 0), 0, 6, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(400, 0, 120), 30, 6, 2, 2, "B");
        var c = EditModeTestHelpers.MakeStation(new Vector3(1300, 0, -160), -20, 6, 2, 2, "C");
        EditModeTestHelpers.Connect(a, b);
        EditModeTestHelpers.Connect(b, c);

        // A-Cは直結ではない(BuildController側の経路作成もConnectedによる直結判定のみを許可する)
        Assert.That(TrackNetwork.Connected(a, c), Is.False);
    }

    [Test]
    public void Reachable_ThroughChain_IncludesAllConnectedStations()
    {
        var a = EditModeTestHelpers.MakeStation(new Vector3(-600, 0, 0), 0, 6, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(400, 0, 120), 30, 6, 2, 2, "B");
        var c = EditModeTestHelpers.MakeStation(new Vector3(1300, 0, -160), -20, 6, 2, 2, "C");
        EditModeTestHelpers.Connect(a, b);
        EditModeTestHelpers.Connect(b, c);

        var fromA = TrackNetwork.Reachable(a);
        Assert.That(fromA, Has.Member(b));
        Assert.That(fromA, Has.Member(c), "A-CはA-B-C経由で到達可能なはず(乗客の行き先候補)");
        Assert.That(fromA, Has.No.Member(a), "自分自身は到達可能集合に含まれない");
    }

    [Test]
    public void Reachable_IsolatedStation_ReturnsEmpty()
    {
        var a = EditModeTestHelpers.MakeStation(new Vector3(-600, 0, 0), 0, 6, 2, 2, "A");
        var isolated = EditModeTestHelpers.MakeStation(new Vector3(3000, 0, 3000), 0, 6, 2, 2, "Isolated");

        Assert.That(TrackNetwork.Reachable(isolated), Is.Empty);
    }
}
