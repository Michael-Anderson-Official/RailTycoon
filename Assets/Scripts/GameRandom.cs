// シミュレーション結果に影響する乱数専用の決定的PRNG(xorshift32)。
// UnityEngine.Randomは描画フレーム数やエディタの内部呼び出しに左右され、
// 同じseed・同じ呼出順でも再現性が保証されないためゲーム状態には使わない。
// 見た目専用の演出(カメラ揺れ等)にUnityEngine.Randomを使うのは対象外。
public static class GameRandom
{
    // 0はxorshiftの不動点(常に0を返し続ける)なので避ける
    static uint state = 0x9E3779B9u;

    public static void Seed(uint seed) => state = seed == 0 ? 0x9E3779B9u : seed;

    // 現在の内部状態(将来のセーブ用に取得・復元可能)
    public static uint GetState() => state;
    public static void SetState(uint s) => state = s == 0 ? 0x9E3779B9u : s;

    static uint NextUInt()
    {
        uint x = state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        state = x;
        return x;
    }

    // [0,1) を返す。UnityEngine.Random.valueは[0,1](閉区間)だが、
    // 呼び出し側(Station.Tick)は重み付き抽選にしか使っておらず境界値の扱いは影響しない
    public static float NextFloat01() => (NextUInt() & 0x00FFFFFFu) / (float)0x01000000u;
}
