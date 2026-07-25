using System.Collections.Generic;
using UnityEngine;

// 線路構造。値はセーブデータへそのまま保存するため固定する。
// 既存セーブでフィールドが欠落した場合は0=Ballastとなり、従来の見た目を維持する。
public enum TrackBedType
{
    Ballast = 0,
    Slab = 1,
}

// 駅間を結ぶ複線区間。両駅のスロート収束点(End)同士を直線で結ぶ
public class TrackSegment
{
    public int id; // M2-C: セーブ/ロードを跨いで安定な識別子。0は未割当
    public Station a, b;
    public int signA, signB;  // 各駅のどちらの端に接続するか(±1)
    public TrackBedType bedType = TrackBedType.Ballast;
    public GameObject go;
    public float length;

    // 閉塞: 駅間を1閉塞とし、同一方向には1列車しか入れない([0]=a→b、[1]=b→a)
    readonly Train[] occupant = new Train[2];

    public Vector3 EndA => a.End(signA);
    public Vector3 EndB => b.End(signB);

    public int SignAt(Station s) => s == a ? signA : signB;
    public Station Other(Station s) => s == a ? b : a;
    // M2-C: SignAt/Other/DirIndex/TryEnter/Leaveは全て「aでなければb」という前提で
    // 動くため、復元時に不正なfromを渡すと誤った側として扱われてしまう。
    // 呼び出し前にこれで実際にa/bのどちらかであることを検証すること
    public bool HasEndpoint(Station s) => s == a || s == b;

    int DirIndex(Station from) => from == a ? 0 : 1;

    public bool TryEnter(Station from, Train t)
    {
        int i = DirIndex(from);
        if (occupant[i] != null && occupant[i] != t) return false;
        occupant[i] = t;
        return true;
    }

    public void Leave(Station from, Train t)
    {
        int i = DirIndex(from);
        if (occupant[i] == t) occupant[i] = null;
    }

    // M2-B.2: ×1/×5/×20比較テスト用の読み取り専用観測プロパティ。挙動は変えない
    public Train OccupantFrom(Station from) => occupant[DirIndex(from)];

    public void Build(Transform parent)
    {
        if (go != null)
        {
            if (Application.isPlaying) Object.Destroy(go);
            else Object.DestroyImmediate(go);
        }
        go = new GameObject("Track_" + a.stationName + "_" + b.stationName);
        go.transform.SetParent(parent, false);
        var bed = new RailKit.MeshData();
        var rail = new RailKit.MeshData();
        var support = new RailKit.MeshData();
        var detail = new RailKit.MeshData();
        // 描画に使った左右の中心線をそのまま保持する。列車の走行経路(Train.BuildLeg)も
        // これを使うため、レールと列車の通り道が原理的にズレない
        sidePlus = SideCentre(TrackOffset);
        sideMinus = SideCentre(-TrackOffset);
        RailKit.AddTrack(bed, rail, support, sidePlus, bedType, detail);
        RailKit.AddTrack(bed, rail, support, sideMinus, bedType, detail);

        // 渡り線は駅前(スロートのリード)に駅側で描く。segmentには描かない
        length = Vector3.Distance(EndA, EndB);
        bool slab = bedType == TrackBedType.Slab;
        RailKit.MeshGO(slab ? "Slab" : "Ballast", bed.ToMesh(),
            MatLib.Get(slab ? "StationHouse" : "Ballast"), go.transform);
        RailKit.MeshGO("Rail", rail.ToMesh(), MatLib.Get("Rail"), go.transform);
        RailKit.MeshGO(slab ? "SlabSupport" : "Tie", support.ToMesh(),
            MatLib.Get(slab ? "Platform" : "Tie"), go.transform);
        if (detail.v.Count > 0)
            RailKit.MeshGO("SlabDetail", detail.ToMesh(), MatLib.Get("Switch"), go.transform);
    }

    // A端→B端の中心線。両駅それぞれの発着方向(Axis*sign)へ、実際の鉄道のように
    // 一定半径に近い滑らかな曲線(Train.BuildLegの駅間区間と同じ規約)で繋ぐ。
    // 直線Lerpだと、駅同士が斜めに向き合っている場合に駅を出た瞬間で折れ曲がって
    // 見えてしまうため
    public List<Vector3> CenterPoints()
    {
        var p0 = EndA;
        var p1 = EndB;
        float d = Vector3.Distance(p0, p1);
        int n = Mathf.Max(16, Mathf.CeilToInt(d / 15f));
        return RailKit.SmoothConnectPath(p0, a.Axis * signA, p1, -(b.Axis * signB), n);
    }

