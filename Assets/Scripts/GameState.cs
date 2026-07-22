using UnityEngine;

// 資金・実績・ゲーム時刻。シーン横断のグローバル状態
public static class GameState
{
    public const double FareBase = 130;      // 初乗り(円)
    public const double FarePerKm = 12;      // 距離加算(円/km)
    public const double FareScale = 1000;    // ゲームバランス用の収入倍率
    public const double TrackCostPerM = 500000;   // 複線 50万円/m

    public static double money = 100e8;      // 100億円スタート
    public static long carried;              // 累計輸送人員
    public static float gameMinutes = 6 * 60; // 1日目 06:00 開始
    public static float timeScale = 5f;
    public static bool paused;               // 一時停止中はBootstrapのaccumulatorに実時間を積まない

    public static bool Spend(double yen)
    {
        if (money < yen) return false;
        money -= yen;
        return true;
    }

    // 撤去・縮小の払い戻し
    public static void Refund(double yen) => money += yen;

    public static void EarnFare(int pax, float km)
    {
        double yen = pax * (FareBase + FarePerKm * km) * FareScale;
        money += yen;
        carried += pax;
    }

    public static double StationCost(int cars, int faces, int lines)
        => (2.0 + 0.1 * cars * (faces + lines)) * 1e8;

    public static string MoneyLabel => (money / 1e8).ToString("F2") + "億円";

    public static string ClockLabel
    {
        get
        {
            int day = (int)(gameMinutes / 1440) + 1;
            int m = (int)gameMinutes % 1440;
            return day + "日目 " + (m / 60).ToString("D2") + ":" + (m % 60).ToString("D2");
        }
    }
}
