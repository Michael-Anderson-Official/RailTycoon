using System.Collections.Generic;
using UnityEngine;

// 駅のホームに立つ人。**完全に視覚専用の層**で、シミュレーションには一切触れない。
// GameRandomも使わない(使うと決定性が壊れ、速度倍率ごとの一致テストが落ちる)。
// 人数はStation.TotalWaiting(行き先別の待ち客の合計)から毎回導出するので、
// セーブデータには何も足さない。
//
// 「待ち客は間引かず全員描く」方針(ユーザー指定)。実機では待600人が普通に出るため、
// カメラからの距離で描き方を3段階に分ける:
//   近い  … 1人ずつGameObjectを持ち、歩く・乗る・降りる
//   中間  … 全員を1枚の結合メッシュで静止描画(動かないが人数は正しい)
//   遠い  … 描かない(その距離では点にもならない)
public class StationCrowd
{
    // ---- 調整値 ----
    const float AnimatedDistance = 320f;   // ここより近ければ1人ずつ動かす
    const float VisibleDistance = 1400f;   // ここより遠ければ描かない
    const int AnimatedMax = 140;           // 個別に動かす上限(残りは静止メッシュ)
    const float WalkSpeed = 1.25f;         // 歩く速さ(m/s)
    const float BoardSpeed = 1.9f;         // 乗車・降車で急ぐときの速さ
    const float BodyHeight = 1.15f;
    const int StaticRebuildSlack = 8;      // 静止メッシュを作り直す人数の変化幅

    enum PState : byte { Idle, Boarding, Leaving }

    struct Person
    {
        public Vector2 pos;        // 駅ローカル(x, z)
        public Vector2 target;
        public float waitT;        // 次の目的地を決めるまでの残り
        public PState state;
        public byte platform;      // 所属するホームのindex
        public byte tint;          // 服の色(0..PaletteSize-1)
        public uint rng;
    }

    readonly Station st;
    readonly List<Person> people = new List<Person>();
    uint spawnRng;

    GameObject root;
    readonly List<Transform> animated = new List<Transform>();
    GameObject[] staticGo;
    int staticBuiltFor = -1;
    bool lastAnimated;

    static Mesh personMesh;
    const int PaletteSize = 4;
    static Material[] palette;

    public StationCrowd(Station station)
    {
        st = station;
        // 駅ごとに固定の種。GameRandomとは無関係なので決定性へ影響しない
        spawnRng = (uint)(station.id * 2654435761u + 12345u) | 1u;
    }

    public int Count => people.Count;

    // ---- 乗降の通知(Trainから。視覚だけに使う) ----

    // n人がこの番線から乗車した。ホーム上の人をドアへ歩かせ、着いたら消す
    public void NotifyBoarded(int track, int n)
    {
        if (n <= 0) return;
        int pi = PlatformFacing(track);
        if (pi < 0) return;
        float edgeX = BoardingX(track, pi);
        int marked = 0;
        for (int i = 0; i < people.Count && marked < n; i++)
        {
            var p = people[i];
            if (p.state != PState.Idle || p.platform != pi) continue;
            p.state = PState.Boarding;
            p.target = new Vector2(edgeX, NearestDoorZ(p.pos.y));
            people[i] = p;
            marked++;
        }
    }

    // n人がこの番線で降りた。ドア位置に現れて階段(中央)へ歩き、着いたら消す
    public void NotifyAlighted(int track, int n)
    {
        if (n <= 0) return;
        int pi = PlatformFacing(track);
        if (pi < 0) return;
        // 降車客は待ち客に含まれないので、増えすぎないよう控えめに出す
        int add = Mathf.Min(n, 80);
        float edgeX = BoardingX(track, pi);
        float inner = InnerX(pi);
        for (int k = 0; k < add; k++)
        {
            var p = NewPerson(pi);
            p.state = PState.Leaving;
            p.pos = new Vector2(edgeX, DoorZ(k));
            // 出口(中央の階段)へ向かうが、遠い端から出た客は近い側の端へ抜ける。
            // 全員を中央へ歩かせるとホーム端の客が100m近く歩くことになる
            float exitZ = Mathf.Abs(p.pos.y) > st.cars * StationLayout.CarLength * 0.3f
                ? Mathf.Sign(p.pos.y) * st.cars * StationLayout.CarLength * 0.42f
                : Mathf.Lerp(-6f, 6f, Frac(ref p.rng));
            p.target = new Vector2(inner, exitZ);
            people.Add(p);
        }
    }

