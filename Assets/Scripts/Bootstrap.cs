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

        UIController.Toast("駅を建て、線路でつなぎ、列車を走らせよう!");
    }

    // 地面と光。エディタのSnapshotからも使う
    public static GameObject BuildEnvironment()
    {
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.52f, 0.55f, 0.58f);

        var root = new GameObject("Environment");

        var lightGo = new GameObject("Sun");
        lightGo.transform.SetParent(root.transform, false);
        var sun = lightGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.15f;
        sun.color = new Color(1f, 0.97f, 0.9f);
        sun.shadows = LightShadows.Soft;
        lightGo.transform.rotation = Quaternion.Euler(52f, -35f, 0);

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
        var baseC = new Color(0.66f, 0.78f, 0.58f);
        var lineC = new Color(0.60f, 0.72f, 0.53f);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
                tex.SetPixel(x, y, (x == 0 || y == 0) ? lineC : baseC);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Repeat;
        return tex;
    }

    void Update()
    {
        float dtMin = Time.deltaTime * GameState.timeScale;
        GameState.gameMinutes += dtMin;
        foreach (var st in TrackNetwork.stations) st.Tick(dtMin);
    }
}
