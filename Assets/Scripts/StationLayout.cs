using System.Collections.Generic;
using UnityEngine;

// 面線配置の計算。スロット列 [線群0] 面0 [線群1] 面1 ... 面M-1 [線群M] で、
// 面と面の間の内側スロットに2線ずつ先に入れ、残りを外側スロットへ交互に振る。
//   1面2線 → 線 面 線(島式)   2面2線 → 面 線線 面(相対式)
//   2面4線 → 線 面 線線 面 線   2面3線 → 線 面 線線 面
public static class StationLayout
{
    public const float TrackPitch = 4.6f;   // 線1本の占有幅
    public const float PlatformWidth = 8f;
    public const float CarLength = 20f;
    public const float ThroatLen = 45f;     // 駅端から収束点までの距離

    public struct Result
    {
        public float[] trackOffsets;     // 各線の中心横オフセット(駅中心基準)
        public List<Vector2> platforms;  // (中心オフセット, 幅)
        public float totalWidth;
        public List<int> stopTracks;     // ホームに接する線のindex(停車可能線)
    }

    public static int[] Slots(int faces, int lines)
    {
        var slots = new int[faces + 1];
        int rest = lines;
        for (int i = 1; i < faces && rest > 0; i++)
        {
            int t = Mathf.Min(2, rest);
            slots[i] = t;
            rest -= t;
        }
        int side = 0;
        while (rest > 0)
        {
            slots[side == 0 ? 0 : faces]++;
            rest--;
            side ^= 1;
        }
        return slots;
    }

    public static Result Compute(int faces, int lines)
    {
        var slots = Slots(faces, lines);
        var offsets = new List<float>();
        var stops = new HashSet<int>();
        var plats = new List<Vector2>();
        float x = 0;
        for (int s = 0; s < slots.Length; s++)
        {
            int first = offsets.Count;
            for (int j = 0; j < slots[s]; j++)
            {
                offsets.Add(x + TrackPitch * 0.5f);
                x += TrackPitch;
            }
            int last = offsets.Count - 1;
            // スロット内の線のうち、隣接ホーム側の端の線だけが停車可能
            if (slots[s] > 0)
            {
                if (s > 0) stops.Add(first);              // 左隣に面がある
                if (s < slots.Length - 1) stops.Add(last); // 右隣に面がある
            }
            if (s < faces)
            {
                plats.Add(new Vector2(x + PlatformWidth * 0.5f, PlatformWidth));
                x += PlatformWidth;
            }
        }
        var r = new Result
        {
            trackOffsets = new float[offsets.Count],
            platforms = new List<Vector2>(),
            totalWidth = x,
            stopTracks = new List<int>(stops),
        };
        for (int i = 0; i < offsets.Count; i++) r.trackOffsets[i] = offsets[i] - x * 0.5f;
        foreach (var p in plats) r.platforms.Add(new Vector2(p.x - x * 0.5f, p.y));
        r.stopTracks.Sort();
        return r;
    }

    public static float Length(int cars) => cars * CarLength + 10f;
}