    // ---- 毎フレーム(tick消化後)の更新 ----

    public void Update(float dt)
    {
        if (st == null || st.preview || st.layout.platforms == null
            || st.layout.platforms.Count == 0) { Clear(); return; }

        SyncCount();
        Move(dt);
        Draw();
    }

    // 待ち客の人数へ合わせる。乗車中・降車中の人はここでは触らない
    // (乗車はシミュレーション側で即座にwaitingが減るが、見た目はドアまで歩かせたい)
    void SyncCount()
    {
        int want = st.TotalWaiting;
        int idle = 0;
        for (int i = 0; i < people.Count; i++) if (people[i].state == PState.Idle) idle++;

        if (idle < want)
        {
            int add = want - idle;
            // 一度に湧きすぎると不自然なので、1回の更新で足す数を抑える
            add = Mathf.Min(add, 60);
            for (int k = 0; k < add; k++) people.Add(NewPerson(PickPlatform()));
        }
        else if (idle > want)
        {
            int remove = idle - want;
            for (int i = people.Count - 1; i >= 0 && remove > 0; i--)
                if (people[i].state == PState.Idle) { people.RemoveAt(i); remove--; }
        }
    }

    void Move(float dt)
    {
        if (dt <= 0f) return;
        for (int i = people.Count - 1; i >= 0; i--)
        {
            var p = people[i];
            float speed = p.state == PState.Idle ? WalkSpeed : BoardSpeed;
            Vector2 d = p.target - p.pos;
            float len = d.magnitude;

            if (len <= speed * dt)
            {
                p.pos = p.target;
                if (p.state != PState.Idle) { people.RemoveAt(i); continue; }
                // 待っている人は、少し待ってからホーム上の別の場所へ歩く
                p.waitT -= dt;
                if (p.waitT <= 0f)
                {
                    p.target = WanderTarget(p.platform, ref p.rng);
                    p.waitT = 2f + Frac(ref p.rng) * 8f;
                }
            }
            else
            {
                p.pos += d / len * (speed * dt);
            }
            people[i] = p;
        }
    }

    // ---- 位置の決め方 ----

    int PickPlatform()
    {
        int n = st.layout.platforms.Count;
        return n <= 1 ? 0 : (int)(Frac(ref spawnRng) * n) % n;
    }

    Person NewPerson(int platformIndex)
    {
        var p = new Person
        {
            state = PState.Idle,
            platform = (byte)Mathf.Clamp(platformIndex, 0, st.layout.platforms.Count - 1),
            rng = spawnRng ^ (uint)(people.Count * 2246822519u + 1u),
        };
        p.rng |= 1u;
        p.tint = (byte)((int)(Frac(ref p.rng) * PaletteSize) % PaletteSize);
        p.pos = WanderTarget(p.platform, ref p.rng);
        p.target = WanderTarget(p.platform, ref p.rng);
        p.waitT = Frac(ref p.rng) * 6f;
        return p;
    }

    // ホーム上で立てる範囲。線路側の帯(警戒線・点字ブロック)へは出さない
    Vector2 WanderTarget(int pi, ref uint rng)
    {
        var pl = st.layout.platforms[pi];
        float visualW = Mathf.Max(2.6f, pl.y - 0.02f);
        float half = Mathf.Max(0.3f, visualW * 0.5f - 1.35f);
        float platLen = st.cars * StationLayout.CarLength;
        return new Vector2(
            pl.x + (Frac(ref rng) * 2f - 1f) * half,
            (Frac(ref rng) * 2f - 1f) * platLen * 0.46f);
    }

