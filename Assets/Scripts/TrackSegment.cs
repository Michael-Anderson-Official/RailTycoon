using System.Collections.Generic;
using UnityEngine;

// 駅間を結ぶ複線区間。両駅のスロート収束点(End)同士を直線で結ぶ
public class TrackSegment
{
    public int id; // M2-C: セーブ/ロードを跨いで安定な識別子。0は未割当
    public Station a, b;
    public int signA, signB;  // 各駅のどちらの端に接続するか(±1)
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
        var ballast = new RailKit.MeshData();
        var rail = new RailKit.MeshData();
        var tie = new RailKit.MeshData();
        var center = CenterPoints();
        // 端点の法線は近傍点からの近似(NormalAt)ではなく、駅の発着方向そのもの
        // (CenterPointsのエルミート曲線に渡したのと同じ接線)を使い、駅の自前スロートの
        // 線路と隙間なく繋がるようにする
        Vector3 tan0 = a.Axis * signA, tan1 = -(b.Axis * signB);
        RailKit.AddTrack(ballast, rail, tie, RailKit.OffsetWithEndTangents(center, 2.3f, tan0, tan1));
        RailKit.AddTrack(ballast, rail, tie, RailKit.OffsetWithEndTangents(center, -2.3f, tan0, tan1));

        // 渡り線は駅前(スロートのリード)に駅側で描く。segmentには描かない
        length = Vector3.Distance(EndA, EndB);
        RailKit.MeshGO("Ballast", ballast.ToMesh(), MatLib.Get("Ballast"), go.transform);
        RailKit.MeshGO("Rail", rail.ToMesh(), MatLib.Get("Rail"), go.transform);
        RailKit.MeshGO("Tie", tie.ToMesh(), MatLib.Get("Tie"), go.transform);
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
}