    // 描画に使った左右の中心線(ワールド)。Build後に有効
    List<Vector3> sidePlus, sideMinus;

    // 中心線からlateralだけ横へずらした線(=実際に敷かれるレールの中心)。
    // 端点の法線は近傍点からの近似ではなく駅の発着方向そのものを使い、
    // 駅の自前スロートの線路と隙間なく繋がるようにする
    // 平滑化までここで済ませて返す。これが「敷かれたレールそのもの」であり、
    // 描画も走行経路もこの結果をそのまま使う(受け取った側が掛け直すと、
    // レールと列車の通り道がズレる)
    public List<Vector3> SideCentre(float lateral)
    {
        Vector3 tan0 = a.Axis * signA, tan1 = -(b.Axis * signB);
        return RailKit.Chaikin(RailKit.OffsetWithEndTangents(CenterPoints(), lateral, tan0, tan1), 2);
    }

    // 駅stの側から見て、本線側offset(±TrackOffset)に対応する描画済みの中心線を、
    // st発の進行方向に並べて返す。走行経路はこれをそのまま使う
    public List<Vector3> SideCentreFrom(Station st, float lateralAtStart)
    {
        var plus = sidePlus ?? SideCentre(TrackOffset);
        var minus = sideMinus ?? SideCentre(-TrackOffset);
        // lateralAtStartはst側の駅ローカルxで指定されるため、どちらの線かを
        // 「st側の端点がその値に近いか」で選ぶ
        float dPlus = Mathf.Abs(st.transform.InverseTransformPoint(EndNearest(plus, st)).x - lateralAtStart);
        float dMinus = Mathf.Abs(st.transform.InverseTransformPoint(EndNearest(minus, st)).x - lateralAtStart);
        var chosen = new List<Vector3>(dPlus <= dMinus ? plus : minus);
        // st側が先頭に来るよう並べ替える
        if (Vector3.Distance(chosen[0], st.transform.position) >
            Vector3.Distance(chosen[chosen.Count - 1], st.transform.position))
            chosen.Reverse();
        return chosen;
    }

    static Vector3 EndNearest(List<Vector3> pts, Station st)
        => Vector3.Distance(pts[0], st.transform.position) <=
           Vector3.Distance(pts[pts.Count - 1], st.transform.position) ? pts[0] : pts[pts.Count - 1];

    // 複線の道床が中心線から左右へ張り出す量(線間±2.3 + 道床肩)。
    // 途中駅のホームを踏むかどうかの判定に使う
    public const float TrackOffset = 2.3f;
    public const float BedHalfWidth = 2.5f;
    public const float HalfCorridorWidth = TrackOffset + BedHalfWidth;

    // この区間(a↔b)を敷いた場合に、道床が平面上でホームを踏んでしまう駅を返す。
    // 両端の駅自身は当然自分のホーム際を通るので除外する。踏まなければnull。
    // Build()前でも呼べるよう、GameObjectではなくCenterPoints()の形状だけで判定する
    public Station FindStationCrossedByBed()
    {
        var center = CenterPoints();
        foreach (var st in TrackNetwork.stations)
        {
            if (st == null || st == a || st == b || st.preview) continue;
            // 曲線の折れ目を跨いで踏むこともあるため、点列の間も補間して見る
            for (int i = 0; i + 1 < center.Count; i++)
            {
                const int sub = 4;
                for (int k = 0; k < sub; k++)
                {
                    var p = Vector3.Lerp(center[i], center[i + 1], k / (float)sub);
                    if (st.PlatformAreaContains(p, HalfCorridorWidth)) return st;
                }
            }
        }
        return null;
    }
}

