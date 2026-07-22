using NUnit.Framework;

// FixedStepAccumulatorのEditModeテスト。UnityEngine.Timeに依存しない純粋なロジックなので
// 実時間待機は一切行わず、任意のdelta値を直接与えて検証する。
public class FixedStepAccumulatorTests
{
    const float Tick = Bootstrap.TickSeconds; // 1/60秒
    const int MaxTicks = Bootstrap.MaxTicksPerFrame; // 8

    [Test]
    public void SameTotalTime_60fpsEquivalent_MatchesExpectedTickCount()
    {
        var acc = new FixedStepAccumulator(Tick, MaxTicks);
        int totalTicks = 0;
        float frameDt = 1f / 60f;
        int frames = 120; // 2秒相当
        for (int i = 0; i < frames; i++) totalTicks += acc.Advance(frameDt);

        Assert.That(totalTicks, Is.EqualTo(frames), "60fps相当なら1フレーム=1tickになること");
    }

    [Test]
    public void SameTotalTime_30fpsEquivalent_MatchesExpectedTickCount()
    {
        var acc = new FixedStepAccumulator(Tick, MaxTicks);
        int totalTicks = 0;
        float frameDt = 1f / 30f;
        int frames = 60; // 30fpsで60フレーム=2秒相当
        for (int i = 0; i < frames; i++) totalTicks += acc.Advance(frameDt);

        // 60fps換算で2秒 = 120tick相当。30fpsでも同じ総経過時間なら同じtick数になること
        Assert.That(totalTicks, Is.EqualTo(120), "総経過時間が同じなら30fps相当でも60fps相当と同じtick数になること");
    }

    [Test]
    public void SameTotalTime_IrregularFrameSequence_MatchesExpectedTickCount()
    {
        var acc = new FixedStepAccumulator(Tick, MaxTicks);
        // 不均一だが、どのフレームもtick上限(8/60≈0.133秒)を超えない現実的な範囲の
        // フレーム時間列。1/60(3回)+1/30(1回)の反復で合計が正確に2秒(=120tick相当)になる
        // よう構成(1/60*3 + 1/30 = 5/60 = 1/12秒/周、24周で2秒)
        var deltas = new System.Collections.Generic.List<float>();
        for (int i = 0; i < 24; i++)
        {
            deltas.Add(1f / 60f);
            deltas.Add(1f / 60f);
            deltas.Add(1f / 60f);
            deltas.Add(1f / 30f); // 直前3フレームぶんカクついた分をまとめて描画した想定(上限内)
        }
        float sum = 0f;
        foreach (var d in deltas) sum += d;
        Assert.That(sum, Is.EqualTo(2.000f).Within(1e-3f), "テストデータの前提(合計2秒)");

        int totalTicks = 0;
        foreach (var d in deltas) totalTicks += acc.Advance(d);

        Assert.That(totalTicks, Is.EqualTo(120),
            "不均一なフレーム時間列でも、総経過時間が同じなら同じtick数になること(単一フレームが上限を超えない限り)");
    }

    [Test]
    public void MaxTicksPerCall_CapsRunawayCatchUp()
    {
        var acc = new FixedStepAccumulator(Tick, MaxTicks);
        // 10秒分のフレーム落ちを1回のAdvanceで与える
        int ticks = acc.Advance(10f);

        Assert.That(ticks, Is.EqualTo(MaxTicks), "1フレームの処理量は上限を超えないこと(デススパイラル防止)");
    }

    [Test]
    public void MaxTicksPerCall_DiscardsExcessTime_NoUnboundedBacklog()
    {
        var acc = new FixedStepAccumulator(Tick, MaxTicks);
        acc.Advance(10f); // 上限に達し、余剰時間は切り捨てられるはず

        // 切り捨て後、通常フレームに戻れば通常どおり1フレーム=1tickに戻ること
        // (蓄積が肥大化したまま残っていれば、しばらく2tick以上が続いてしまう)
        int nextTicks = acc.Advance(1f / 60f);
        Assert.That(nextTicks, Is.LessThanOrEqualTo(1), "上限到達後は蓄積が肥大化せず、直後のフレームは通常どおりに戻ること");
    }

    [Test]
    public void ZeroDelta_ProducesNoTicks()
    {
        var acc = new FixedStepAccumulator(Tick, MaxTicks);
        Assert.That(acc.Advance(0f), Is.EqualTo(0));
    }

    [Test]
    public void NotAdvancingWhilePaused_ThenResuming_DoesNotBurstCatchUp()
    {
        // Bootstrap.RunFrameは一時停止中Advance自体を呼ばない設計。
        // それを模して「一時停止中は何もしない」を確認し、解除後の最初のフレームが
        // 通常どおり(蓄積されていた「一時停止中の実時間」ぶんの暴走tickにならない)ことを検証する
        var acc = new FixedStepAccumulator(Tick, MaxTicks);
        acc.Advance(1f / 60f); // 通常運転で少し進めておく

        // ここで「一時停止」: 5秒間、Advanceを一切呼ばない(呼ばれない、が正しい実装)
        // (実装上は何もしないので明示的な操作は無いが、意図を示すためのコメント)

        int resumedTicks = acc.Advance(1f / 60f); // 解除後、通常の1フレームぶんだけ経過
        Assert.That(resumedTicks, Is.EqualTo(1), "一時停止中に実時間を積んでいなければ、解除後は通常どおり1tickで済むこと");
    }
}
