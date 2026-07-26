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

    // 停車中の車窓カメラを組み、標識板の中心が画面のどこに映るかを返す
    static Vector3 MarkerViewport(Station st, Train train, out float plateBottomY)
    {
        train.CabPose(out var pos, out var fwd);
        var camGo = new GameObject("Cab");
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = Fov;
        cam.aspect = ScreenW / ScreenH;
        camGo.transform.SetPositionAndRotation(pos,
            Quaternion.LookRotation((fwd + Vector3.down * LookDownY).normalized, Vector3.up));

        // この編成の停止位置目標(板)の中心を探す
        float nose = train.fm.cars * StationLayout.CarLength * 0.5f;
        var signs = st.transform.Find("StationSigns").GetComponent<MeshFilter>().sharedMesh;
        float bestD = float.MaxValue;
        Vector3 best = Vector3.zero, bestLocal = Vector3.zero;
        foreach (var v in signs.vertices)
        {
            if (v.y > 1.0f) continue;                       // 低い停車位置目標だけ
            float d = Mathf.Abs(v.z - nose) + Mathf.Abs(v.y - 0.55f) * 10f;
            if (d < bestD) { bestD = d; bestLocal = v; best = st.transform.TransformPoint(v); }
        }
        Assert.That(bestD, Is.LessThan(float.MaxValue), "停車位置目標が生成されていること");

        var centre = st.transform.TransformPoint(
            new Vector3(bestLocal.x, 0.55f, Mathf.Sign(bestLocal.z) * (nose + 7.0f)));
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

        float plateBottomY;
        var vp = MarkerViewport(st, train, out plateBottomY);

        Assert.That(vp.z, Is.GreaterThan(0f), "標識がカメラの前方にあること");
        Assert.That(plateBottomY, Is.GreaterThan(0f),
            "板の下端が画面内に収まること(下端y=" + plateBottomY.ToString("F3") + ")");
        Assert.That(vp.y, Is.LessThan(0.12f),
            "板が画面下端の近くにあること(中心y=" + vp.y.ToString("F3") + ")");
        Assert.That(vp.x, Is.InRange(0.0f, 1.0f),
            "板が横方向にも画面内へ収まること(x=" + vp.x.ToString("F3") + ")");
    }

    [Test]
    public void StopMarkers_ExistForEveryCarCountTheStationAccepts()
    {
        var (st, _) = Setup(10, 0);
        var signs = st.transform.Find("StationSigns").GetComponent<MeshFilter>().sharedMesh;

        // 期待する停止位置(両数ごと・両方向)にそれぞれ板があること
        foreach (var f in TrainCatalog.Formations)
        {
            if (f.cars > st.cars) continue;
            float nose = f.cars * StationLayout.CarLength * 0.5f;
            foreach (int sign in new[] { -1, 1 })
            {
                float want = sign * (nose + 7.0f);
                bool found = false;
                foreach (var v in signs.vertices)
                    if (v.y < 1.0f && Mathf.Abs(v.z - want) < 0.1f) { found = true; break; }
                Assert.That(found, Is.True,
                    f.cars + "両の停止位置目標(z=" + want.ToString("F1") + ")があること");
            }
        }
    }

    [Test]
    public void StopMarker_StaysBelowTheCarFloorAndOutsideTheRail()
    {
        // 車体の下へ収まる低い標識にしている。床下に収まり、レールにも当たらないこと
        var (st, _) = Setup(10, 0);
        var signs = st.transform.Find("StationSigns").GetComponent<MeshFilter>().sharedMesh;
        float floor = RailDimensions.RailTop + RailDimensions.VehicleFloorAboveRail;
        foreach (var v in signs.vertices)
        {
            if (v.y > 1.0f) continue;   // 停車位置目標だけを見る
            Assert.That(v.y, Is.LessThan(floor),
                "標識が車両の床面より低いこと(y=" + v.y.ToString("F2") + ")");
            float fromTrack = float.MaxValue;
            foreach (float t in st.layout.trackOffsets)
                fromTrack = Mathf.Min(fromTrack, Mathf.Abs(v.x - t));
            Assert.That(fromTrack, Is.GreaterThan(RailDimensions.HalfGauge + 0.05f),
                "標識がレールの外側にあること(線路中心から" + fromTrack.ToString("F2") + "m)");
        }
    }
}