    // 番線に面しているホームのindex(無ければ-1)
    int PlatformFacing(int track)
    {
        foreach (var e in st.layout.edges)
            if (e.trackIndex == track) return e.platformIndex;
        return -1;
    }

    // 乗降位置(ホーム縁のすぐ内側)のx
    float BoardingX(int track, int pi)
    {
        var pl = st.layout.platforms[pi];
        float visualW = Mathf.Max(2.6f, pl.y - 0.02f);
        float trackX = st.layout.trackOffsets[track];
        // ホーム中心から見て線路のある側へ寄せる
        float dir = Mathf.Sign(trackX - pl.x);
        return pl.x + dir * (visualW * 0.5f - 0.9f);
    }

    // ホームの内側(線路と反対寄り)のx
    float InnerX(int pi)
    {
        var pl = st.layout.platforms[pi];
        float visualW = Mathf.Max(2.6f, pl.y - 0.02f);
        int away = Station.FurnitureAwayDirection(st.layout, pi);
        return pl.x + away * Mathf.Max(0f, visualW * 0.5f - 1.6f);
    }

    // 車両のドア位置(20mごとに4つ)。zに一番近いものへ寄せる
    float NearestDoorZ(float z)
    {
        float car = StationLayout.CarLength;
        float k = Mathf.Round(z / car);
        float baseZ = k * car;
        float best = baseZ; float bestD = float.MaxValue;
        foreach (float dz in DoorOffsets)
        {
            float d = Mathf.Abs(baseZ + dz - z);
            if (d < bestD) { bestD = d; best = baseZ + dz; }
        }
        return best;
    }

    static readonly float[] DoorOffsets = { -6.4f, -2.15f, 2.15f, 6.4f };

    // 降車客の湧き出し位置。**編成全体のドアへ散らす**。
    // 先頭から詰めて出すと、後ろの車両から誰も降りてこない上に、
    // ホーム端から出た客が中央まで100m近く歩くことになる
    float DoorZ(int k)
    {
        float car = StationLayout.CarLength;
        int cars = Mathf.Max(1, st.cars);
        int slots = cars * DoorOffsets.Length;
        int slot = (k * 7 + k / slots) % slots;   // 隣り合わないよう飛ばして配る
        int carIdx = slot / DoorOffsets.Length;
        float platLen = cars * car;
        float z = -platLen * 0.5f + car * (carIdx + 0.5f) + DoorOffsets[slot % DoorOffsets.Length];
        return Mathf.Clamp(z, -platLen * 0.5f, platLen * 0.5f);
    }

    // ---- 描画 ----

    void Draw()
    {
        var cam = Camera.main;
        float dist = cam == null ? 0f
            : Vector3.Distance(cam.transform.position, st.transform.position);
        if (dist > VisibleDistance) { Hide(); return; }

        EnsureRoot();
        bool useAnimated = dist <= AnimatedDistance;
        if (useAnimated != lastAnimated) { ClearAnimated(); ClearStatic(); lastAnimated = useAnimated; }

        int animatedCount = useAnimated ? Mathf.Min(people.Count, AnimatedMax) : 0;
        DrawAnimated(animatedCount);
        DrawStatic(animatedCount);
    }

