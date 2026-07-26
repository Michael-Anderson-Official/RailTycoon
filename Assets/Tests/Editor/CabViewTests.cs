using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// 運転士目線の前面展望、運転台、ドアの開閉、ドアモニター。
// いずれも視覚専用で、シミュレーション(決定的な固定tick)には影響してはならない。
public class CabViewTests
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

    static Train Make(int formationIndex, out Station a, out Station b)
    {
        a = EditModeTestHelpers.MakeStation(Vector3.zero, 0, 10, 2, 2, "A");
        b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 2500), 0, 10, 2, 2, "B");
        EditModeTestHelpers.Connect(a, b);
        a.RebuildTrackVisual(); b.RebuildTrackVisual();

        int track; a.TryReserve(out track);
        var go = new GameObject("Train");
        go.transform.SetParent(BuildController.WorldRoot, false);
        var train = go.AddComponent<Train>();
        train.id = ++TrackNetwork.trainIdCounter;
        TrackNetwork.trains.Add(train);
        train.Init(TrainCatalog.Formations[formationIndex], new List<Station> { a, b },
            new List<int> { track, b.StopTracks[0] });
        return train;
    }

    // ---- 運転士目線 ----

    [Test]
    public void CabPose_IsInsideTheCabOnTheDriversSide()
    {
        var train = Make(0, out _, out _);
        train.CabPose(out var eye, out var fwd);

        // 先頭車の車体ローカルへ戻して確かめる
        Transform body = null;
        foreach (Transform car in train.transform) if (car.name == "Car0") { body = car; break; }
        Assert.That(body, Is.Not.Null);
        var local = body.InverseTransformPoint(eye);

        Assert.That(local.z, Is.LessThan(TrainCab.FrontZ),
            "鼻先より車内側にあること(z=" + local.z.ToString("F2") + ")");
        Assert.That(local.x, Is.LessThan(-0.2f),
            "進行方向左(運転席側)にあること(x=" + local.x.ToString("F2") + ")");
        Assert.That(local.y, Is.EqualTo(TrainCab.EyeY).Within(0.01f), "着席目線の高さであること");
        Assert.That(Vector3.Dot(fwd.normalized, body.forward), Is.GreaterThan(0.99f),
            "前を向いていること");
    }

    [Test]
    public void CabPose_IsAheadOfTheSeatBack()
    {
        // 目線が背もたれより後ろだと、背もたれが目の前に来て画面を塞ぐ
        var train = Make(0, out _, out _);
        train.CabPose(out var eye, out _);
        Transform body = null;
        foreach (Transform car in train.transform) if (car.name == "Car0") { body = car; break; }
        float z = body.InverseTransformPoint(eye).z;
        Assert.That(z, Is.GreaterThan(TrainCab.SeatZ - 0.3f),
            "座席の背もたれより前にあること(z=" + z.ToString("F2") + ")");
    }

    // ---- 運転台 ----

    [Test]
    public void HeadCar_HasACabInterior()
    {
        var train = Make(0, out _, out _);
        Transform body = null;
        foreach (Transform car in train.transform) if (car.name == "Car0") { body = car; break; }
        Assert.That(body.Find("CabInterior"), Is.Not.Null, "運転台の内装が作られること");
        Assert.That(body.Find("CabScreen"), Is.Not.Null, "計器・表示器が作られること");
        Assert.That(body.Find("DoorMonitor"), Is.Not.Null, "ドアモニターの画面が作られること");
    }

    [Test]
    public void CabLayout_DiffersByGeneration()
    {
        // ワンハンドル(液晶2画面)と2ハンドル(丸型計器3つ)で機器の構成が変わること
        int VertsOf(int fmIndex, string part)
        {
            TrackNetwork.Clear(); Services.Clear();
            EditModeTestHelpers.DestroyWorldRoot();
            var t = Make(fmIndex, out _, out _);
            foreach (Transform car in t.transform)
            {
                if (car.name != "Car0") continue;
                var mf = car.Find(part)?.GetComponent<MeshFilter>();
                return mf == null || mf.sharedMesh == null ? 0 : mf.sharedMesh.vertexCount;
            }
            return 0;
        }
        // Formations[0]=京王5000系(OneHandleLcd)、最後=名鉄6000系(TwoHandle)
        int lcd = VertsOf(0, "CabScreen");
        int twoHandle = VertsOf(TrainCatalog.Formations.Count - 1, "CabScreen");
        Assert.That(lcd, Is.GreaterThan(0));
        Assert.That(twoHandle, Is.GreaterThan(0));
        Assert.That(lcd, Is.Not.EqualTo(twoHandle),
            "世代で計器の構成が変わること(液晶2画面=" + lcd + " 丸型3つ=" + twoHandle + ")");
    }

    [Test]
    public void FrontFace_IsHiddenOnlyWhileRidingTheCab()
    {
        // 前面は開口が無いので、車窓の間だけ隠して前が見えるようにしている
        var train = Make(0, out _, out _);
        Transform body = null;
        foreach (Transform car in train.transform) if (car.name == "Car0") { body = car; break; }
        var face = body.Find("Face").GetComponent<MeshRenderer>();

        Assert.That(face.enabled, Is.True, "既定では前面が見えていること");
        train.SetFrontFaceVisible(false);
        Assert.That(face.enabled, Is.False, "車窓中は隠すこと");
        train.SetFrontFaceVisible(true);
        Assert.That(face.enabled, Is.True, "車窓を抜けたら戻すこと");
    }

    // ---- ドアの開閉 ----

    [Test]
    public void Doors_OpenOnlyOnThePlatformSideWhileStopped()
    {
        var train = Make(0, out var a, out _);
        for (int i = 0; i < 20; i++) train.UpdateDoors(0.5f);

        Assert.That(train.DoorOpenRatio, Is.GreaterThan(0.9f), "停車中は開くこと");
        Assert.That(train.OpenDoorSide, Is.Not.EqualTo(0), "開ける側が決まること");

        // 開いた側の扉だけが動いていること
        Transform body = null;
        foreach (Transform car in train.transform) if (car.name == "Car0") { body = car; break; }
        var opened = body.Find(TrainVisual.DoorLeafName(train.OpenDoorSide, 1));
        var closed = body.Find(TrainVisual.DoorLeafName(-train.OpenDoorSide, 1));
        Assert.That(opened, Is.Not.Null);
        Assert.That(closed, Is.Not.Null);
        Assert.That(Mathf.Abs(opened.localPosition.z), Is.GreaterThan(0.5f),
            "ホーム側の扉が引き込まれること");
        Assert.That(Mathf.Abs(closed.localPosition.z), Is.LessThan(0.01f),
            "反対側の扉は閉じたままであること");
    }

    [Test]
    public void Doors_AreClosedWhileRunning()
    {
        var train = Make(0, out _, out _);
        for (int i = 0; i < 20; i++) train.UpdateDoors(0.5f);
        Assert.That(train.DoorOpenRatio, Is.GreaterThan(0.9f), "まず開いていること");

        // 発車させて走行状態にする
        for (int i = 0; i < 4000; i++)
        {
            train.SimTick(Bootstrap.TickSeconds);
            train.PlaceCars();
            train.UpdateDoors(Bootstrap.TickSeconds);
            if (!train.IsDwelling && train.SpeedKmh > 20f) break;
        }
        Assert.That(train.IsDwelling, Is.False, "走行状態になったこと(テスト前提)");
        Assert.That(train.DoorOpenRatio, Is.EqualTo(0f).Within(0.01f), "走行中は閉じていること");
    }

    // ---- 影響の切り分けと後始末 ----

    [Test]
    public void DoorsAndMonitor_DoNotDisturbTheSimulation()
    {
        var train = Make(0, out var a, out var b);
        a.waiting[b] = 30;
        GameRandom.Seed(4242u);
        uint stateBefore = GameRandom.GetState();
        double moneyBefore = GameState.money;
        int waitingBefore = a.TotalWaiting;

        for (int i = 0; i < 60; i++) train.UpdateDoors(0.3f);

        Assert.That(GameRandom.GetState(), Is.EqualTo(stateBefore), "乱数が動かないこと");
        Assert.That(GameState.money, Is.EqualTo(moneyBefore).Within(1e-6), "資金が動かないこと");
        Assert.That(a.TotalWaiting, Is.EqualTo(waitingBefore), "待ち客が動かないこと");
    }

    [Test]
    public void DestroyingATrain_DoesNotLeakMonitorResources()
    {
        // RenderTextureとMaterialはネイティブ資源。GameObjectを消しても残るので、
        // 列車を撤去するたびに積み上がる(実装後レビューでCodex CLIが指摘)
        int Count<T>() where T : Object => Resources.FindObjectsOfTypeAll<T>().Length;

        // モニターは「車窓モード中」しか作られないので、CameraRigを立てて乗り込む
        var rigGo = new GameObject("Rig");
        var rig = rigGo.AddComponent<CameraRig>();
        // EditModeではAwakeが呼ばれないので、静的参照は自分で差す
        CameraRig.I = rig;
        try
        {
            int rtBefore = Count<RenderTexture>();
            int matBefore = Count<Material>();

            var train = Make(0, out _, out _);
            rig.cabTrain = train;
            for (int i = 0; i < 20; i++) train.UpdateDoors(0.5f);
            Assert.That(Count<RenderTexture>(), Is.GreaterThan(rtBefore),
                "車窓中はモニター用のRenderTextureが作られること(テスト前提)");

            train.DisposeVisuals();   // 撤去側(BuildController)と同じ手順
            Object.DestroyImmediate(train.gameObject);

            int rtAfter = Count<RenderTexture>();
            int matAfter = Count<Material>();
            Assert.That(rtAfter, Is.LessThanOrEqualTo(rtBefore),
                "撤去後にRenderTextureが残らないこと(前" + rtBefore + " 後" + rtAfter + ")");
            Assert.That(matAfter, Is.LessThanOrEqualTo(matBefore + 1),
                "撤去後にMaterialが積み上がらないこと(前" + matBefore + " 後" + matAfter + ")");
        }
        finally { CameraRig.I = null; Object.DestroyImmediate(rigGo); }
    }
}