// 駅と線路の台帳+到達可能判定
public static class TrackNetwork
{
    public static readonly List<Station> stations = new List<Station>();
    public static readonly List<TrackSegment> segments = new List<TrackSegment>();
    // 列車の安定した中央リスト(登録順)。stations/segmentsと同じく、生成側が
    // 明示的にAdd/Removeする(Train.OnEnable/OnDisableには依存しない)。
    // 【M2-B.1での変更理由】OnEnable/OnDisableでの自己登録を試みたが、EditModeの
    // Unity Test Framework実行下ではAddComponent直後にOnEnableが確実に発火しない
    // ことが判明した(PlayModeでは正常に発火する。既存コードのTrackTest.csにも
    // 同種の既知の注記「EditモードはAwakeが呼ばれない」がある)。このためTrain生成・
    // 破棄の全箇所(BuildController.DispatchTrain/RemoveStation/DeleteLine、
    // SaveLoad.Load)で明示的にAdd/Removeする方式に統一した。
    // Bootstrap.SimTickがここを固定順で回すことで、Unity既定のUpdate呼び出し順に
    // 依存しない決定的なtick処理を実現する
    public static readonly List<Train> trains = new List<Train>();
    public static int nameCounter;

    // M2-C: 駅・線路・列車の安定ID発行カウンタ。ServiceLine.id/Services.idCounter
    // (既存の運行系統の安定ID方式)と同じ「型ごとの単調増加int」パターンを踏襲する。
    // 0は「未割当」を表す不正値として予約するため、次に発行するIDは常に(++counter)
    public static int stationIdCounter;
    public static int segmentIdCounter;
    public static int trainIdCounter;

    static readonly Dictionary<Station, HashSet<Station>> reachCache = new Dictionary<Station, HashSet<Station>>();

    public static void Clear()
    {
        stations.Clear();
        segments.Clear();
        trains.Clear();
        reachCache.Clear();
        nameCounter = 0;
        stationIdCounter = 0;
        segmentIdCounter = 0;
        trainIdCounter = 0;
    }

    public static void MarkDirty() => reachCache.Clear();

    public static TrackSegment Find(Station x, Station y)
    {
        foreach (var s in segments)
            if ((s.a == x && s.b == y) || (s.a == y && s.b == x)) return s;
        return null;
    }

    public static bool Connected(Station x, Station y) => Find(x, y) != null;

    public static Station StationById(int id)
    {
        if (id == 0) return null;
        foreach (var s in stations) if (s.id == id) return s;
        return null;
    }

    public static TrackSegment SegmentById(int id)
    {
        if (id == 0) return null;
        foreach (var s in segments) if (s.id == id) return s;
        return null;
    }

    public static Train TrainById(int id)
    {
        if (id == 0) return null;
        foreach (var t in trains) if (t.id == id) return t;
        return null;
    }

    // sと同じ連結成分の他駅(乗客の行き先候補)
    public static HashSet<Station> Reachable(Station s)
    {
        HashSet<Station> r;
        if (reachCache.TryGetValue(s, out r)) return r;
        r = new HashSet<Station>();
        var q = new Queue<Station>();
        var seen = new HashSet<Station> { s };
        q.Enqueue(s);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            foreach (var seg in segments)
            {
                if (seg.a != cur && seg.b != cur) continue;
                var o = seg.Other(cur);
                if (seen.Add(o))
                {
                    r.Add(o);
                    q.Enqueue(o);
                }
            }
        }
        reachCache[s] = r;
        return r;
    }

    // 経由駅(通過駅)を含む経路探索用。fromからtoまでの実際のホップ列
    // (どの駅をどのセグメントで通るか)を、Reachableと同じホップ数ベースのBFSで求める。
    // 距離による重み付けはしない(既存の「隣接=1ホップ」という考え方と一貫させるため)。
    // 到達不能ならnull、from==toなら空リストを返す
    public readonly struct PathHop
    {
        public readonly Station station; // このホップで到達する駅(起点fromは含まない)
        public readonly TrackSegment seg; // このホップで使うセグメント
        public PathHop(Station station, TrackSegment seg) { this.station = station; this.seg = seg; }
    }

    public static List<PathHop> FindPath(Station from, Station to)
    {
        if (from == to) return new List<PathHop>();
        var prev = new Dictionary<Station, PathHop>();
        var seen = new HashSet<Station> { from };
        var q = new Queue<Station>();
        q.Enqueue(from);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            foreach (var seg in segments)
            {
                if (seg.a != cur && seg.b != cur) continue;
                var o = seg.Other(cur);
                if (!seen.Add(o)) continue;
                prev[o] = new PathHop(cur, seg);
                if (o == to)
                {
                    var hops = new List<PathHop>();
                    var node = to;
                    while (node != from)
                    {
                        var hop = prev[node];
                        hops.Add(new PathHop(node, hop.seg));
                        node = hop.station;
                    }
                    hops.Reverse();
                    return hops;
                }
                q.Enqueue(o);
            }
        }
        return null;
    }
}
