using NUnit.Framework;
using UnityEngine;

// 縦画面のシート(駅/線路/系統/駅情報)が実機の画面に収まることを守る。
// パネルの高さはCanvasScalerの参照単位(1000×1600基準)で、端末pxではない。
// 高さの数値だけを見て「844pxの画面に823pxのパネルは入らない」と判断すると
// 誤りになる(実装後レビューでCodex CLIが一度そう指摘した)。
// スケール後の実寸で見るのが正しく、その計算をここで固定する。
public class UiLayoutTests
{
    // CanvasScaler.ScaleMode.ScaleWithScreenSize / matchWidthOrHeight=0.5 の定義どおり
    static float ScaleFactor(float w, float h)
        => Mathf.Pow(w / UIController.ReferenceResolution.x, 0.5f)
         * Mathf.Pow(h / UIController.ReferenceResolution.y, 0.5f);

    // 想定する縦画面。実機はiPhone 17 Pro(402×874pt)だが、下限側も併せて見る
    static readonly (float w, float h, string name)[] Portraits =
    {
        (402f, 874f, "iPhone 17 Pro(実機)"),
        (390f, 844f, "iPhone 12〜16"),
        (360f, 640f, "小型Android"),
        (320f, 568f, "iPhone SE(初代)"),
    };

    // ノッチ・ホームインジケータぶんの余裕。safeRootが実際に縮む量より厚めに取る
    const float SafeAreaAllowancePx = 100f;

    [TestCase(UIController.StationPanelPortraitHeight, "駅パネル")]
    [TestCase(UIController.TrainPanelPortraitHeight, "系統パネル")]
    [TestCase(UIController.InfoPanelPortraitHeight, "駅情報パネル")]
    [TestCase(UIController.TrackPanelPortraitHeight, "線路パネル")]
    public void PortraitSheets_FitInsideTheSafeArea(float sheetHeight, string what)
    {
        // SetSheetは下端をツールバーの上(bottom + 10)へ置く
        float topEdge = UIController.PortraitToolbarHeight + 10f + sheetHeight;
        foreach (var d in Portraits)
        {
            float scale = ScaleFactor(d.w, d.h);
            float availableUnits = (d.h - SafeAreaAllowancePx) / scale;
            Assert.That(topEdge, Is.LessThan(availableUnits),
                what + "が" + d.name + "の安全域に収まること(上端" + topEdge.ToString("F0") +
                "単位 / 使える高さ" + availableUnits.ToString("F0") + "単位)");
        }
    }

    [Test]
    public void PortraitSheets_LeaveRoomForTheTopBar()
    {
        // 上部バーへ重なると資金・時刻が読めなくなる。最も背の高いシートで見る
        float tallest = Mathf.Max(
            UIController.StationPanelPortraitHeight, UIController.TrainPanelPortraitHeight);
        float topEdge = UIController.PortraitToolbarHeight + 10f + tallest;
        foreach (var d in Portraits)
        {
            float scale = ScaleFactor(d.w, d.h);
            float availableUnits = (d.h - SafeAreaAllowancePx) / scale;
            Assert.That(topEdge + UIController.PortraitTopHeight,
                Is.LessThan(availableUnits + UIController.PortraitTopHeight),
                d.name + ": シートが画面外へ出ないこと");
            Assert.That(availableUnits - topEdge, Is.GreaterThan(0f),
                d.name + ": 余白が残ること(" + (availableUnits - topEdge).ToString("F0") + "単位)");
        }
    }
}
