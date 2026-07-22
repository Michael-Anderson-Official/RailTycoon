using NUnit.Framework;

// GameRandom(決定的PRNG)のEditModeテスト。UnityEngine.Randomに依存しない純粋なロジック。
public class GameRandomTests
{
    [TearDown]
    public void TearDown()
    {
        // 他テストへ影響しないよう、次回利用者が明示的にSeed()する前提を崩さないためにも
        // 固定値へ戻しておく(既定値と同じ)
        GameRandom.Seed(0x9E3779B9u);
    }

    [Test]
    public void SameSeed_ProducesSameSequence()
    {
        GameRandom.Seed(12345u);
        var seq1 = new float[10];
        for (int i = 0; i < seq1.Length; i++) seq1[i] = GameRandom.NextFloat01();

        GameRandom.Seed(12345u);
        var seq2 = new float[10];
        for (int i = 0; i < seq2.Length; i++) seq2[i] = GameRandom.NextFloat01();

        Assert.That(seq2, Is.EqualTo(seq1), "同じseedなら同じ乱数列になること");
    }

    [Test]
    public void DifferentSeed_ProducesDifferentSequence()
    {
        GameRandom.Seed(1u);
        var seq1 = new float[5];
        for (int i = 0; i < seq1.Length; i++) seq1[i] = GameRandom.NextFloat01();

        GameRandom.Seed(2u);
        var seq2 = new float[5];
        for (int i = 0; i < seq2.Length; i++) seq2[i] = GameRandom.NextFloat01();

        Assert.That(seq2, Is.Not.EqualTo(seq1), "異なるseedなら(高確率で)異なる乱数列になること");
    }

    [Test]
    public void StateRoundTrip_ContinuesIdentically()
    {
        GameRandom.Seed(777u);
        for (int i = 0; i < 7; i++) GameRandom.NextFloat01(); // いくつか消費して状態を進めておく
        uint savedState = GameRandom.GetState();
        var continued = new float[5];
        for (int i = 0; i < continued.Length; i++) continued[i] = GameRandom.NextFloat01();

        // 別の系列で状態を荒らしたあと、保存した状態を復元
        GameRandom.Seed(999u);
        for (int i = 0; i < 20; i++) GameRandom.NextFloat01();
        GameRandom.SetState(savedState);
        var resumed = new float[5];
        for (int i = 0; i < resumed.Length; i++) resumed[i] = GameRandom.NextFloat01();

        Assert.That(resumed, Is.EqualTo(continued), "状態を保存・復元すると続きの乱数列が一致すること");
    }

    [Test]
    public void NextFloat01_StaysWithinHalfOpenUnitRange()
    {
        GameRandom.Seed(42u);
        for (int i = 0; i < 1000; i++)
        {
            float v = GameRandom.NextFloat01();
            Assert.That(v, Is.GreaterThanOrEqualTo(0f));
            Assert.That(v, Is.LessThan(1f), "戻り値は[0,1)の半開区間であること(1.0は含まない)");
        }
    }

    [Test]
    public void Seed_Zero_FallsBackToNonZeroState()
    {
        // 0は不動点(xorshiftが0のまま停滞する)なので、0を渡しても内部で既定値へ
        // フォールバックし、正常に乱数列が進むこと
        GameRandom.Seed(0u);
        Assert.That(GameRandom.GetState(), Is.Not.EqualTo(0u));
        float v1 = GameRandom.NextFloat01();
        float v2 = GameRandom.NextFloat01();
        Assert.That(v2, Is.Not.EqualTo(v1));
    }

    [Test]
    public void SetState_Zero_FallsBackToNonZeroState()
    {
        GameRandom.Seed(123u);
        GameRandom.SetState(0u);
        Assert.That(GameRandom.GetState(), Is.Not.EqualTo(0u));
    }
}
