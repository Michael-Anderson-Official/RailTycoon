using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// 停車位置目標。運転士が先頭を合わせる線路脇の標識で、
// 「前面展望の画面下端に来たら停止位置」という目安になるよう置いている。
// 位置は車窓カメラ(Train.CabPose)の幾何から逆算した値なので、
// カメラの目線高さ・前方オフセット・画角が変わると成立しなくなる。ここで固定する。
public class StopMarkerTests
{
    // 実機 iPhone 17 Pro の縦画面
    const float ScreenW = 402f, ScreenH = 874f;
    const float Fov = 60f;              // CameraRigはCameraの既定FOVをそのまま使う
    const float LookDownY = 0.06f;      // CameraRigの車窓が加える下向き成分
    const float SafeBottomPx = 34f;     // ホームインジケータぶん(iPhoneでの想定最大)
    // 運転台(コンソール)が画面下を占める割合は**定数で書かない**。運転台の
    // 寸法を変えるたびに古くなり、標識が運転台の裏に隠れても気づけないため、
    // 実際の運転台の上端を同じカメラで投影して求める

    // 車窓でも下部ナビが画面下端を覆う。その上端が画面高のどこに来るか。
    // パネル高と同じくCanvasScalerの参照単位で計算する必要がある
    static float ToolbarTopFraction()
    {
        float scale = Mathf.Sqrt((ScreenW / UIController.ReferenceResolution.x)
                               * (ScreenH / UIController.ReferenceResolution.y));
        float canvasH = ScreenH / scale;
        float toolbarTop = UIController.PortraitToolbarHeight + SafeBottomPx / scale;
        return toolbarTop / canvasH;
    }

    [TearDown]
    public void TearDown()
    {
        EditModeTestHelpers.DestroyWorldRoot();
        TrackNetwork.Clear();
    }

