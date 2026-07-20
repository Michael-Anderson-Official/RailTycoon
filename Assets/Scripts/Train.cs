using System.Collections.Generic;
using UnityEngine;

// 列車。route(駅列)を往復し、各駅で停車線を確保してから発車する。
// 発車時に到着駅の線を予約するので、駅間で詰まって止まることはない
public class Train : MonoBehaviour
{
    public TrainCatalog.Formation fm;
    public List<Station> route;
    public List<int> routeTracks; // 各停車駅で入る番線(trackIdx)。routeと同じ長さ
    public List<int> lineIds = new List<int>(); // 配属先の運行系統ID列(運用。順に走る)
    public bool cyclic;           // 経路末尾が先頭に直結するなら巡回、そうでなければ折返し

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
    TrackSegment curSeg;      // 走行中の閉塞区間
    Station curSegFrom;

    List<Transform> carTs;
    readonly List<(Station dest, int count, Vector3 boardPos)> onboard = new List<(Station, int, Vector3)>();
    int onboardCount;

    const float CarGap = 0.7f;

    public float HalfTrain => fm.cars * StationLayout.CarLength * 0.5f;

    public void Init(TrainCatalog.Formation formation, List<Station> stations, List<int> tracks,
        int startIdx = 0, int dirInit = 1)
    {
        fm = formation;
        route = stations;
        routeTracks = tracks;
        // 経路末尾が先頭に直結していれば巡回(行き/帰りで別パターン等)。無ければ折返し
        cyclic = route.Count >= 2 && TrackNetwork.Connected(route[route.Count - 1], route[0]);
        idx = Mathf.Clamp(startIdx, 0, route.Count - 1);
        dir = idx >= route.Count - 1 ? -1 : (idx <= 0 ? 1 : (dirInit >= 0 ? 1 : -1));
        int startTrack = routeTracks[idx];
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
            float vAllow = Mathf.Sqrt(2f * fm.type.Decel * rem);
            float vmax = fm.type.maxSpeedKmh / 3.6f;
            // 実車の加速は速度が上がると鈍る(定出力域+走行抵抗)。起動加速度に
            // 1-(v/vmax)^2 を掛けて高速域で頭打ちさせる(低速はキビキビ)
            float r = vmax > 0.1f ? v / vmax : 0f;
            float a = fm.type.Accel * Mathf.Max(0.08f, 1f - r * r);
            v = Mathf.Min(v + a * dt, vmax, vAllow);
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
        int next;
        if (cyclic)
        {
            dir = 1;
            next = (idx + 1) % route.Count;   // 末尾→先頭へループ
        }
        else
        {
            next = idx + dir;
            if (next < 0 || next >= route.Count)
            {
                dir = -dir;
                next = idx + dir;
            }
        }
        var to = route[next];
        var seg = TrackNetwork.Find(cur, to);
        if (seg == null) { dwellT = 5f; return; }
        int exitSign = seg.SignAt(cur);
        int enterSign = seg.SignAt(to);
        // 到着駅は指定番線を確保(空くまで発車を待つ)。指定が無ければ左側優先
        int destTrack;
        int wantTrack = (routeTracks != null && next < routeTracks.Count) ? routeTracks[next] : -1;
        if (wantTrack >= 0)
        {
            if (!to.TryReserveSpecific(wantTrack)) { dwellT = 2.5f; return; }
            destTrack = wantTrack;
        }
        else if (!to.TryReserveFor(enterSign, out destTrack)) { dwellT = 3f; return; }
        // 閉塞: 同一方向の駅間に先行列車がいる間は出発できない
        if (!seg.TryEnter(cur, this))
        {
            to.Release(destTrack);
            dwellT = 3f;
            return;
        }

        int boarded = Board(cur);
        cur.OnDeparture(boarded);
        path = BuildLeg(cur, curTrack, exitSign, to, destTrack, enterSign, HalfTrain);
        cum = RailKit.Cumulative(path);
        // 経路先頭は列車の尻尾位置。先頭車はそこから編成長ぶん先にいる
        s = HalfTrain * 2f;
        v = 0;
        departStation = cur;
        departTrack = curTrack;
        released = false;
        releaseS = cur.HalfLen + StationLayout.ThroatLen + fm.cars * StationLayout.CarLength + 10f;
        curSeg = seg;
        curSegFrom = cur;
        curTrack = destTrack;
        idx = next;
        state = St.Run;
    }

