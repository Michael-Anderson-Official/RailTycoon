// 固定tickの積算器。UnityEngine.Timeに依存しない純粋なロジックなので、
// 任意のdelta値を直接与えてEditModeテストで検証できる。
//
// 【重要】速度倍率(GameState.timeScale)はこのクラスの一切関与しない。
// tick頻度(1秒あたり何回tickを消化するか)は実時間(与えられたrealDeltaSeconds)のみで
// 決まり、速度倍率は「1tickが表すシミュレーション秒数」にのみ影響する
// (Bootstrap.SimTickのsimDt = tickSeconds * GameState.timeScale)。
// つまり×1/×5/×20いずれでも、実時間1秒あたりに必要なtick数は同じ(最大1/tickSeconds回)。
//
// 描画フレームレートが異なっても、合計経過時間が同じならAdvance()の戻り値(tick数)の
// 合計が一致する(単一フレームの遅延がmaxTicksPerCallを超えない限り、かつ速度倍率にも無関係)。
// それを超える遅延(実測フレームレートが概ね 1/(tickSeconds*maxTicksPerCall) 未満に
// 落ちる極端なスタッター)でのみ蓄積を打ち切ってデススパイラルを防ぐ。
// この場合に限り経過時間の一部が失われ、以後の結果は「遅れて」進行する
// (黙って捨てるのではなく、DroppedSimulationTime等で観測可能にしている)。
public class FixedStepAccumulator
{
    public readonly float tickSeconds;
    public readonly int maxTicksPerCall;
    float accumulator;

    // デバッグ用の可観測性。オーバーロード(上限tick到達)の発生を黙って隠さないための計測値
    public int MaxTicksReachedCount { get; private set; }   // 上限に到達した(=切り捨てが発生した)回数
    public int DroppedTickCount { get; private set; }        // 切り捨てられた"tick換算"の総数(累積)
    public float DroppedSimulationTime { get; private set; } // 切り捨てられた実時間の総量(秒、累積)

    public FixedStepAccumulator(float tickSeconds, int maxTicksPerCall)
    {
        this.tickSeconds = tickSeconds;
        this.maxTicksPerCall = maxTicksPerCall;
    }

    public float Accumulator => accumulator; // 現在のbacklog(未消化の蓄積実時間)

    // realDeltaSeconds分を積算し、今回消化すべきtick数を返す(最大maxTicksPerCall)。
    // 上限に達した場合、消化しきれない余剰時間は捨てて蓄積の際限ない肥大化を防ぐ。
    public int Advance(float realDeltaSeconds)
    {
        accumulator += realDeltaSeconds;
        int ticks = 0;
        while (accumulator >= tickSeconds && ticks < maxTicksPerCall)
        {
            accumulator -= tickSeconds;
            ticks++;
        }
        if (ticks >= maxTicksPerCall && accumulator > 0f)
        {
            MaxTicksReachedCount++;
            DroppedSimulationTime += accumulator;
            DroppedTickCount += Mathf_FloorDiv(accumulator, tickSeconds);
            accumulator = 0f; // 上限到達時は残りを切り捨てる(仕様どおり)
        }
        return ticks;
    }

    static int Mathf_FloorDiv(float a, float b) => (int)(a / b);

    public void Reset() => accumulator = 0f;

    public void ResetDiagnostics()
    {
        MaxTicksReachedCount = 0;
        DroppedTickCount = 0;
        DroppedSimulationTime = 0f;
    }
}
