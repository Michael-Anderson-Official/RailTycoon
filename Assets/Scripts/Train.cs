using System.Collections.Generic;
using UnityEngine;

// 列車。route(駅列)を往復し、各駅で停車線を確保してから発車する。
// 発車時に到着駅の線を予約するので、駅間で詰まって止まることはない
public class Train : MonoBehaviour
{
    public int id; // M2-C: セーブ/ロードを跨いで安定な識別子。0は未割当
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

    // M2-C: Dwell中のpathがどちらの由来かを記録する(セーブ用メタデータのみ。
    // path形状・車両姿勢・運行ロジックには一切影響しない)。
    // StationLocal = Init()/ResyncToNetwork()が作る駅内3点経路
    // JustArrivedLeg = Arrive()が到着直前のlegをそのまま残した状態
    // (Arrive()はpathを再構築しないため、departStationは到着後も直前の出発駅を
    // 指したまま残る。この由来を区別しないと、セーブ/ロード後のRouteS・前面展望が
    // 「保存せず継続した場合」と一致しなくなる。CodexReviews/参照)
    public enum DwellPathKind { StationLocal, JustArrivedLeg }
    DwellPathKind dwellPathKind = DwellPathKind.StationLocal;
    public DwellPathKind CurrentDwellPathKind => dwellPathKind;

    List<Vector3> path;
    float[] cum;
    float s, v;
    Station departStation;
    int departTrack;
    float releaseS;
    bool released;
    TrackSegment curSeg;      // 走行中の閉塞区間(最終区間。Arrive()が解放)
    Station curSegFrom;

    // 通過駅(スキップストップ)を挟む区間で使う早期解放チェックポイント。
    // 出発駅の番線(released/releaseS)・最終区間(curSeg、Arrive()が解放)とは別に、
    // 通過駅の番線と、通過駅より手前の区間(最終区間を除く)を、列車がそこを抜けた
    // 時点で順に解放する。直結2駅の区間(通過駅なし)では常に空リストのままなので、
    // 既存の単一区間の挙動・数値には一切影響しない
    struct TransitCheckpoint
    {
        public float atS;
        public bool isSegment;
        public TrackSegment seg;
        public Station segFrom;
        public Station trackStation;
        public int track;
    }
    List<TransitCheckpoint> transitCheckpoints = new List<TransitCheckpoint>();
    int nextTransitCheckpoint;

