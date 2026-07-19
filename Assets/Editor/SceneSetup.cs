using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// バッチから -executeMethod SceneSetup.CreateAll で呼ぶ。
// マテリアルアセット(Resources/Materials)とMain.unityを生成する。
// マテリアルは実行時生成だとWebGLのシェーダーストリッピングで消えるためアセット化する
public static class SceneSetup
{
    public static void CreateAll()
    {
        CreateMaterials();
        EnsureShaders();
        CreateScene();
        PlayerSettings.productName = "レールタイクーン";
        PlayerSettings.companyName = "MichaelAnderson";
        PlayerSettings.runInBackground = true;
        AssetDatabase.SaveAssets();
        Debug.Log("SceneSetup: done");
        EditorApplication.Exit(0);
    }

    static void CreateMaterials()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Materials"))
            AssetDatabase.CreateFolder("Assets/Resources", "Materials");
        Mat("Ground", Color.white, 0f, 0.05f);
        Mat("Ballast", Hex("8A8577"), 0f, 0.1f);
        Mat("Rail", Hex("A8B0B8"), 0.85f, 0.6f);
        Mat("Tie", Hex("4C453C"), 0f, 0.1f);
        Mat("Platform", Hex("CCC6B8"), 0f, 0.2f);
        Mat("Canopy", Hex("5E6B77"), 0.3f, 0.4f);
        Mat("StationHouse", Hex("DAD5CA"), 0f, 0.3f);
        Mat("Building", Hex("C3BDB2"), 0f, 0.25f);
        Mat("BuildingHigh", Hex("8FB4BC"), 0.45f, 0.72f); // 駅近の高層オフィス(ガラス)
        Mat("BuildingMid", Hex("D8C6A2"), 0f, 0.3f);      // 中層(暖色)
        Mat("BuildingLow", Hex("BFCE9E"), 0f, 0.25f);     // 郊外の低層住宅(緑がかり)
        Mat("Road", Hex("41444B"), 0f, 0.2f);
        Mat("TrainBase", Color.white, 0.15f, 0.5f);
        Mat("TrainDark", Hex("26282C"), 0.2f, 0.55f);
        Mat("TrainRoof", Hex("6B7076"), 0.3f, 0.4f);
        Mat("Marker", new Color(1f, 0.82f, 0.3f, 0.45f), 0f, 0.1f, true);
    }

    static Color Hex(string h)
    {
        Color c;
        ColorUtility.TryParseHtmlString("#" + h, out c);
        return c;
    }

    static void Mat(string name, Color c, float metallic, float smooth, bool transparent = false)
    {
        string path = "Assets/Resources/Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool fresh = m == null;
        if (fresh) m = new Material(Shader.Find("Standard"));
        m.color = c;
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Glossiness", smooth);
        if (transparent)
        {
            m.SetFloat("_Mode", 3);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = 3000;
        }
        if (fresh) AssetDatabase.CreateAsset(m, path);
        else EditorUtility.SetDirty(m);
    }

    // 実行時生成のUI/TextMeshが使うシェーダーはシーンから参照されないので、
    // Resources内のキャリア用マテリアルアセット経由でビルドに同梱する。
    // Always Included Shadersに built-in を入れる方式は内部リソース参照
    // (HideFlags.DontSave)のビルドエラーになるため使わない(リストは空へ戻す)
    public static void EnsureShaders()
    {
        var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0];
        var so = new SerializedObject(gs);
        so.FindProperty("m_AlwaysIncludedShaders").arraySize = 0;
        so.ApplyModifiedProperties();

        if (!AssetDatabase.IsValidFolder("Assets/Resources/Materials"))
            AssetDatabase.CreateFolder("Assets/Resources", "Materials");
        CarrierMat("UIDefault", "UI/Default");
        CarrierMat("TextShader", "GUI/Text Shader");
        CarrierMat("SpritesDefault", "Sprites/Default");
        AssetDatabase.SaveAssets();
    }

    static void CarrierMat(string name, string shaderName)
    {
        string path = "Assets/Resources/Materials/" + name + ".mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
        var sh = Shader.Find(shaderName);
        if (sh == null) { Debug.LogWarning("CarrierMat: shader not found " + shaderName); return; }
        AssetDatabase.CreateAsset(new Material(sh), path);
    }

    static void CreateScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var go = new GameObject("Bootstrap");
        go.AddComponent<Bootstrap>();
        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true) };
    }
}