    void DrawAnimated(int count)
    {
        while (animated.Count < count)
        {
            var go = new GameObject("Person");
            go.transform.SetParent(root.transform, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = PersonMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Palette()[animated.Count % PaletteSize];
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            animated.Add(go.transform);
        }
        for (int i = 0; i < animated.Count; i++)
        {
            bool on = i < count;
            if (animated[i].gameObject.activeSelf != on) animated[i].gameObject.SetActive(on);
            if (!on) continue;
            var p = people[i];
            animated[i].localPosition = new Vector3(p.pos.x, RailDimensions.PlatformTop, p.pos.y);
            Vector2 d = p.target - p.pos;
            if (d.sqrMagnitude > 1e-4f)
                animated[i].localRotation = Quaternion.Euler(0,
                    Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg, 0);
        }
    }

    // 個別に動かさないぶんを1枚の結合メッシュで描く。人数が大きく変わった時だけ作り直す
    void DrawStatic(int skip)
    {
        int rest = people.Count - skip;
        if (rest <= 0) { ClearStatic(); staticBuiltFor = 0; return; }
        if (staticGo != null && Mathf.Abs(rest - staticBuiltFor) <= StaticRebuildSlack) return;

        ClearStatic();
        var md = new RailKit.MeshData[PaletteSize];
        for (int i = 0; i < PaletteSize; i++) md[i] = new RailKit.MeshData();
        for (int i = skip; i < people.Count; i++)
        {
            var p = people[i];
            AddPerson(md[p.tint % PaletteSize],
                new Vector3(p.pos.x, RailDimensions.PlatformTop, p.pos.y));
        }
        staticGo = new GameObject[PaletteSize];
        for (int i = 0; i < PaletteSize; i++)
        {
            if (md[i].v.Count == 0) continue;
            staticGo[i] = RailKit.MeshGO("CrowdStatic" + i, md[i].ToMesh(), Palette()[i], root.transform);
            var mr = staticGo[i].GetComponent<MeshRenderer>();
            if (mr != null) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        staticBuiltFor = rest;
    }

    static void AddPerson(RailKit.MeshData md, Vector3 at)
    {
        RailKit.AddBox(md, at + new Vector3(0, BodyHeight * 0.5f, 0),
            new Vector3(0.42f, BodyHeight, 0.28f), Quaternion.identity);
        RailKit.AddBox(md, at + new Vector3(0, BodyHeight + 0.11f, 0),
            new Vector3(0.22f, 0.22f, 0.22f), Quaternion.identity);
    }

    static Mesh PersonMesh()
    {
        if (personMesh != null) return personMesh;
        var md = new RailKit.MeshData();
        AddPerson(md, Vector3.zero);
        personMesh = md.ToMesh();
        return personMesh;
    }

    static Material[] Palette()
    {
        if (palette != null) return palette;
        palette = new[]
        {
            MatLib.Tinted("StationHouse", new Color(0.20f, 0.24f, 0.34f)),
            MatLib.Tinted("StationHouse", new Color(0.52f, 0.30f, 0.28f)),
            MatLib.Tinted("StationHouse", new Color(0.30f, 0.36f, 0.30f)),
            MatLib.Tinted("StationHouse", new Color(0.62f, 0.60f, 0.56f)),
        };
        return palette;
    }

    void EnsureRoot()
    {
        // Hide()で非表示にしたまま戻ってきた場合に表示へ戻す。
        // 「rootがあるから何もしない」で済ませると、一度遠ざかった駅の人が
        // 二度と現れない(実装後レビューでCodex CLIが指摘)
        if (root != null)
        {
            if (!root.activeSelf) root.SetActive(true);
            return;
        }
        // Station.Build()が子を全消しするので、消えていたら作り直す
        root = new GameObject("Crowd");
        root.transform.SetParent(st.transform, false);
        animated.Clear();
        staticGo = null;
        staticBuiltFor = -1;
    }

    void Hide()
    {
        if (root != null && root.activeSelf) root.SetActive(false);
    }

    void ClearAnimated()
    {
        foreach (var t in animated) if (t != null) DestroySafe(t.gameObject);
        animated.Clear();
    }

    void ClearStatic()
    {
        if (staticGo == null) return;
        foreach (var g in staticGo) if (g != null) DestroySafe(g);
        staticGo = null;
        staticBuiltFor = -1;
    }

    public void Clear()
    {
        people.Clear();
        ClearAnimated();
        ClearStatic();
        if (root != null) DestroySafe(root);
        root = null;
    }

    static void DestroySafe(GameObject go)
    {
        if (go == null) return;
        if (Application.isPlaying) Object.Destroy(go);
        else Object.DestroyImmediate(go);
    }

    // 視覚専用の乱数(xorshift)。GameRandomには絶対に触れない
    static float Frac(ref uint s)
    {
        s ^= s << 13; s ^= s >> 17; s ^= s << 5;
        return (s & 0xFFFFFF) / (float)0x1000000;
    }
}
