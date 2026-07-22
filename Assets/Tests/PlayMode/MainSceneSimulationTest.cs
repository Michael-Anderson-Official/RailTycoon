using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

// Assets/Scenes/Main.unity を実際にロードして、一時停止と速度倍率がゲーム内時計の
// 進み方に正しく反映されることを確認するPlayModeテスト。
// BootstrapSmokeTest(直接Bootstrapを生成する簡易版)とは別に、実シーンファイル経由の
// 起動経路も検証する(シーン側の参照切れ等はBootstrapSmokeTestでは検出できないため)。
public class MainSceneSimulationTest
{
    const string Key = "railtycoon_save";
    bool hadRealSave;
    string realSaveBackup;

    [SetUp]
    public void SetUp()
    {
        hadRealSave = PlayerPrefs.HasKey(Key);
        if (hadRealSave) realSaveBackup = PlayerPrefs.GetString(Key);
        PlayerPrefs.DeleteKey(Key);
    }

    [TearDown]
    public void TearDown()
    {
        GameState.paused = false;
        PlayerPrefs.DeleteKey(Key);
        if (hadRealSave) PlayerPrefs.SetString(Key, realSaveBackup);
        PlayerPrefs.Save();
    }

    [UnityTest]
    public IEnumerator Paused_DoesNotAdvanceGameClock()
    {
        yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
        yield return null; // Bootstrap.Awakeを1フレーム走らせる

        GameState.paused = true;
        float before = GameState.gameMinutes;
        for (int i = 0; i < 30; i++) yield return null;
        float after = GameState.gameMinutes;

        Assert.That(after, Is.EqualTo(before), "一時停止中はゲーム内時計が進まないこと");
    }

    [UnityTest]
    public IEnumerator HigherSpeedMultiplier_AdvancesClockFaster()
    {
        yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
        yield return null;

        GameState.paused = false;
        GameState.timeScale = 1f;
        float before1 = GameState.gameMinutes;
        for (int i = 0; i < 30; i++) yield return null;
        float delta1 = GameState.gameMinutes - before1;

        GameState.timeScale = 20f;
        float before20 = GameState.gameMinutes;
        for (int i = 0; i < 30; i++) yield return null;
        float delta20 = GameState.gameMinutes - before20;

        // 実フレーム時間には多少のばらつきがあるため、20倍の意味が反映されていることを
        // 緩めの閾値(5倍以上)で確認する(厳密な比例確認はEditMode側のFixedStepAccumulator
        // /Bootstrap.SimTickの単体テストで行っている)
        Assert.That(delta20, Is.GreaterThan(delta1 * 5f),
            "速度倍率を上げるとゲーム内時計がより速く進むこと");
    }
}