    void Arrive()
    {
        if (!released) { released = true; departStation.Release(departTrack); }
        if (curSeg != null)
        {
            curSeg.Leave(curSegFrom, this); // 閉塞解放
            curSeg = null;
        }
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

    public bool RouteHas(Station st) => route != null && route.Contains(st);

    // 撤去時の払い戻し額
    public double RefundValue => fm.CostYen * 0.5;

    // 保有中の予約(閉塞・発車駅の線・現在/到着駅の線)を全て解放。撤去前に呼ぶ
    public void ReleaseAll()
    {
        if (curSeg != null) { curSeg.Leave(curSegFrom, this); curSeg = null; }
        if (!released && departStation != null) departStation.Release(departTrack);
        released = true;
        if (route != null && idx >= 0 && idx < route.Count) route[idx].Release(curTrack);
    }

    // 線路網が変わった(駅の建て替え等)あと、現在(直前)駅に停車状態で復帰する。
    // 予約を取り直し、番線を有効値へ整合する
    public void ResyncToNetwork()
    {
        if (curSeg != null) { curSeg.Leave(curSegFrom, this); curSeg = null; }
        if (!released && departStation != null) departStation.Release(departTrack);
        released = true;

        idx = Mathf.Clamp(idx, 0, route.Count - 1);
        for (int i = 0; i < route.Count; i++)
        {
            int tr = (routeTracks != null && i < routeTracks.Count) ? routeTracks[i] : -1;
            if (tr < 0 || tr >= route[i].occupied.Length || route[i].PlatformNumberOf(tr) <= 0)
                SetRouteTrack(i, route[i].StopTracks[0]);
        }
        var st = route[idx];
        int track = routeTracks[idx];
        st.Release(track);
        if (!st.TryReserveSpecific(track))
        {
            int alt;
            if (st.TryReserve(out alt)) { track = alt; routeTracks[idx] = alt; }
        }
        curTrack = track;
        float h = HalfTrain + 2f;
        path = RailKit.Chaikin(new List<Vector3>
        {
            st.TrackWorldPoint(track, -h),
            st.TrackWorldPoint(track, 0),
            st.TrackWorldPoint(track, h),
        }, 1);
        cum = RailKit.Cumulative(path);
        s = cum[cum.Length - 1];
        v = 0;
        PlaceCars();
        state = St.Dwell;
        dwellT = 6f;
    }

    void SetRouteTrack(int i, int track)
    {
        if (routeTracks == null) routeTracks = new List<int>();
        while (routeTracks.Count <= i) routeTracks.Add(track);
        routeTracks[i] = track;
    }

    void PlaceCars() => PlaceCarsStatic(carTs, path, cum, s);

    public float SpeedKmh => v * 3.6f;

    // 前面展望カメラ用: 先頭車前端の位置と進行方向
    public void CabPose(out Vector3 pos, out Vector3 fwd)
    {
        if (path == null || cum == null)
        {
            pos = transform.position + Vector3.up * 3f;
            fwd = Vector3.forward;
            return;
        }
        Vector3 pf, pr, f;
        RailKit.Sample(path, cum, s, out pf, out f);
        RailKit.Sample(path, cum, s - 4f, out pr, out f);
        fwd = pf - pr;
        if (fwd.sqrMagnitude < 1e-6f) fwd = f;
        fwd.Normalize();
        // 前面ガラスは車体先端より前へ張り出すので、鼻先より少し前・運転席高さに置く
        // (pfのままだと赤い前面の内側に入り画面が真っ赤になる)
        pos = pf + fwd * 2.2f + Vector3.up * 2.9f;
    }

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
        float hf = from.HalfLen, tf = StationLayout.ThroatLen, L = StationLayout.LeadLen;
        float offF = from.layout.trackOffsets[fromTrack];
        pts.Add(from.TrackWorldPoint(fromTrack, -exitSign * halfTrain));
        pts.Add(from.TrackWorldPoint(fromTrack, 0));
        pts.Add(from.TrackWorldPoint(fromTrack, exitSign * hf));                                                     // ホーム端
        pts.Add(from.transform.TransformPoint(new Vector3(Mathf.Sign(offF) * 2.3f, 0, exitSign * (hf + tf - L))));   // 収束(±2.3)
        pts.Add(from.transform.TransformPoint(new Vector3(Mathf.Sign(offF) * 2.3f, 0, exitSign * (hf + tf))));       // 駅端(リード端)

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
        pts.Add(to.transform.TransformPoint(new Vector3(Mathf.Sign(offT) * 2.3f, 0, enterSign * (ht + tf))));       // 駅端(リード端)
        pts.Add(to.transform.TransformPoint(new Vector3(Mathf.Sign(offT) * 2.3f, 0, enterSign * (ht + tf - L))));   // 収束(±2.3)
        pts.Add(to.TrackWorldPoint(toTrack, enterSign * ht));
        pts.Add(to.TrackWorldPoint(toTrack, 0));
        pts.Add(to.TrackWorldPoint(toTrack, -enterSign * halfTrain));
        return RailKit.Chaikin(pts, 2);
    }
}