    List<(Transform body, Transform bogieF, Transform bogieR)> carTs;
    readonly List<(Station dest, int count, Vector3 boardPos)> onboard = new List<(Station, int, Vector3)>();
    int onboardCount;

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
        // 開始駅のホームに据え付け(先頭が停止位置目標に来る)
        var st = route[idx];
        float h = HalfTrain;
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
        dwellPathKind = DwellPathKind.StationLocal;
    }

    // M2-C: SaveLoadV2専用の復元データ。フィールドの意味はTryDepart/Arrive/
    // ResyncToNetworkが実際に取り得る状態の組み合わせと1対1で対応する。
    // legSegmentはstate/dwellPathKindの組み合わせに応じて意味が変わる:
    // Run→走行中の閉塞(BuildLegの再構築と、呼び出し側でのTryEnter適用に使う)、
    // Dwell+JustArrivedLeg→到着に使った閉塞(path再構築専用。閉塞claimは生成しない)、
    // Dwell+StationLocal→null(未使用)。
    // 番線・閉塞の実際の予約適用(TryReserveSpecific/TryEnter)はこのメソッドの外
    // (SaveLoad側の全列車一括claim検証パス)で行う。ここでは論理状態の設定のみ行う
    public struct RestoreSpec
    {
        public TrainCatalog.Formation formation;
        public List<Station> route;
        public List<int> routeTracks;
        public List<int> lineIds;
        public bool cyclic;
        public int idx, dir, curTrack;
        public bool isRunning; // false=Dwell, true=Run
        public DwellPathKind dwellPathKind; // isRunning==trueの場合は無視
        public float dwellT, s, v;
        public Station departStation; // Dwell/StationLocalではnull
        public int departTrack;
        public float releaseS;
        public bool released; // isRunning==trueの場合のみ意味を持つ
        public TrackSegment legSegment; // 上記コメント参照
        // 通過駅(スキップストップ)を挟む区間の復元用。isRunning==trueの場合のみ意味を持つ。
        // 空/null(要素数1以下)なら従来通りの直結2駅(通過駅なし)として扱う。
        // 非空の場合、legSegmentはtransitSegmentsの末尾と必ず一致していること
        // (呼び出し側で事前に検証済みであること)。弧長(atS)は保存せず、都度この
        // メソッド内でTryDepart時と同じ計算式から再構築する
        public List<TrackSegment> transitSegments;   // departStation→routeIds[idx]の全区間(末尾=legSegment)
        public List<Station> transitStations;        // 通過駅のみ。transitSegments.Count-1個
        public List<int> transitTracksAtStations;     // 対応する番線。transitStations.Countと同数
        public int passedCheckpoints;                 // 既に解放済みのチェックポイント数
        public List<(Station dest, int count, Vector3 boardPos)> onboard;
        public int departureCount, arrivalCount;
    }

    // 保存データから論理状態を直接設定する(Init()と異なりDwellへ強制初期化しない)。
    // 呼び出し前提: routeTracks[idx]==curTrack、legSegmentがある場合は
    // legSegment.HasEndpoint(departStation)とlegSegment.HasEndpoint(route[idx])が
    // 共に真であることを、呼び出し側(SaveLoad)で事前に検証済みであること
    public void RestoreState(RestoreSpec spec)
    {
        fm = spec.formation;
        route = spec.route;
        routeTracks = spec.routeTracks;
        lineIds = spec.lineIds ?? new List<int>();
        cyclic = spec.cyclic;
        idx = spec.idx;
        dir = spec.dir;
        curTrack = spec.curTrack;
        carTs = TrainVisual.BuildCars(transform, fm);

        onboard.Clear();
        onboardCount = 0;
        if (spec.onboard != null)
            foreach (var grp in spec.onboard) { onboard.Add(grp); onboardCount += grp.count; }

        DepartureCount = spec.departureCount;
        ArrivalCount = spec.arrivalCount;

        var to = route[idx];
        if (spec.isRunning)
        {
            var from = spec.departStation;
            var seg = spec.legSegment;
            if (spec.transitSegments != null && spec.transitSegments.Count > 1)
            {
                // 通過駅を挟む多区間の復元。TryDepart時と同じ組み立て方(BuildMultiLeg+
                // チェックポイント再計算)で、保存されていた通過駅・番線から再構築する
                var stations = new List<Station> { from };
                stations.AddRange(spec.transitStations);
                stations.Add(to);
                var segs = spec.transitSegments;
                var tracks = new int[stations.Count];
                tracks[0] = spec.departTrack;
                for (int i = 0; i < spec.transitTracksAtStations.Count; i++) tracks[i + 1] = spec.transitTracksAtStations[i];
                tracks[tracks.Length - 1] = curTrack;

                var waypoints = new List<(Station st, int track, int enterSign, int exitSign)>(stations.Count);
                for (int i = 0; i < stations.Count; i++)
                {
                    int ex = i < segs.Count ? segs[i].SignAt(stations[i]) : 0;
                    int en = i > 0 ? segs[i - 1].SignAt(stations[i]) : 0;
                    waypoints.Add((stations[i], tracks[i], en, ex));
                }
                path = BuildMultiLeg(waypoints, HalfTrain);
                cum = RailKit.Cumulative(path);
                transitCheckpoints = BuildTransitCheckpoints(stations, segs, tracks, path, cum);
                nextTransitCheckpoint = Mathf.Clamp(spec.passedCheckpoints, 0, transitCheckpoints.Count);
            }
            else
            {
                int exitSignSingle = seg.SignAt(from);
                int enterSignSingle = seg.SignAt(to);
                path = BuildLeg(from, spec.departTrack, exitSignSingle, to, curTrack, enterSignSingle, HalfTrain);
                cum = RailKit.Cumulative(path);
                transitCheckpoints = new List<TransitCheckpoint>();
                nextTransitCheckpoint = 0;
            }
            float total = cum[cum.Length - 1];
            s = Mathf.Clamp(spec.s, 0f, total);
            v = Mathf.Max(0f, spec.v);
            departStation = from;
            departTrack = spec.departTrack;
            released = spec.released;
            releaseS = spec.releaseS;
            curSeg = seg;
            curSegFrom = spec.transitSegments != null && spec.transitSegments.Count > 1
                ? spec.transitStations[spec.transitStations.Count - 1]
                : from;
            state = St.Run;
            dwellT = spec.dwellT;
        }
        else if (spec.dwellPathKind == DwellPathKind.JustArrivedLeg)
        {
            var from = spec.departStation;
            var seg = spec.legSegment;
            int exitSign = seg.SignAt(from);
            int enterSign = seg.SignAt(to);
            path = BuildLeg(from, spec.departTrack, exitSign, to, curTrack, enterSign, HalfTrain);
            cum = RailKit.Cumulative(path);
            s = cum[cum.Length - 1];
            v = 0;
            departStation = from;
            departTrack = spec.departTrack;
            released = true;
            curSeg = null;
            curSegFrom = null;
            state = St.Dwell;
            dwellT = spec.dwellT;
            dwellPathKind = DwellPathKind.JustArrivedLeg;
        }
        else
        {
            float h = HalfTrain;
            path = RailKit.Chaikin(new List<Vector3>
            {
                to.TrackWorldPoint(curTrack, -h),
                to.TrackWorldPoint(curTrack, 0),
                to.TrackWorldPoint(curTrack, h),
            }, 1);
            cum = RailKit.Cumulative(path);
            s = cum[cum.Length - 1];
            v = 0;
            departStation = null;
            departTrack = 0;
            released = true;
            curSeg = null;
            curSegFrom = null;
            state = St.Dwell;
            dwellT = spec.dwellT;
            dwellPathKind = DwellPathKind.StationLocal;
        }
        PlaceCars();
    }

    // Bootstrap.SimTickから固定tickごとに呼ばれるシミュレーション本体。
    // dtは「シミュレーション秒」(tickSeconds * GameState.timeScale)。
    // 見た目の反映(PlaceCars)はここでは行わず、Bootstrap側が全列車のtick消化後に
    // 1回だけまとめて呼ぶ(複数tickを1フレームで消化する場合の無駄な再描画を避けるため)
    public void SimTick(float dt)
    {
        // 速度倍率が高いほど1tickあたりのシミュレーション秒数(dt)が大きくなるが、
        // そのまま陽的に積分すると、加減速・発車・到着の各判定がtickの粗さへ
        // 量子化され、同じシミュレーション時刻でも×1と×20で列車位置が数十m
        // ずれていた(×20では1tick=1/3シミュレーション秒)。
        // 刻み幅を倍率によらず一定(=×1相当)にして、×20を「×1の刻みを20回」に
        // 分解することで、構造的に等価にする。物理は数回の四則演算なので、
        // 刻み数が増えても負荷は問題にならない
        int steps = Mathf.Max(1, Mathf.CeilToInt(dt / Bootstrap.TickSeconds - 1e-4f));
        float step = dt / steps;
        for (int i = 0; i < steps; i++) SimStep(step);
    }

    void SimStep(float dt)
    {
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
            while (nextTransitCheckpoint < transitCheckpoints.Count && s > transitCheckpoints[nextTransitCheckpoint].atS)
            {
                var cp = transitCheckpoints[nextTransitCheckpoint];
                if (cp.isSegment) cp.seg.Leave(cp.segFrom, this);
                else cp.trackStation.Release(cp.track);
                nextTransitCheckpoint++;
            }
            if (s >= total - 0.05f)
            {
                s = total;
                Arrive();
            }
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

        // 直結でなければ、間に挟まる通過駅を経由するホップ列を探す(種別による停車
        // パターンの違いは、経路(route)自体には現れず、隣接しない2停車駅の間を
        // どう繋ぐかというこの発車処理内部の詳細として吸収する)
        var stations = new List<Station> { cur };
        var segs = new List<TrackSegment>();
        var directSeg = TrackNetwork.Find(cur, to);
        if (directSeg != null)
        {
            stations.Add(to);
            segs.Add(directSeg);
        }
        else
        {
            var hops = TrackNetwork.FindPath(cur, to);
            if (hops == null) { dwellT = 5f; return; }
            foreach (var hop in hops) { stations.Add(hop.station); segs.Add(hop.seg); }
        }

        int n = stations.Count;
        var tracks = new int[n];
        tracks[0] = curTrack;
        var reservedTracks = new List<(Station st, int track)>();
        var reservedSegs = new List<(TrackSegment seg, Station from)>();
        bool ok = true;
        for (int i = 1; i < n; i++)
        {
            var st = stations[i];
            int enterSign = segs[i - 1].SignAt(st);
            int track;
            if (i == n - 1)
            {
                // 真の到着駅: 指定番線があればそれ(空くまで発車を待つ)、無ければ左側優先
                int wantTrack = (routeTracks != null && next < routeTracks.Count) ? routeTracks[next] : -1;
                if (wantTrack >= 0)
                {
                    if (!st.TryReserveSpecific(wantTrack)) { ok = false; break; }
                    track = wantTrack;
                }
                else if (!st.TryReserveFor(enterSign, out track)) { ok = false; break; }
            }
            // 通過駅: 他の列車が別の番線に停車中でも使えるよう、空いている番線を動的に探す
            else if (!st.TryReserveFor(enterSign, out track)) { ok = false; break; }
            tracks[i] = track;
            reservedTracks.Add((st, track));

            var seg = segs[i - 1];
            var fromSt = stations[i - 1];
            // 閉塞: 同一方向の区間に先行列車がいる間は出発できない
            if (!seg.TryEnter(fromSt, this)) { ok = false; break; }
            reservedSegs.Add((seg, fromSt));
        }
        if (!ok)
        {
            // 経路上のどこか1か所でも確保できなければ、それまでに確保した分を全て
            // 巻き戻し、既存の失敗時リトライ(数秒後に再試行)へ合流する
            foreach (var (seg, from) in reservedSegs) seg.Leave(from, this);
            foreach (var (st, track) in reservedTracks) st.Release(track);
            dwellT = 3f;
            return;
        }

        int boarded = Board(cur);
        cur.OnDeparture(boarded);

        var waypoints = new List<(Station st, int track, int enterSign, int exitSign)>(n);
        for (int i = 0; i < n; i++)
        {
            int exitSign = i < segs.Count ? segs[i].SignAt(stations[i]) : 0;
            int enterSign = i > 0 ? segs[i - 1].SignAt(stations[i]) : 0;
            waypoints.Add((stations[i], tracks[i], enterSign, exitSign));
        }
        path = BuildMultiLeg(waypoints, HalfTrain);
        cum = RailKit.Cumulative(path);
        // 経路先頭は列車の尻尾位置。先頭車はそこから編成長ぶん先にいる
        s = HalfTrain * 2f;
        v = 0;
        departStation = cur;
        departTrack = curTrack;
        released = false;
        releaseS = cur.HalfLen + StationLayout.ThroatLen + fm.cars * StationLayout.CarLength + 10f;

        // 通過駅の番線・通過区間(最終区間を除く)は、列車がそこを実際に抜けた時点で
        // 順に解放する(発車時点で経路全体を一括確保しつつ、他列車がすぐ使えるように
        // するため)。最終区間はcurSeg/curSegFromとして従来通りArrive()が解放する
        transitCheckpoints = BuildTransitCheckpoints(stations, segs, tracks, path, cum);
        nextTransitCheckpoint = 0;

        curSeg = segs[segs.Count - 1];
        curSegFrom = stations[n - 2];
        curTrack = tracks[n - 1];
        idx = next;
        state = St.Run;
        DepartureCount++;
    }

    // stations[1..^2](通過駅)の番線と、それぞれ手前の区間(segs[0..^2])の早期解放
    // チェックポイントを組む。TryDepart(新規発車)・RestoreState(セーブ復元)の両方から
    // 同じ計算式で呼ぶ(セーブには弧長を保存せず、都度この駅ジオメトリから再計算する)
    List<TransitCheckpoint> BuildTransitCheckpoints(List<Station> stations, List<TrackSegment> segs, int[] tracks, List<Vector3> path, float[] cum)
    {
        var list = new List<TransitCheckpoint>();
        int n = stations.Count;
        for (int i = 1; i < n - 1; i++)
        {
            float atS = ArcLengthNear(path, cum, stations[i].TrackWorldPoint(tracks[i], 0))
                + fm.cars * StationLayout.CarLength + 10f;
            list.Add(new TransitCheckpoint { atS = atS, isSegment = true, seg = segs[i - 1], segFrom = stations[i - 1] });
            list.Add(new TransitCheckpoint { atS = atS, isSegment = false, trackStation = stations[i], track = tracks[i] });
        }
        return list;
    }

    // pathの中でworldPosに最も近い点の弧長を返す(通過駅・通過区間の早期解放位置を
    // 求めるのに使う。厳密な等距離探索ではなく最近傍点ベースだが、早期解放は
    // 「列車の尾が確実に抜けた後」を狙う安全側の目的なので十分な精度)
    static float ArcLengthNear(List<Vector3> path, float[] cum, Vector3 worldPos)
    {
        int best = 0; float bestD = float.MaxValue;
        for (int i = 0; i < path.Count; i++)
        {
            float d = (path[i] - worldPos).sqrMagnitude;
            if (d < bestD) { bestD = d; best = i; }
        }
        return cum[best];
    }

    void Arrive()
    {
        ArrivalCount++;
        if (!released) { released = true; departStation.Release(departTrack); }
        if (curSeg != null)
        {
            curSeg.Leave(curSegFrom, this); // 閉塞解放
            curSeg = null;
        }
        var st = route[idx];
        // 降車と運賃収受。M2-D: この線に降車可能なホーム縁が1つも無ければ、
        // ループごと丸ごとスキップする(ホーム縁ごとに繰り返さないため、同じ旅客・
        // 運賃を二重に計上することは無い)。降車不可の旅客は乗車したまま残る
        int off = 0;
        if (st.CanAlightAt(curTrack))
        {
            for (int i = onboard.Count - 1; i >= 0; i--)
            {
                if (onboard[i].dest != st) continue;
                float km = Vector3.Distance(onboard[i].boardPos, st.transform.position) / 1000f;
                GameState.EarnFare(onboard[i].count, km);
                off += onboard[i].count;
                onboardCount -= onboard[i].count;
                onboard.RemoveAt(i);
            }
        }
        st.UpdateLabel();
        state = St.Dwell;
        dwellT = 25f;
        v = 0;
        dwellPathKind = DwellPathKind.JustArrivedLeg;
    }

    int Board(Station st)
    {
        // M2-D: この線に乗車可能なホーム縁が1つも無ければ乗車処理自体を行わない
        if (!st.CanBoardAt(curTrack)) return 0;
        int avail = fm.Capacity - onboardCount;
        if (avail <= 0) return 0;
        int total = 0;
        // Dictionary.Keysの列挙順は保証されないため、TrackNetwork.stationsの登録順で
        // フィルタして安定させる(同一seed・同一手順で同じ乗車内訳になるようにするため)
        foreach (var dest in TrackNetwork.stations)
        {
            if (!st.waiting.ContainsKey(dest)) continue;
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

    // 走行中の区間(最終区間・未解放の通過区間)としてsegを掴んでいるか。
    // 線路の撤去時に、影響を受ける列車だけを組み直すために使う
    // (無関係な列車まで組み直すと、走行中の位置・速度が失われてダイヤが乱れる)
    public bool HoldsSegment(TrackSegment seg)
    {
        if (seg == null) return false;
        if (curSeg == seg) return true;
        for (int i = nextTransitCheckpoint; i < transitCheckpoints.Count; i++)
            if (transitCheckpoints[i].isSegment && transitCheckpoints[i].seg == seg) return true;
        return false;
    }

    // 撤去時の払い戻し額
    public double RefundValue => fm.CostYen * 0.5;

    // 保有中の予約(閉塞・発車駅の線・現在/到着駅の線)を全て解放。撤去前に呼ぶ
    public void ReleaseAll()
    {
        if (curSeg != null) { curSeg.Leave(curSegFrom, this); curSeg = null; }
        if (!released && departStation != null) departStation.Release(departTrack);
        released = true;
        for (int i = nextTransitCheckpoint; i < transitCheckpoints.Count; i++)
        {
            var cp = transitCheckpoints[i];
            if (cp.isSegment) cp.seg.Leave(cp.segFrom, this);
            else cp.trackStation.Release(cp.track);
        }
        transitCheckpoints.Clear();
        nextTransitCheckpoint = 0;
        if (route != null && idx >= 0 && idx < route.Count) route[idx].Release(curTrack);
    }

    // 線路網が変わった(駅の建て替え等)あと、現在(直前)駅に停車状態で復帰する。
    // 予約を取り直し、番線を有効値へ整合する
    public void ResyncToNetwork()
    {
        if (curSeg != null) { curSeg.Leave(curSegFrom, this); curSeg = null; }
        if (!released && departStation != null) departStation.Release(departTrack);
        released = true;
        for (int i = nextTransitCheckpoint; i < transitCheckpoints.Count; i++)
        {
            var cp = transitCheckpoints[i];
            if (cp.isSegment) cp.seg.Leave(cp.segFrom, this);
            else cp.trackStation.Release(cp.track);
        }
        transitCheckpoints.Clear();
        nextTransitCheckpoint = 0;

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
        float h = HalfTrain;   // 先頭がN両の停止位置目標(±N*車長/2)に来るよう据え付け
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
        dwellPathKind = DwellPathKind.StationLocal;
    }

    void SetRouteTrack(int i, int track)
    {
        if (routeTracks == null) routeTracks = new List<int>();
        while (routeTracks.Count <= i) routeTracks.Add(track);
        routeTracks[i] = track;
    }

    public void PlaceCars() => PlaceCarsStatic(carTs, path, cum, s);

    public float SpeedKmh => v * 3.6f;
    // M2-C: セーブ用。SpeedKmh(*3.6f)経由の往復はfloat丸め誤差が後続tickへ伝播し得るため、
    // 内部値をbit-exactに読み書きできる専用アクセサを用意する
    public float V => v;

    // M2-B.2: ×1/×5/×20比較テスト用の読み取り専用観測プロパティ。挙動は変えない
    public bool IsDwelling => state == St.Dwell;
    public float DwellRemaining => dwellT;
    public float RouteS => s;
    public bool DepartureTrackReleased => released;
    public int OnboardCount => onboardCount;
    public int DepartureCount { get; private set; }
    public int ArrivalCount { get; private set; }

    // M2-C: セーブ用の読み取り専用観測プロパティ。挙動は変えない
    public Station DepartStation => departStation;
    public int DepartTrack => departTrack;
    public float ReleaseS => releaseS;
    public TrackSegment CurSeg => curSeg; // Run中のみ非null
    public IReadOnlyList<(Station dest, int count, Vector3 boardPos)> Onboard => onboard;

    // 通過駅(スキップストップ)を挟む区間のセーブ用。直結2駅の単一区間ならsegmentIdsは
    // CurSeg 1件のみ・station/tracksは空になる(従来通りの単一区間として保存される)
    public void GetTransitChainForSave(out List<int> segmentIds, out List<int> stationIds, out List<int> tracks, out int passed)
    {
        segmentIds = new List<int>();
        stationIds = new List<int>();
        tracks = new List<int>();
        for (int i = 0; i < transitCheckpoints.Count; i += 2)
        {
            segmentIds.Add(transitCheckpoints[i].seg.id);
            stationIds.Add(transitCheckpoints[i + 1].trackStation.id);
            tracks.Add(transitCheckpoints[i + 1].track);
        }
        if (curSeg != null) segmentIds.Add(curSeg.id);
        passed = nextTransitCheckpoint;
    }

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
        pos = pf + fwd * 2.2f
            + Vector3.up * (TrainVisual.BogieRootY + TrainVisual.CabEyeLocalY);
    }

    // 台車ごとの接線サンプル用の前後窓。狭くしすぎるとレール中心線の折れ点で
    // 接線がガタつき、広すぎると渡り線のような急カーブで実際の軌道からずれる
    const float BogieSampleWindow = 1.5f;

    // エディタのSnapshotでも使う車両配置(先頭車の弧長sから後方へ並べる)。
    // 各台車(前後)は自分の弧長位置でレール中心線を独立サンプルするため、渡り線の
    // ような急なカーブでも車輪(台車)が必ずレールへ追従する。車体は実車と同様、
    // 前後の台車中心を結ぶ弦の上に乗る剛体として、その2点から姿勢を決める
    public static void PlaceCarsStatic(List<(Transform body, Transform bogieF, Transform bogieR)> cars,
        List<Vector3> path, float[] cum, float s)
    {
        if (cars == null) return;
        float carLen = StationLayout.CarLength;
        for (int i = 0; i < cars.Count; i++)
        {
            float c = s - carLen * 0.5f - i * carLen;
            var (body, bogieF, bogieR) = cars[i];

            Vector3 fPos = SampleBogie(path, cum, c + TrainVisual.BogieOffset, out Vector3 fFwd);
            Vector3 rPos = SampleBogie(path, cum, c - TrainVisual.BogieOffset, out Vector3 rFwd);
            var mid = (fPos + rPos) * 0.5f;
            var fwd = fPos - rPos;
            if (fwd.sqrMagnitude < 1e-6f) fwd = fFwd;
            body.SetPositionAndRotation(mid, Quaternion.LookRotation(fwd.normalized, Vector3.up));
            // 台車はbodyの子なので、車体を動かした後にworld姿勢を確定する。逆順だと
            // 最後のbody移動が台車へ二重に加算され、停止中でも車輪がレールからずれる。
            if (bogieF != null)
                bogieF.SetPositionAndRotation(fPos, Quaternion.LookRotation(fFwd, Vector3.up));
            if (bogieR != null)
                bogieR.SetPositionAndRotation(rPos, Quaternion.LookRotation(rFwd, Vector3.up));
        }
    }

    // 弧長centerの位置と接線を返す。持上げ量は車輪下面がレール頭頂へ一致する実寸値。
    static Vector3 SampleBogie(List<Vector3> path, float[] cum, float center, out Vector3 fwd)
    {
        Vector3 pf, pr, f;
        RailKit.Sample(path, cum, center + BogieSampleWindow, out pf, out f);
        RailKit.Sample(path, cum, center - BogieSampleWindow, out pr, out f);
        fwd = pf - pr;
        if (fwd.sqrMagnitude < 1e-6f) fwd = f;
        fwd.Normalize();
        return (pf + pr) * 0.5f + Vector3.up * TrainVisual.BogieRootY;
    }

    // 収束点と本線側(左側通行)のオフセットが±0.1m以上ずれていれば、その駅端では
    // 両渡り線(Station.RebuildTrackVisualのAddCrossover)を渡って本線側へ乗り換える
    // 必要がある、という判定に使うしきい値
    const float CrossoverMismatch = 0.1f;

    // 走行経路を最後に取り直す間隔。台車のサンプル窓(BogieSampleWindow=1.5m)より
    // 十分細かくし、かつ点数が増えすぎない値
    const float PathSampleSpacing = 1.0f;

    // 駅stを出て(またはそこへ入って)signの向きの区間へ抜けるまでの点列を作る部品。
    // BuildLeg/BuildMultiLegから共通で使う(通過駅では停止位置マーカー(タップ余白)を
    // 省き、ホーム中心のみで繋ぐ)。mainOffsetは接続先(駅間カーブ)の端点をst基準の
    // ローカルxへ直したもの(このstから見た本線側オフセット)
    // 駅構内の走行経路は、実際に描画したレールの中心線(Station.TrackCentreLocal)を
    // そのまま切り出して使う。以前は同じ形をここで別途組み直していたため、片方だけ
    // 直すと列車がレールから浮いた(スロートの収束の滲み出しがまさにそれだった)
    static List<Vector3> StationRun(Station st, int track, int sign, float mainOffset,
        float halfTrain, bool includeTailMarker, bool departing)
    {
        float h = st.HalfLen, tf = StationLayout.ThroatLen, L = StationLayout.LeadLen;
        float conv = Mathf.Sign(st.layout.trackOffsets[track]) * 2.3f;
        var centre = st.TrackCentreLocal(track);
        // 駅端(z=±(h+tf))まで伸びた中心線が無い場合(未接続端など)は、従来どおり
        // その場で組む。走行経路が途切れるのを避けるための保険
        if (centre == null || centre.Count < 2)
            return LegacyStationRun(st, track, sign, mainOffset, halfTrain, includeTailMarker, departing);

        float zStop = includeTailMarker ? -sign * halfTrain : 0f;
        float zEnd = sign * (h + tf);
        var local = departing ? Station.PortionByZ(centre, zStop, zEnd)
                              : Station.PortionByZ(centre, zEnd, zStop);
        if (local.Count < 2)
            return LegacyStationRun(st, track, sign, mainOffset, halfTrain, includeTailMarker, departing);

        // 停車線が進行方向の本線側と反対だった場合は、リード区間(駅端手前L)の中で
        // 両渡り線を渡って本線側へ移る。Station側が同じ位置に渡り線を描いている
        if (Mathf.Abs(mainOffset - conv) > CrossoverMismatch)
        {
            // 描画された渡り線(Station.RebuildTrackVisualのAddCrossover)と同じS字を辿る。
            // 位置・長さ・曲線の作り方をRailKit側と揃えているので、列車はレールの上を通る
            // (以前はリード区間全体を線形に横切っており、描かれた分岐から最大1.7m外れていた)
            float czCross = sign * (h + tf - L * 0.5f);         // 渡り線の中心z
            float dHalf = RailKit.CrossoverHalfLength;
            var from = new Vector3(conv, 0f, czCross - sign * dHalf);
            var to = new Vector3(mainOffset, 0f, czCross + sign * dHalf);
            var cross = RailKit.CrossoverPath(from, to, new Vector3(0f, 0f, sign));
            // 既存点のxを曲線から取るだけでは、リード区間の点が疎なので点と点の間が
            // 弦になってカーブを内側で切ってしまう(実測0.14m)。渡り線区間は曲線の点
            // そのものへ差し替え、前後は自線側/本線側の直線にする
            // crossは常に「自線側→本線側」(出発向き)で作られる。到着時のlocalは
            // 駅端→ホームの順なので、いったん出発向きへ揃えて組み、最後に戻す。
            // 揃えずに組むと到着経路の点順が壊れ、駅構内で経路が飛ぶ
            float zA = cross[0].z, zB = cross[cross.Count - 1].z;
            var ordered = new List<Vector3>(local);
            if (!departing) ordered.Reverse();
            var rebuilt = new List<Vector3>(ordered.Count + cross.Count);
            foreach (var p in ordered)
                if ((p.z - zA) * sign < 0f) rebuilt.Add(new Vector3(conv, p.y, p.z));
            foreach (var c in cross) rebuilt.Add(c);
            foreach (var p in ordered)
                if ((p.z - zB) * sign > 0f) rebuilt.Add(new Vector3(mainOffset, p.y, p.z));
            if (!departing) rebuilt.Reverse();
            local = rebuilt;
        }

        var pts = new List<Vector3>(local.Count);
        foreach (var p in local) pts.Add(st.transform.TransformPoint(p));
        return pts;
    }

    // 中心線が使えない場合の従来構成(保険)
    static List<Vector3> LegacyStationRun(Station st, int track, int sign, float mainOffset,
        float halfTrain, bool includeTailMarker, bool departing)
    {
        var pts = new List<Vector3>();
        float h = st.HalfLen, tf = StationLayout.ThroatLen, L = StationLayout.LeadLen;
        float conv = Mathf.Sign(st.layout.trackOffsets[track]) * 2.3f;
        if (includeTailMarker) pts.Add(st.TrackWorldPoint(track, -sign * halfTrain));
        pts.Add(st.TrackWorldPoint(track, 0));
        pts.Add(st.TrackWorldPoint(track, sign * (h - Station.PlatformEndHold)));
        pts.Add(st.TrackWorldPoint(track, sign * h));
        pts.Add(st.transform.TransformPoint(new Vector3(conv, 0, sign * (h + tf - L))));
        if (Mathf.Abs(mainOffset - conv) > CrossoverMismatch)
            pts.Add(st.transform.TransformPoint(new Vector3(mainOffset, 0, sign * (h + tf - L * 0.5f))));
        pts.Add(st.transform.TransformPoint(new Vector3(mainOffset, 0, sign * (h + tf))));
        if (!departing) pts.Reverse();
        return pts;
    }

    static List<Vector3> StationDeparture(Station st, int track, int sign, float mainOffset, float halfTrain, bool includeTailMarker)
        => StationRun(st, track, sign, mainOffset, halfTrain, includeTailMarker, departing: true);

    static List<Vector3> StationArrival(Station st, int track, int sign, float mainOffset, float halfTrain, bool includeTailMarker)
        => StationRun(st, track, sign, mainOffset, halfTrain, includeTailMarker, departing: false);

    // 駅from(exitSign方向へ出る)〜駅to(enterSign方向から入る)を結ぶ曲線。
    // mainF/mainTは、それぞれfrom/to自身のローカル座標系で見た曲線端点のオフセット
    // (StationDeparture/StationArrivalの本線側オフセットにそのまま渡す)
    static List<Vector3> CurveBetween(Station from, int exitSign, Station to, int enterSign, out float mainF, out float mainT)
    {
        var endA = from.End(exitSign);
        var endB = to.End(enterSign);
        // 駅間は直線ではなく、両駅それぞれの発着方向(Axis*sign)へ、実際の鉄道のように
        // 一定半径に近い滑らかな曲線で接続する。駅同士が斜めに向き合っていても、
        // 駅を出た瞬間に進行方向が折れ曲がらないようにするため
        float dist = Vector3.Distance(endA, endB);
        int curveN = Mathf.Max(16, Mathf.CeilToInt(dist / 15f));
        Vector3 tan0 = from.Axis * exitSign, tan1 = -(to.Axis * enterSign);
        var curve = RailKit.SmoothConnectPath(endA, tan0, endB, tan1, curveN);
        // 左側通行(実際のレールと同じ規約)。端点は近似接線でなくtan0/tan1そのものを使い、
        // 駅の自前スロートのレールと厳密に一致させる。平滑化まで含めてTrackSegment.SideCentre
        // と同じ手順にしてあるので、敷かれたレールと同一の線になる
        var curveOffset = RailKit.Chaikin(RailKit.OffsetWithEndTangents(curve, 2.3f, tan0, tan1), 2);
        mainF = from.transform.InverseTransformPoint(curveOffset[0]).x;
        mainT = to.transform.InverseTransformPoint(curveOffset[curveOffset.Count - 1]).x;
        return curveOffset;
    }

    // 駅fromの線fromTrackから駅toの線toTrackまでの走行経路を組む(直結2駅専用)。
    // 内部的にはBuildMultiLegの2駅版(通過駅なし)と同じ
    public static List<Vector3> BuildLeg(Station from, int fromTrack, int exitSign,
        Station to, int toTrack, int enterSign, float halfTrain)
        => BuildMultiLeg(new List<(Station st, int track, int enterSign, int exitSign)>
        {
            (from, fromTrack, 0, exitSign),
            (to, toTrack, enterSign, 0),
        }, halfTrain);

    // 通過駅を挟んだ複数区間を1本の走行経路として繋ぐ。waypoints[0]=真の出発駅、
    // waypoints[^1]=真の到着駅、間は全て通過駅(そのenterSign/exitSignの両方を使う)。
    // 停止位置マーカー(halfTrain分の余白)は先頭・末尾の駅にだけ付け、通過駅では
    // ホーム中心のみで発着を繋ぐ(素朴にBuildLegを複数連結すると、通過駅ごとに
    // 前後halfTrainぶん行って戻る不自然な折り返しが混入するため)
    public static List<Vector3> BuildMultiLeg(List<(Station st, int track, int enterSign, int exitSign)> waypoints, float halfTrain)
    {
        var pts = new List<Vector3>();
        int n = waypoints.Count;
        for (int i = 0; i + 1 < n; i++)
        {
            var wa = waypoints[i];
            var wb = waypoints[i + 1];
            var curve = CurveBetween(wa.st, wa.exitSign, wb.st, wb.enterSign, out float mainF, out float mainT);
            bool isTrueOrigin = i == 0;
            bool isTrueDestination = i + 2 == n;
            pts.AddRange(StationDeparture(wa.st, wa.track, wa.exitSign, mainF, halfTrain, isTrueOrigin));
            pts.AddRange(curve);
            pts.AddRange(StationArrival(wb.st, wb.track, wb.enterSign, mainT, halfTrain, isTrueDestination));
        }
        // ここでChaikinを掛け直さない。駅構内は描画済みの中心線(平滑化済み)、駅間は
        // SmoothConnectPathの曲線をそのまま使っており、再平滑化するとレールから
        // 外れてしまう(まさにそれが「列車だけ浮く」原因だった)。
        // 継ぎ目は駅端で接線が一致しているので、掛け直さなくても折れない。
        //
        // ただし、切り貼りしただけでは区間長が「ホーム部分は数十m、カーブは数十cm」と
        // 百倍以上ばらつく。長い区間は将来の縦カーブ(高架・地下)を串刺しにして
        // ショートカットするため、最後に一定間隔で取り直す。元の線の上を通るので
        // レールからは外れない
        return RailKit.Resample(pts, PathSampleSpacing);
    }
}
