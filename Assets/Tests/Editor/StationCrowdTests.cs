using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// ホームに立つ人。視覚専用の層なので、シミュレーション(決定的な固定tick)には
// 一切影響してはならない。人数は待ち客から導出し、間引かず全員ぶん持つ。
public class StationCrowdTests
{
    [SetUp]
    public void SetUp()
    {
        TrackNetwork.Clear();
        Services.Clear();
        GameRandom.Seed(777u);
    }

    [TearDown]
    public void TearDown()
    {
        EditModeTestHelpers.DestroyWorldRoot();
        TrackNetwork.Clear();
        Services.Clear();
    }

    static Station MakeConnectedStation(int faces, int lines)
    {
        var a = EditModeTestHelpers.MakeStation(Vector3.zero, 0, 10, faces, lines, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 2400), 0, 10, faces, lines, "B");
        EditModeTestHelpers.Connect(a, b);
        a.RebuildTrackVisual(); b.RebuildTrackVisual();
        return a;
    }

    // 待ち客をn人にする(行き先は問わない)
    static void SetWaiting(Station st, Station dest, int n)
    {
        st.waiting.Clear();
        if (n > 0) st.waiting[dest] = n;
    }

    // 人数が追いつくまで更新を回す(1回の更新で足す数に上限があるため)
    static void Settle(Station st, int steps = 40)
    {
        for (int i = 0; i < steps; i++) st.UpdateCrowd(0.5f);
    }

    [Test]
    public void CrowdSize_FollowsTheWaitingCount()
    {
        var a = MakeConnectedStation(2, 2);
        var b = TrackNetwork.stations[1];

        SetWaiting(a, b, 0);
        Settle(a);
        Assert.That(a.Crowd.Count, Is.EqualTo(0), "待ち客0なら誰もいないこと");

        SetWaiting(a, b, 25);
        Settle(a);
        Assert.That(a.Crowd.Count, Is.EqualTo(25), "待ち客と同じ人数になること");

        SetWaiting(a, b, 7);
        Settle(a);
        Assert.That(a.Crowd.Count, Is.EqualTo(7), "減ったぶんは消えること");
    }

    [Test]
    public void CrowdSize_IsNotThinnedOutEvenWhenCrowded()
    {
        // 「上限なしで全員描く」方針。数百人でも人数ぶん持つこと
        var a = MakeConnectedStation(2, 4);
        var b = TrackNetwork.stations[1];
        SetWaiting(a, b, 600);
        Settle(a, 200);
        Assert.That(a.Crowd.Count, Is.EqualTo(600),
            "待ち客600人なら600人ぶん持つこと(実際" + a.Crowd.Count + "人)");
    }

    [Test]
    public void People_StandOnThePlatformAndClearOfTheTrackSideBand()
    {
        var a = MakeConnectedStation(1, 2);   // 島式
        var b = TrackNetwork.stations[1];
        SetWaiting(a, b, 60);
        Settle(a);

        var root = a.transform.Find("Crowd");
        Assert.That(root, Is.Not.Null, "人の描画物が作られること");

        float platLen = a.cars * StationLayout.CarLength;
        int checkedCount = 0;
        foreach (Transform person in root)
        {
            if (person.name != "Person" || !person.gameObject.activeSelf) continue;
            checkedCount++;
            var p = person.localPosition;
            Assert.That(p.y, Is.EqualTo(RailDimensions.PlatformTop).Within(0.01f),
                "ホーム面の上に立つこと");
            Assert.That(Mathf.Abs(p.z), Is.LessThanOrEqualTo(platLen * 0.5f + 0.5f),
                "ホームの長さの中に収まること");

            // どこかのホームの、線路側の帯より内側にいること
            bool ok = false;
            foreach (var pl in a.layout.platforms)
            {
                float visualW = Mathf.Max(2.6f, pl.y - 0.02f);
                if (Mathf.Abs(p.x - pl.x) <= visualW * 0.5f - 1.3f) { ok = true; break; }
            }
            Assert.That(ok, Is.True,
                "線路側の帯(警戒線・点字ブロック)へ出ないこと(x=" + p.x.ToString("F2") + ")");
        }
        Assert.That(checkedCount, Is.GreaterThan(0), "個別の人が描かれていること(テスト前提)");
    }

    [Test]
    public void Crowd_DoesNotDisturbTheSimulation()
    {
        // 人の更新でGameRandomや待ち客・資金が動いてはいけない
        var a = MakeConnectedStation(2, 2);
        var b = TrackNetwork.stations[1];
        SetWaiting(a, b, 40);
        Settle(a);

        GameRandom.Seed(4242u);
        uint stateBefore = GameRandom.GetState();
        double moneyBefore = GameState.money;
        int waitingBefore = a.TotalWaiting;

        for (int i = 0; i < 50; i++) a.UpdateCrowd(0.5f);

        Assert.That(GameRandom.GetState(), Is.EqualTo(stateBefore),
            "人の更新で乱数の状態が変わらないこと");
        Assert.That(GameState.money, Is.EqualTo(moneyBefore).Within(1e-6),
            "資金が変わらないこと");
        Assert.That(a.TotalWaiting, Is.EqualTo(waitingBefore), "待ち客が変わらないこと");
    }

    [Test]
    public void Boarding_MakesPeopleWalkToTheDoorsAndVanish()
    {
        var a = MakeConnectedStation(2, 2);
        var b = TrackNetwork.stations[1];
        SetWaiting(a, b, 30);
        Settle(a);
        Assert.That(a.Crowd.Count, Is.EqualTo(30));

        // 10人が乗車。シミュレーション側の待ち客も同時に減る
        SetWaiting(a, b, 20);
        a.NotifyBoarded(a.StopTracks[0], 10);
        Assert.That(a.Crowd.Count, Is.EqualTo(30),
            "乗車の瞬間はまだホーム上にいること(ドアまで歩かせる)");

        Settle(a, 200);
        Assert.That(a.Crowd.Count, Is.EqualTo(20), "歩き終えたら消えること");
    }

    [Test]
    public void Alighting_AddsPeopleThatLeaveThePlatform()
    {
        var a = MakeConnectedStation(2, 2);
        var b = TrackNetwork.stations[1];
        SetWaiting(a, b, 5);
        Settle(a);

        a.NotifyAlighted(a.StopTracks[0], 12);
        Assert.That(a.Crowd.Count, Is.GreaterThan(5), "降車客が現れること");

        Settle(a, 300);
        Assert.That(a.Crowd.Count, Is.EqualTo(5), "降車客は去って待ち客だけが残ること");
    }

    [Test]
    public void Crowd_ComesBackAfterBeingFarAway()
    {
        // 遠ざかると描画を止めるが、戻ってきたら必ず現れること
        var a = MakeConnectedStation(2, 2);
        var b = TrackNetwork.stations[1];
        SetWaiting(a, b, 20);

        // 他のテストが残したカメラをCamera.mainが拾うと距離判定が狂うので、
        // 先に片付けてから自前のカメラだけを立てる
        foreach (var other in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            if (other != null) Object.DestroyImmediate(other.gameObject);

        var camGo = new GameObject("TestCam") { tag = "MainCamera" };
        camGo.AddComponent<Camera>();
        try
        {
            Assert.That(Camera.main, Is.Not.Null, "テスト用カメラがCamera.mainになること");
            camGo.transform.position = new Vector3(0, 30f, -60f);   // 近い
            Settle(a);
            var root = a.transform.Find("Crowd");
            Assert.That(root, Is.Not.Null);
            Assert.That(root.gameObject.activeSelf, Is.True, "近ければ表示されること");

            camGo.transform.position = new Vector3(0, 200f, -5000f); // 遠い
            a.UpdateCrowd(0.5f);
            Assert.That(root.gameObject.activeSelf, Is.False, "遠ければ描かないこと");

            camGo.transform.position = new Vector3(0, 30f, -60f);   // 戻る
            a.UpdateCrowd(0.5f);
            Assert.That(root.gameObject.activeSelf, Is.True, "戻ったら再び表示されること");
        }
        finally { Object.DestroyImmediate(camGo); }
    }

    [Test]
    public void PreviewStation_HasNoCrowd()
    {
        var a = MakeConnectedStation(2, 2);
        var b = TrackNetwork.stations[1];
        SetWaiting(a, b, 20);
        Settle(a);

        var previewGo = new GameObject("Preview");
        previewGo.transform.SetParent(BuildController.WorldRoot, false);
        var preview = previewGo.AddComponent<Station>();
        preview.preview = true;
        preview.cars = 6; preview.faces = 2; preview.lines = 2;
        preview.Build();
        for (int i = 0; i < 20; i++) preview.UpdateCrowd(0.1f);
        Assert.That(preview.transform.Find("Crowd"), Is.Null, "建設プレビューには人を出さないこと");
    }
}
