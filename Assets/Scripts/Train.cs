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
    TrackSegment curSeg;      // 走行中の閉塞区間
    Station curSegFrom;

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
            int exitSign = seg.SignAt(from);
            int enterSign = seg.SignAt(to);
            path = BuildLeg(from, spec.departTrack, exitSign, to, curTrack, enterSign, HalfTrain);
            cum = RailKit.Cumulative(path);
            float total = cum[cum.Length - 1];
            s = Mathf.Clamp(spec.s, 0f, total);
            v = Mathf.Max(0f, spec.v);
            departStation = from;
            departTrack = spec.departTrack;
            released = spec.released;
            releaseS = spec.releaseS;
            curSeg = seg;
            curSegFrom = from;
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
        DepartureCount++;
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
            if (bogieF != null) bogieF.SetPositionAndRotation(fPos, Quaternion.LookRotation(fFwd, Vector3.up));
            if (bogieR != null) bogieR.SetPositionAndRotation(rPos, Quaternion.LookRotation(rFwd, Vector3.up));

            var mid = (fPos + rPos) * 0.5f;
            var fwd = fPos - rPos;
            if (fwd.sqrMagnitude < 1e-6f) fwd = fFwd;
            body.SetPositionAndRotation(mid, Quaternion.LookRotation(fwd.normalized, Vector3.up));
        }
    }

    // 弧長centerの位置(前後窓±BogieSampleWindowの中点、+0.2m持ち上げ)と接線を返す
    static Vector3 SampleBogie(List<Vector3> path, float[] cum, float center, out Vector3 fwd)
    {
        Vector3 pf, pr, f;
        RailKit.Sample(path, cum, center + BogieSampleWindow, out pf, out f);
        RailKit.Sample(path, cum, center - BogieSampleWindow, out pr, out f);
        fwd = pf - pr;
        if (fwd.sqrMagnitude < 1e-6f) fwd = f;
        fwd.Normalize();
        return (pf + pr) * 0.5f + Vector3.up * 0.2f;
    }

    // 収束点と本線側(左側通行)のオフセットが±0.1m以上ずれていれば、その駅端では
    // 両渡り線(Station.RebuildTrackVisualのAddCrossover)を渡って本線側へ乗り換える
    // 必要がある、という判定に使うしきい値
    const float CrossoverMismatch = 0.1f;

    // 駅fromの線fromTrackから駅toの線toTrackまでの走行経路を組む。
    // 収束(収束点、駅の自線側±2.3)と、駅間区間で使う本線側(常に左側通行)のオフセットが
    // 一致しない場合(=停車線が進行方向の反対側だった場合)、リード区間の途中
    // (Station.RebuildTrackVisualが両渡り線を描く位置と同じz)で乗り換える点を挟み、
    // 実際に描かれた渡り線の上を通るようにする
    public static List<Vector3> BuildLeg(Station from, int fromTrack, int exitSign,
        Station to, int toTrack, int enterSign, float halfTrain)
    {
        var pts = new List<Vector3>();
        float hf = from.HalfLen, tf = StationLayout.ThroatLen, L = StationLayout.LeadLen;
        float offF = from.layout.trackOffsets[fromTrack];
        float convF = Mathf.Sign(offF) * 2.3f;

        var endA = from.End(exitSign);
        var endB = to.End(enterSign);
        // 駅間は直線ではなく、両駅それぞれの発着方向(Axis*sign)へ滑らかに接続する
        // 曲線(3次エルミート)にする。駅同士が斜めに向き合っていても、駅を出た瞬間に
        // 進行方向が折れ曲がらないようにするため
        float dist = Vector3.Distance(endA, endB);
        int curveN = Mathf.Max(12, Mathf.CeilToInt(dist / 20f));
        var curve = RailKit.HermitePath(endA, from.Axis * exitSign, endB, -(to.Axis * enterSign), curveN);
        var curveOffset = RailKit.Offset(curve, 2.3f); // 左側通行(実際のレールと同じ規約)
        // 駅間区間の始点(curveOffset[0])を出発駅のローカル座標に戻し、本線側のオフセットを得る
        float mainF = from.transform.InverseTransformPoint(curveOffset[0]).x;

        pts.Add(from.TrackWorldPoint(fromTrack, -exitSign * halfTrain));
        pts.Add(from.TrackWorldPoint(fromTrack, 0));
        pts.Add(from.TrackWorldPoint(fromTrack, exitSign * hf));                                     // ホーム端
        pts.Add(from.transform.TransformPoint(new Vector3(convF, 0, exitSign * (hf + tf - L))));     // 収束(自線側±2.3)
        if (Mathf.Abs(mainF - convF) > CrossoverMismatch)
            pts.Add(from.transform.TransformPoint(new Vector3(mainF, 0, exitSign * (hf + tf - L * 0.5f)))); // 両渡り線で本線側へ
        pts.Add(from.transform.TransformPoint(new Vector3(mainF, 0, exitSign * (hf + tf))));         // 駅端(リード端、本線側)

        pts.AddRange(curveOffset);

        float ht = to.HalfLen;
        float offT = to.layout.trackOffsets[toTrack];
        float convT = Mathf.Sign(offT) * 2.3f;
        float mainT = to.transform.InverseTransformPoint(curveOffset[curveOffset.Count - 1]).x;
        pts.Add(to.transform.TransformPoint(new Vector3(mainT, 0, enterSign * (ht + tf))));          // 駅端(リード端、本線側)
        if (Mathf.Abs(mainT - convT) > CrossoverMismatch)
            pts.Add(to.transform.TransformPoint(new Vector3(mainT, 0, enterSign * (ht + tf - L * 0.5f)))); // 両渡り線で自線側へ
        pts.Add(to.transform.TransformPoint(new Vector3(convT, 0, enterSign * (ht + tf - L))));       // 収束(自線側±2.3)
        pts.Add(to.TrackWorldPoint(toTrack, enterSign * ht));
        pts.Add(to.TrackWorldPoint(toTrack, 0));
        pts.Add(to.TrackWorldPoint(toTrack, -enterSign * halfTrain));
        return RailKit.Chaikin(pts, 2);
    }
}
