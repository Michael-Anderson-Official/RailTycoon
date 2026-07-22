// 固定tickの積算器。UnityEngine.Timeに依存しない純粋なロジックなので、
// 任意のdelta値を直接与えてEditModeテストで検証できる。
// 描画フレームレートが異なっても、合計経過時間が同じならAdvance()の
// 戻り値(tick数)の合計が一致する(単一フレームの遅延がmaxTicksPerCallを
// 超えない限り)。それを超える遅延は蓄積を打ち切ってデススパイラルを防ぐため、
// その場合のみ経過時間の一部が失われ、以後の結果は「遅れて」進行する。
public class FixedStepAccumulator
{
    public readonly float tickSeconds;
    public readonly int maxTicksPerCall;
    float accumulator;

    public FixedStepAccumulator(float tickSeconds, int maxTicksPerCall)
    {
        this.tickSeconds = tickSeconds;
        this.maxTicksPerCall = maxTicksPerCall;
    }

    public float Accumulator => accumulator;

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
        if (ticks >= maxTicksPerCall) accumulator = 0f; // 上限到達時は残りを切り捨てる(仕様どおり)
        return ticks;
    }

    public void Reset() => accumulator = 0f;
}
