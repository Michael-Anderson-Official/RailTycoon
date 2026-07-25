using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

// 通常の起動経路(Bootstrap.Awake)を実行し、数フレーム進めても例外/エラーログが
// 出ないことを確認するPlayModeスモークテスト。
// Bootstrap.Awakeは無条件でSaveLoad.Load()を呼ぶため、実プレイヤーの本セーブ
// (PlayerPrefsキー"railtycoon_save")を退避・復元し、テストが実データを壊さないようにする。
public class BootstrapSmokeTest
{
    const string Key = "railtycoon_save";
    bool hadRealSave;
    string realSaveBackup;

    static readonly string[] SpawnedRootNames =
        { "Bootstrap", "Main Camera", "EventSystem", "Canvas", "Environment", "City", "World" };

    [SetUp]
    public void SetUp()
    {
        hadRealSave = PlayerPrefs.HasKey(Key);
        if (hadRealSave) realSaveBackup = PlayerPrefs.GetString(Key);
        PlayerPrefs.DeleteKey(Key); // 起動が「新規プレイ」経路を通るようにする(決定的な初期状態)
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var name in SpawnedRootNames)
        {
            var go = GameObject.Find(name);
            if (go != null) Object.Destroy(go);
        }

        PlayerPrefs.DeleteKey(Key);
        if (hadRealSave) PlayerPrefs.SetString(Key, realSaveBackup);
        PlayerPrefs.Save();

        TrackNetwork.Clear();
        Services.Clear();
    }

    [UnityTest]
    public IEnumerator Bootstrap_InitializesAndRunsSeveralFramesWithoutErrors()
    {
        var go = new GameObject("Bootstrap");
        go.AddComponent<Bootstrap>();

        // Awakeが例外なく走ること
        Assert.That(GameObject.Find("Bootstrap"), Is.Not.Null);

        for (int i = 0; i < 10; i++)
            yield return null;

        // 起動経路が組み立てるはずの主要オブジェクトが存在すること
        Assert.That(GameObject.Find("Main Camera"), Is.Not.Null, "CameraRigが生成されていること");
        var canvas = GameObject.Find("Canvas");
        Assert.That(canvas, Is.Not.Null, "UIController.Buildが実行されていること");
        Assert.That(GameObject.Find("EventSystem"), Is.Not.Null);
        Assert.That(go.GetComponent<BuildController>(), Is.Not.Null, "BuildControllerがBootstrap自身に追加されていること");

        Assert.That(canvas.transform.Find("SafeArea/TopBar/Settings"), Is.Not.Null,
            "破壊操作は常設ボタンではなく設定画面へまとめること");
        Assert.That(canvas.transform.Find("SafeArea/Toolbar/ModeStation"), Is.Not.Null);
        Assert.That(canvas.transform.Find("SafeArea/CameraTools/Home"), Is.Not.Null,
            "タッチ端末にも全体表示カメラ操作があること");
        Assert.That(canvas.transform.Find("ConfirmModal"), Is.Not.Null,
            "撤去・廃止・初期化に共通確認画面があること");
        Assert.That(canvas.GetComponentsInChildren<ScrollRect>(true).Length,
            Is.GreaterThanOrEqualTo(2), "長い系統一覧と番線設定がスクロール可能なこと");
        Assert.That(UIController.MinimumPrimaryButtonHeight, Is.GreaterThanOrEqualTo(54f));

        var toast = canvas.transform.Find("SafeArea/Toast");
        Assert.That(toast, Is.Not.Null);
        Assert.That(toast.GetComponent<CanvasGroup>().blocksRaycasts, Is.False,
            "通知表示中も起動案内や地図の操作を遮らないこと");
        foreach (var graphic in toast.GetComponentsInChildren<Graphic>(true))
            Assert.That(graphic.raycastTarget, Is.False);

        var safeArea = canvas.transform.Find("SafeArea").GetComponent<RectTransform>();
        var edgeBox = safeArea.Find("EdgeModal/Box").GetComponent<RectTransform>();
        Assert.That(edgeBox.rect.height, Is.LessThanOrEqualTo(safeArea.rect.height - 30f),
            "番線設定の外枠を横向き端末でもセーフエリア内へ収めること");
        Assert.That(edgeBox.rect.width, Is.LessThanOrEqualTo(safeArea.rect.width - 30f));
    }
}
