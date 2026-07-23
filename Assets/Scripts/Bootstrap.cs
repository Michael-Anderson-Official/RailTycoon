using UnityEngine;
using UnityEngine.EventSystems;

// シーンにはこれ1つだけ置く。地面・光源・カメラ・UI・コントローラを実行時に組み立てる
public class Bootstrap : MonoBehaviour
{
    // 固定tick間隔。Awakeで Application.targetFrameRate=60 を指定しており、
    // 既存の列車速度・加減速・乗客生成レートはこの前提で調整されてきたため、
    // それに最も近い刻みを採用し既存バランスへの影響を最小化する
    //
    // 【tick頻度と速度倍率(GameState.timeScale)の関係(M2-B.1で明文化)】
    // tick頻度(1秒あたりのSimTick呼出回数)はTime.unscaledDeltaTimeのみで決まり、
    // GameState.timeScaleには一切依存しない(SimTick()内のsimDt計算でのみ登場する)。
    // つまり×1/×5/×20いずれでも、実時間1秒あたりに必要なtickは同じ(最大60回/秒、
    // 実フレームレート次第)。速度倍率は「1tickが表すシミュレーション秒数」だけを
    // 増やす(×20なら1tick=1/60*20=1/3シミュレーション秒)。
    // 実測: 京王5000系(vmax=110km/h, decel=4.0km/h/s)基準で、×20時の1tick移動距離は
    // 約10.2m、停止制御(vAllow=√(2*decel*rem)を毎tick再計算する閉ループ式)による
    // 最大オーバーシュートは d*dt²/2 ≈ 6.17cmで実用上問題ない
    // (Codex CLIによる独立検証・数式で確認済み、2026-07-23)。
    //
    // 【オーバーロードの分類】
    // - 通常の低FPS(例: 安定20fps): 1フレームあたり最大 20/60≈1.33→2tick程度。切り捨てなし
    // - 一時的なフレーム落ち(単発の重いフレーム): 単発なら8tick(133ms)以内に収まることが多く
    //   切り捨てなし。133ms(≈7.5fps相当)を単発で超えた場合のみ、そのフレームだけ切り捨てが発生
    // - 数秒以上のアプリ停止(一時停止/バックグラウンド化等): 復帰後最初のAdvance呼び出しで
    //   ほぼ確実に上限到達し、停止していた実時間のほぼ全てが切り捨てられる(意図した挙動。
    //   Application.runInBackground=trueのためバックグラウンド中も本来Updateは走り得るが、
    //   一時停止(GameState.paused)中はaccumulatorへの加算自体を止めているため無関係)
    // - ×20実行中の処理超過: 上記のとおりtick頻度は速度倍率と無関係なため、「×20だから
    //   起きやすい」という追加リスクは無い。オーバーロードの発生条件は速度倍率によらず
    //   常に「実フレームレート」のみで決まる
    public const float TickSeconds = 1f / 60f;
    // 1フレームで消化する最大tick数。60Hz基準で約133ms分の遅延まで同一フレームで
    // 消化する。これを超える遅延(フレーム落ち)ではシミュレーション時間の一部を
    // 切り捨てるため、その場合に限り「同じ総経過時間でも結果が一致する」保証の
    // 対象外となる(デススパイラル防止を優先)
    public const int MaxTicksPerFrame = 8;

    readonly FixedStepAccumulator accumulator = new FixedStepAccumulator(TickSeconds, MaxTicksPerFrame);

    // オーバーロードの可観測性(デバッグ用)。黙って時間を捨てず、UIやログから参照できるようにする
    public int OverloadCount => accumulator.MaxTicksReachedCount;
    public int DroppedTickCount => accumulator.DroppedTickCount;
    public float DroppedSimulationTime => accumulator.DroppedSimulationTime;
    public float BacklogTime => accumulator.Accumulator;

    void Awake()
    {
        // 実プレイでは毎回変わる種を採用(テストはGameRandom.Seed()で明示的に固定して使う)
        GameRandom.Seed(unchecked((uint)System.DateTime.Now.Ticks));
        Application.targetFrameRate = 60;
        QualitySettings.shadowDistance = 700f;
        BuildEnvironment();
        CityGrid.Init();

        var camGo = new GameObject("Main Camera");
        var rig = camGo.AddComponent<CameraRig>();
        rig.Setup();

        gameObject.AddComponent<BuildController>();

        var esGo = new GameObject("EventSystem");
        esGo.AddComponent<EventSystem>();
        esGo.AddComponent<StandaloneInputModule>();

        var uiGo = new GameObject("Canvas");
        var ui = uiGo.AddComponent<UIController>();
        ui.rig = rig;
        ui.Build();

        SaveLoad.suppress = false;
        if (SaveLoad.Load())
        {
            if (TrackNetwork.stations.Count > 0)
                rig.target = TrackNetwork.stations[0].transform.position;
            UIController.Toast("前回の続きから再開しました(自動保存中)");
        }
        else
        {
            UIController.Toast("駅を建て、線路でつなぎ、列車を走らせよう!");
        }
    }