    static (Station a, Train train) Setup(int stationCars, int formationIndex)
    {
        var a = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 0), 0, stationCars, 1, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(0, 0, 2500), 0, stationCars, 1, 2, "B");
        EditModeTestHelpers.Connect(a, b);
        a.RebuildTrackVisual(); b.RebuildTrackVisual();

        int track;
        a.TryReserve(out track);
        var go = new GameObject("Train");
        go.transform.SetParent(BuildController.WorldRoot, false);
        var train = go.AddComponent<Train>();
        train.id = ++TrackNetwork.trainIdCounter;
        TrackNetwork.trains.Add(train);
        train.Init(TrainCatalog.Formations[formationIndex], new List<Station> { a, b },
            new List<int> { track, b.StopTracks[0] });
        return (a, train);
    }

    // 停車中の車窓カメラを組み、標識板の中心が画面のどこに映るかを返す。
    // cabTopには運転台の上端が映る高さ(画面下からの割合)を返す
    static Vector3 MarkerViewport(Station st, Train train, out float plateBottomY, out float cabTop)
    {
        train.CabPose(out var pos, out var fwd);
        var camGo = new GameObject("Cab");
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = Fov;
        cam.aspect = ScreenW / ScreenH;
        camGo.transform.SetPositionAndRotation(pos,
            Quaternion.LookRotation((fwd + Vector3.down * LookDownY).normalized, Vector3.up));

        // 運転台の上端(先頭車の車体ローカル→ワールド→画面)
        Transform body = null;
        foreach (Transform car in train.transform) if (car.name == "Car0") { body = car; break; }
        Assert.That(body, Is.Not.Null, "先頭車があること");
        cabTop = cam.WorldToViewportPoint(body.TransformPoint(TrainCab.ConsoleTopLocal)).y;

        // この編成の停止位置目標(板)を**実際に生成されたメッシュから**探す。
        // 期待位置を定数で書いてしまうと、標識が動いてもテストが通ってしまう
        float nose = train.fm.cars * StationLayout.CarLength * 0.5f;
        var signs = st.transform.Find("StopMarkers").GetComponent<MeshFilter>().sharedMesh;
        float bestD = float.MaxValue;
        Vector3 bestLocal = Vector3.zero;
        foreach (var v in signs.vertices)
        {
            if (v.z < nose) continue;                       // 進行方向の前にあるものだけ
            float d = v.z - nose;
            if (d < bestD) { bestD = d; bestLocal = v; }
        }
        Assert.That(bestD, Is.LessThan(float.MaxValue), "停車位置目標が生成されていること");

        // 見つけた頂点のx/zをそのまま使い、yは板の中心へ寄せる(頂点は上下端にある)
        var centre = st.transform.TransformPoint(
            new Vector3(bestLocal.x, Station.MarkerPlateY, bestLocal.z));
        var vpCentre = cam.WorldToViewportPoint(centre);
        var vpBottom = cam.WorldToViewportPoint(
            centre + Vector3.down * 0.15f);   // 板の高さ0.30の半分
        plateBottomY = vpBottom.y;
        Object.DestroyImmediate(camGo);
        return vpCentre;
    }

    [TestCase(10, 0)]   // 京王5000系10両
    [TestCase(8, 2)]    // 名鉄2000系8両
    [TestCase(6, 3)]    // 名鉄2200系6両
    public void StopMarker_SitsAtTheBottomEdgeOfTheCabViewWhenStopped(int stationCars, int fmIndex)
    {
        var (st, train) = Setup(stationCars, fmIndex);
        Assert.That(train.fm.cars, Is.LessThanOrEqualTo(stationCars), "テスト前提: 駅に収まる編成");

        float plateBottomY, cabTop;
        var vp = MarkerViewport(st, train, out plateBottomY, out cabTop);

        float barTop = ToolbarTopFraction();
        Assert.That(vp.z, Is.GreaterThan(0f), "標識がカメラの前方にあること");
        Assert.That(plateBottomY, Is.GreaterThan(barTop + 0.02f),
            "板が下部ナビ(上端" + barTop.ToString("F3") + ")の裏に隠れないこと" +
            "(板の下端y=" + plateBottomY.ToString("F3") + ")");
        // 運転士目線にしてから、画面下は運転台(コンソール)が占める。その上に出ること
        Assert.That(plateBottomY, Is.GreaterThan(cabTop + 0.01f),
            "板が運転台(上端" + cabTop.ToString("F3") + ")に隠れないこと" +
            "(板の下端y=" + plateBottomY.ToString("F3") + ")");
        // 運転台のすぐ上が定位置。画面の真ん中まで上がってきたら置き方がおかしい
        Assert.That(vp.y, Is.LessThan(0.52f),
            "それでも画面下寄りに留まること(中心y=" + vp.y.ToString("F3") + ")");
        Assert.That(vp.x, Is.InRange(0.0f, 1.0f),
            "板が横方向にも画面内へ収まること(x=" + vp.x.ToString("F3") + ")");
    }

    [Test]
    public void StopMarkers_ExistForEveryCarCountTheStationAccepts()
    {
        var (st, _) = Setup(10, 0);
        var signs = st.transform.Find("StopMarkers").GetComponent<MeshFilter>().sharedMesh;

        // 期待する停止位置(両数ごと・両方向)にそれぞれ板があること
        foreach (var f in TrainCatalog.Formations)
        {
            if (f.cars > st.cars) continue;
            float nose = f.cars * StationLayout.CarLength * 0.5f;
            foreach (int sign in new[] { -1, 1 })
            {
                float want = sign * (nose + 8.5f);
                bool found = false;
                foreach (var v in signs.vertices)
                    if (Mathf.Abs(v.z - want) < 0.1f) { found = true; break; }
                Assert.That(found, Is.True,
                    f.cars + "両の停止位置目標(z=" + want.ToString("F1") + ")があること");
            }
        }
    }

    [Test]
    public void InWorldText_IsDepthTestedSoItDoesNotShowThroughSolidThings()
    {
        // TextMeshの既定マテリアルはZTest Always。実景の文字(停車位置目標の両数・
        // ホームの駅名標)に使うと、運転台や車体を突き抜けて数字が浮く。
        // 文字色はフォントのアルファのみのアトラスから取れないため、無地シェーダ
        // (Sprites/Default)でも代用できない(文字が真っ黒になる)
        var (st, _) = Setup(10, 0);
        int checkedCount = 0;
        foreach (Transform ch in st.transform)
        {
            if (!ch.name.StartsWith("StopMarkerText_") && !ch.name.StartsWith("StationSignText_"))
                continue;
            var mat = ch.GetComponent<MeshRenderer>().sharedMaterial;
            Assert.That(mat, Is.Not.Null, ch.name + " にマテリアルが差さっていること");
            Assert.That(mat.shader.name, Is.EqualTo("RailTycoon/TextDepth"),
                ch.name + " が奥行きを見るシェーダーであること(実際は" + mat.shader.name + ")");
            Assert.That(mat.mainTexture, Is.Not.Null,
                ch.name + " にフォントのアトラスが差さっていること");
            checkedCount++;
        }
        Assert.That(checkedCount, Is.GreaterThan(0), "実景の文字が生成されていること(テスト前提)");
    }

    [Test]
    public void MapLabel_StaysOnTop()
    {
        // 一方、地図の駅名は地形や建物に隠れては困る。従来どおり手前に出すこと
        var (st, _) = Setup(10, 0);
        var label = st.transform.Find("Label");
        Assert.That(label, Is.Not.Null);
        Assert.That(label.GetComponent<MeshRenderer>().sharedMaterial.shader.name,
            Is.Not.EqualTo("RailTycoon/TextDepth"), "地図の駅名は手前に出したままにすること");
    }

    [Test]
    public void StopMarker_ClearsTheCarBody()
    {
        // 目の高さに掲げる標識なので、**車体断面の外**に無いと通過する列車が当たる。
        // 自分より長い編成は短い編成の停止位置を通り越すため、必ず起きる
        var (st, _) = Setup(10, 0);
        var signs = st.transform.Find("StopMarkers").GetComponent<MeshFilter>().sharedMesh;
        int checkedCount = 0;
        foreach (var v in signs.vertices)
        {
            float fromTrack = float.MaxValue;
            foreach (float t in st.layout.trackOffsets)
                fromTrack = Mathf.Min(fromTrack, Mathf.Abs(v.x - t));
            Assert.That(fromTrack, Is.GreaterThan(RailDimensions.CarBodyHalfWidth + 0.1f),
                "標識が車体断面の外にあること(線路中心から" + fromTrack.ToString("F2") +
                "m / 高さ" + v.y.ToString("F2") + "m)");
            checkedCount++;
        }
        Assert.That(checkedCount, Is.GreaterThan(0), "停車位置目標が生成されていること(テスト前提)");
    }
}
