// 線路・駅・車両で共有する実寸基準(m)。
// 個別クラスへ同じ寸法を重複させると、車体だけ幅を変えた時にホームへ食い込む等の
// 不整合が再発するため、接触・段差に関係する値はここを唯一の基準にする。
public static class RailDimensions
{
    // 京王線の軌間は1,372mm。RailKitでは中心線から片側レールまでの距離を使う。
    public const float Gauge = 1.372f;
    public const float HalfGauge = Gauge * 0.5f;

    public const float RailTop = 0.55f;

    // 京王5000系相当の車体幅2,800mm。直線ホームでは車体との水平隙間を80mm確保する。
    public const float CarBodyWidth = 2.8f;
    public const float CarBodyHalfWidth = CarBodyWidth * 0.5f;
    public const float PlatformHorizontalGap = 0.08f;
    public const float TrackCenterToPlatformFace = CarBodyHalfWidth + PlatformHorizontalGap;

    // 車両床面はレール頭頂から1,130mm、ホームはそこから10mm低くする。
    // 混雑時の沈み込みを含めても大きな逆段差に見えない、ほぼ面一の停車姿勢になる。
    public const float VehicleFloorAboveRail = 1.13f;
    public const float PlatformAboveRail = 1.12f;
    public const float PlatformTop = RailTop + PlatformAboveRail;

    public const float WheelRadius = 0.43f;
    public const float WheelLocalCenterY = 0.42f;
    public const float BogieRootY = RailTop + WheelRadius - WheelLocalCenterY;
    public const float VehicleFloorLocalY = RailTop + VehicleFloorAboveRail - BogieRootY;

    // 駅構内はホーム面との間へ保守用の細い排水隙間を残すため、駅間より道床肩を絞る。
    public const float StationBedHalfWidth = 1.34f;

    // ---- 階層(高架) ----
    // 高さは連続値ではなく「階」で持つ。0=地上、1=2階、2=3階…。
    // 将来の地下は負の値で表せるよう、計算は符号を選ばない形にしておく
    public const float FloorHeight = 8f;              // 1階ぶん(道路上空を抜ける高さ)
    public const float ViaductDeckThickness = 1.2f;   // 桁の厚み
    public const float ViaductPierWidth = 1.6f;       // 橋脚の一辺
    public const float ViaductPierSpacing = 25f;      // 橋脚の間隔
    public static float HeightOfLevel(int level) => level * FloorHeight;

    // これ以上の高低差があれば、平面が重なっていても干渉しない(立体交差)とみなす
    public const float LevelClearance = 4f;

    // 勾配の上限(実際の鉄道に近い35‰)。駅間の縦断は両端の勾配を0にするS字なので、
    // 最大勾配は平均勾配の1.5倍になる。必要な水平距離は 1.5*Δh/MaxGrade
    public const float MaxGrade = 0.035f;

    // 複線の線路中心どうしの間隔の半分(=中心線から各線までの距離)。
    // 駅のスロート収束点・駅間の左右線・渡り線が全てこの値を共有する。
    // 個別に書くと渡り線だけ位置がずれる等の不整合が起きるため、ここを唯一の基準にする
    public const float MainTrackOffset = 2.3f;
}
