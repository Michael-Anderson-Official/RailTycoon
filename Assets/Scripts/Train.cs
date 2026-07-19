using System.Collections.Generic;
using UnityEngine;

// 列車。route(駅列)を往復し、各駅で停車線を確保してから発車する。
// 発車時に到着駅の線を予約するので、駅間で詰まって止まることはない
public class Train : MonoBehaviour
{
    public TrainCatalog.Formation fm;
    public List<Station> route;

    public int idx;      // 現在(または直前に発った)駅のindex
    public int dir = 1;  // 進行方向(route上)
    public int curTrack; // 現在駅で占有中の線

    enum St { Dwell, Run }
    St state = St.Dwell;
    float dwellT = 5f;

    List<Vector3> path;
    float[] cum;
    float s, v;
    Station departStation;
    int departTrack;
    float releaseS;
    bool released;

    List<Transform> carTs;
    readonly List<(Station dest, int count, Vector3 boardPos)> onboard = new List<(Station, int, Vector3)>();
    int onboardCount;

    const float Accel = 0.9f, Decel = 1.0f;
    const float CarGap = 0.7f;

    public float HalfTrain => fm.cars * StationLayout.CarLength * 0.5f;

    public void Init(TrainCatalog.Formation formation, List<Station> stations, int startTrack,
        int startIdx = 0, int dirInit = 1)
    {
        fm = formation;
        route = stations;
        idx = Mathf.Clamp(startIdx, 0, route.Count - 1);
        dir = idx >= route.Count - 1 ? -1 : (idx <= 0 ? 1 : (dirInit >= 0 ? 1 : -1));
        curTrack = startTrack;
        carTs = TrainVisual.BuildCars(transform, fm);
        // 開始駅のホームに据え付け
        var st = route[idx];
        float h = HalfTrain + 2f;
        path = RailKit.Chaikin(new List<Vector3>
        {
            st.TrackWorldPoint(startTrack, -h),
            st.TrackWorldPoint(startTrack, 0),
            st.TrackWorldPoint(startTrack, h),
        }, 1);
        cum = RailKit.Cumulative(path);
        s = cum[cum.Length - 1];
        PlaceCars();
        state = St.Dwell;
        dwellT = 8f;
    }

    void Update()
    {
        float dt = Time.deltaTime * GameState.timeScale;
        if (state == St.Dwell)
        {
            dwellT -= dt;
            if (dwellT <= 0) TryDepart();
        }
        else
        {
            float total = cum[cum.Length - 1];
            float rem = Mathf.Max(0, total - s);
            float vAllow = Mathf.Sqrt(2f * Decel * rem);
            float vmax = fm.type.maxSpeedKmh / 3.6f;
            v = Mathf.Min(v + Accel * dt, vmax, vAllow);
            s += v * dt;
            if (!released && s > releaseS)
            {
                released = true;
                departStation.Release(departTrack);
            }
            if (s >= total - 0.05f)
            {
                s = total;
                Arrive();
            }
            PlaceCars();
        }
    }

    void TryDepart()
    {
        var cur = route[idx];
        int next = idx + dir;
        if (next < 0 || next >= route.Count)
        {
            dir = -dir;
            next = idx + dir;
        }
        var to = route[next];
        var seg = TrackNetwork.Find(cur, to);
        if (seg == null) { dwellT = 5f; return; }
        int destTrack;
        if (!to.TryReserve(out destTrack)) { dwellT = 3f; return; } // 到着線が空くまで待つ

        int boarded = Board(cur);
        cur.OnDeparture(boarded);

        int exitSign = seg.SignAt(cur);
        int enterSign = seg.SignAt(to);
        path = BuildLeg(cur, curTrack, exitSign, to, destTrack, enterSign, HalfTrain);
        cum = RailKit.Cumulative(path);
        // 経路先頭は列車の尻尾位置。先頭車はそこから編成長ぶん先にいる
        s = HalfTrain * 2f;
        v = 0;
        departStation = cur;
        departTrack = curTrack;
        released = false;
        releaseS = cur.HalfLen + StationLayout.ThroatLen + fm.cars * StationLayout.CarLength + 10f;
        curTrack = destTrack;
        idx = next;
        state = St.Run;
    }