    float saveTimer;

    // 地面と光。エディタのSnapshotからも使う
    public static GameObject BuildEnvironment()
    {
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.50f, 0.56f, 0.64f);
        RenderSettings.ambientEquatorColor = new Color(0.44f, 0.46f, 0.46f);
        RenderSettings.ambientGroundColor = new Color(0.30f, 0.33f, 0.28f);
        // 遠景だけをうっすら霞ませて奥行きを出す(近くは白飛びさせない)
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.80f, 0.86f, 0.90f);
        RenderSettings.fogStartDistance = 2800f;
        RenderSettings.fogEndDistance = 8200f;

        var root = new GameObject("Environment");

        var lightGo = new GameObject("Sun");
        lightGo.transform.SetParent(root.transform, false);
        var sun = lightGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.08f;
        sun.color = new Color(1f, 0.96f, 0.88f);
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.7f;
        lightGo.transform.rotation = Quaternion.Euler(48f, -40f, 0);

        var md = new RailKit.MeshData();
        const float S = 2000f;
        md.v.Add(new Vector3(-S, 0, -S));
        md.v.Add(new Vector3(S, 0, -S));
        md.v.Add(new Vector3(-S, 0, S));
        md.v.Add(new Vector3(S, 0, S));
        md.t.AddRange(new[] { 0, 2, 3, 0, 3, 1 });
        var mesh = md.ToMesh();
        mesh.SetUVs(0, new System.Collections.Generic.List<Vector2>
        {
            new Vector2(0, 0), new Vector2(200, 0), new Vector2(0, 200), new Vector2(200, 200),
        });
        var mat = MatLib.Get("Ground");
        mat.mainTexture = MakeGridTexture();
        RailKit.MeshGO("Ground", mesh, mat, root.transform);
        return root;
    }

    static Texture2D MakeGridTexture()
    {
        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGB24, true);
        var baseC = new Color(0.45f, 0.58f, 0.38f);
        var lineC = new Color(0.38f, 0.50f, 0.33f);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
                tex.SetPixel(x, y, (x == 0 || y == 0) ? lineC : baseC);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Repeat;
        return tex;
    }

    void Update()
    {
        RunFrame(Time.unscaledDeltaTime);

        saveTimer += Time.unscaledDeltaTime;
        if (saveTimer >= 15f)
        {
            saveTimer = 0;
            if (TrackNetwork.stations.Count > 0) SaveLoad.Save();
        }
    }

    // 1フレーム分の進行。一時停止中はaccumulatorに実時間を積まない(解除時にまとめて
    // 進む/暴走するのを防ぐ)ため0を返す。Time.timeScale(Unity組込み)ではなく
    // unscaledDeltaSecondsを直接受け取ることで、ゲーム独自のGameState.timeScaleと
    // 完全に独立させ、かつテストから任意のdelta値を与えて検証できるようにする。
    // 戻り値は今回消化したtick数
    public int RunFrame(float unscaledDeltaSeconds)
    {
        if (GameState.paused) return 0;
        int ticks = accumulator.Advance(unscaledDeltaSeconds);
        for (int i = 0; i < ticks; i++) SimTick(TickSeconds);
        if (ticks > 0) foreach (var t in TrackNetwork.trains) t.PlaceCars();
        return ticks;
    }

    // 固定tick1回分のシミュレーション本体。処理順序は固定: 時計→駅(乗客生成)→列車(移動・到着・乗降)。
    // Bootstrap.Updateのaccumulatorから、または将来のテストから直接呼べる
    public void SimTick(float tickSeconds)
    {
        float simDt = tickSeconds * GameState.timeScale; // 列車と同じシミュレーション秒基準(×1で実速度)
        GameState.gameMinutes += simDt / 60f;             // 時計も同じ基準(×1=1秒で1秒進む)
        foreach (var st in TrackNetwork.stations) st.Tick(simDt);
        foreach (var t in TrackNetwork.trains) t.SimTick(simDt);
    }

    void LateUpdate() => CityGrid.FlushIfDirty();
}
