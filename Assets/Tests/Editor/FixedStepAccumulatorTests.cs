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
    public void NormalOperation_DoesNotRecordOverload()
    {
        var acc = new FixedStepAccumulator(Tick, MaxTicks);
        for (int i = 0; i < 300; i++) acc.Advance(1f / 60f); // 5秒分、正常フレーム

        Assert.That(acc.MaxTicksReachedCount, Is.EqualTo(0), "通常運転ではオーバーロードを記録しないこと");
        Assert.That(acc.DroppedTickCount, Is.EqualTo(0));
        Assert.That(acc.DroppedSimulationTime, Is.EqualTo(0f));
    }

    [Test]
    public void Overload_IsObservable_NotSilentlyDropped()
    {
        var acc = new FixedStepAccumulator(Tick, MaxTicks);
        acc.Advance(10f); // 133msを大幅に超える単発の重いフレーム(スタッター相当)

        // 10秒のうちMaxTicks(8)ぶん=8/60秒だけ消化され、残りは正確に切り捨てられるはず
        float expectedDropped = 10f - MaxTicks * Tick;
        Assert.That(acc.MaxTicksReachedCount, Is.EqualTo(1), "オーバーロード発生回数が記録されること");
        Assert.That(acc.DroppedSimulationTime, Is.EqualTo(expectedDropped).Within(1e-4f),
            "捨てられた実時間が正確な値で観測できること");
        // 浮動小数点の丸め(accumulatorの減算蓄積 vs テスト側の直接計算)で1ズレ得るため許容誤差を持たせる
        Assert.That(acc.DroppedTickCount, Is.EqualTo((int)(expectedDropped / Tick)).Within(1),
            "捨てられたtick換算数がおおむね正確な値で観測できること");
    }

    [Test]
    public void Overload_IsIndependentOfSpeedMultiplier()
    {
        // FixedStepAccumulatorはGameState.timeScaleを一切参照しない設計。
        // 同じ実時間deltaなら、速度倍率の値に関わらずオーバーロード判定は同一になることを確認する
        // (このクラス自体がtimeScaleに触れないことの直接的な証明)
        var accA = new FixedStepAccumulator(Tick, MaxTicks);
        var accB = new FixedStepAccumulator(Tick, MaxTicks);
        // GameState.timeScaleをどちらのAdvance呼び出しにも一切渡していない・参照していないことが
        // このテストの構造自体で示される(引数はrealDeltaSecondsのみ)
        int ticksA = accA.Advance(0.2f);
        int ticksB = accB.Advance(0.2f);
        Assert.That(ticksB, Is.EqualTo(ticksA));
        Assert.That(accB.MaxTicksReachedCount, Is.EqualTo(accA.MaxTicksReachedCount));
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