    void Arrive()
    {
        if (!released) { released = true; departStation.Release(departTrack); }
        var st = route[idx];
        // 降車と運賃収受
        int off = 0;
        for (int i = onboard.Count - 1; i >= 0; i--)
        {
            if (onboard[i].dest != st) continue;
            float km = Vector3.Distance(onboard[i].boardPos, st.transform.position) / 1000f;
            GameState.EarnFare(onboard[i].count, km);
            off += onboard[i].count;
            onboardCount -= onboard[i].count;
            onboard.RemoveAt(i);
        }
        st.UpdateLabel();
        state = St.Dwell;
        dwellT = 25f;
        v = 0;
    }

    int Board(Station st)
    {
        int avail = fm.Capacity - onboardCount;
        if (avail <= 0) return 0;
        int total = 0;
        var keys = new List<Station>(st.waiting.Keys);
        foreach (var dest in keys)
        {
            if (avail <= 0) break;
            if (!route.Contains(dest) || dest == st) continue;
            int take = Mathf.Min(st.waiting[dest], avail);
            if (take <= 0) continue;
            st.waiting[dest] -= take;
            if (st.waiting[dest] <= 0) st.waiting.Remove(dest);
            onboard.Add((dest, take, st.transform.position));
            onboardCount += take;
            avail -= take;
            total += take;
        }
        return total;
    }

    void PlaceCars() => PlaceCarsStatic(carTs, path, cum, s);

    // エディタのSnapshotでも使う車両配置(先頭車の弧長sから後方へ並べる)
    public static void PlaceCarsStatic(List<Transform> cars, List<Vector3> path, float[] cum, float s)
    {
        if (cars == null) return;
        float carLen = StationLayout.CarLength;
        for (int i = 0; i < cars.Count; i++)
        {
            float c = s - carLen * 0.5f - i * (carLen + CarGap);
            Vector3 pf, pr, f;
            RailKit.Sample(path, cum, c + 7f, out pf, out f);
            RailKit.Sample(path, cum, c - 7f, out pr, out f);
            var mid = (pf + pr) * 0.5f + Vector3.up * 0.2f;
            var fwd = pf - pr;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            cars[i].SetPositionAndRotation(mid, Quaternion.LookRotation(fwd.normalized, Vector3.up));
        }
    }

    // 駅fromの線fromTrackから駅toの線toTrackまでの走行経路を組む
    public static List<Vector3> BuildLeg(Station from, int fromTrack, int exitSign,
        Station to, int toTrack, int enterSign, float halfTrain)
    {
        var pts = new List<Vector3>();
        float hf = from.HalfLen, tf = StationLayout.ThroatLen;
        float offF = from.layout.trackOffsets[fromTrack];
        pts.Add(from.TrackWorldPoint(fromTrack, -exitSign * halfTrain));
        pts.Add(from.TrackWorldPoint(fromTrack, 0));
        pts.Add(from.TrackWorldPoint(fromTrack, exitSign * hf));
        pts.Add(from.transform.TransformPoint(new Vector3(Mathf.Sign(offF) * 2.3f, 0, exitSign * (hf + tf * 0.6f))));

        var endA = from.End(exitSign);
        var endB = to.End(enterSign);
        var d = (endB - endA).normalized;
        var right = Vector3.Cross(Vector3.up, d).normalized;
        var ml = right * -2.3f; // 左側通行
        float dist = Vector3.Distance(endA, endB);
        int n = Mathf.Max(2, Mathf.CeilToInt(dist / 40f) + 1);
        for (int i = 0; i < n; i++)
            pts.Add(Vector3.Lerp(endA, endB, i / (float)(n - 1)) + ml);

        float ht = to.HalfLen;
        float offT = to.layout.trackOffsets[toTrack];
        pts.Add(to.transform.TransformPoint(new Vector3(Mathf.Sign(offT) * 2.3f, 0, enterSign * (ht + tf * 0.6f))));
        pts.Add(to.TrackWorldPoint(toTrack, enterSign * ht));
        pts.Add(to.TrackWorldPoint(toTrack, 0));
        pts.Add(to.TrackWorldPoint(toTrack, -enterSign * halfTrain));
        return RailKit.Chaikin(pts, 2);
    }
}
