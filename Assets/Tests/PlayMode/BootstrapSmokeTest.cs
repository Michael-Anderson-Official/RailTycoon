using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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
        Assert.That(GameObject.Find("Canvas"), Is.Not.Null, "UIController.Buildが実行されていること");
        Assert.That(GameObject.Find("EventSystem"), Is.Not.Null);
        Assert.That(go.GetComponent<BuildController>(), Is.Not.Null, "BuildControllerがBootstrap自身に追加されていること");
    }
}
