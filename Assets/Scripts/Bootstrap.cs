using UnityEngine;
using UnityEngine.EventSystems;

// シーンにはこれ1つだけ置く。地面・光源・カメラ・UI・コントローラを実行時に組み立てる
public class Bootstrap : MonoBehaviour
{
    void Awake()
    {
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
        float dtSec = Time.deltaTime * GameState.timeScale;      // 列車と同じ実時間基準(×1で実速度)
        GameState.gameMinutes += dtSec / 60f;                    // 時計も実時間(×1=1秒で1秒進む)
        foreach (var st in TrackNetwork.stations) st.Tick(dtSec);

        saveTimer += Time.unscaledDeltaTime;
        if (saveTimer >= 15f)
        {
            saveTimer = 0;
            if (TrackNetwork.stations.Count > 0) SaveLoad.Save();
        }
    }

    void LateUpdate() => CityGrid.FlushIfDirty();
}
