using System.Collections.Generic;
using UnityEngine;

// 面線配置の計算。スロット列 [線群0] 面0 [線群1] 面1 ... 面M-1 [線群M] で、
// 面と面の間の内側スロットへ、まず1線ずつラウンドロビンで配り、まだ余りがあれば
// 各内側スロット上限2まで2周目を配り、それでも余れば外側スロットへ交互に振る。
//   1面2線 → 線 面 線(島式)   2面2線 → 面 線線 面(相対式)
//   2面4線 → 線 面 線線 面 線   2面3線 → 線 面 線線 面
//   3面2線 → 線 面 線 面 線(外側|1番線|島式|2番線|外側。M2-Dで追加)
public static class StationLayout
{
    public const float TrackPitch = 4.6f;   // 線1本の占有幅
    public const float PlatformWidth = 8f;
    public const float CarLength = 20f;
    public const float ThroatLen = 52f;     // 駅端から収束点までの距離
    public const float LeadLen = 30f;       // 収束後、駅端手前で±2.3の平行になる直線区間(渡り線を置く)

    // M2-D: ホーム縁(列車に接する面)の利用モード。値は保存データにも使うため固定する
    public enum PlatformEdgeMode { Normal = 0, BoardOnly = 1, AlightOnly = 2, Disabled = 3 }

    public static bool AllowsBoard(PlatformEdgeMode m) => m == PlatformEdgeMode.Normal || m == PlatformEdgeMode.BoardOnly;
    public static bool AllowsAlight(PlatformEdgeMode m) => m == PlatformEdgeMode.Normal || m == PlatformEdgeMode.AlightOnly;

    // M2-D: 1つのホーム縁(物理線の片側)。1本の物理線に左右両側のホーム縁が
    // 関連付けられ得る(例: 3面2線の各線は両側にホーム縁を持つ)
    public struct PlatformEdge
    {
        public int platformIndex; // 接しているホーム本体のindex(platforms配列の添字)
        public int trackIndex;    // 物理線のindex(trackOffsets配列の添字)
        public int side;          // 駅ローカル座標系でのホームの方向(-1=線より-x側, +1=+x側)
        public PlatformEdgeMode mode; // 既定はNormal(乗降可)。Compute()の戻り値は常にNormalで
                                       // 初期化されるため、上書きは呼び出し側(Station)が行う
    }

    public struct Result
    {
        public float[] trackOffsets;     // 各線の中心横オフセット(駅中心基準)
        public List<Vector2> platforms;  // (中心オフセット, 幅)
        public float totalWidth;
        public List<int> stopTracks;     // ホームに接する線のindex(停車可能線、重複排除済み)
        public List<PlatformEdge> edges; // M2-D: ホーム縁一覧(1線に最大2件)
    }

    // 内側スロット(面と面の間)へ、まず1線ずつラウンドロビンで配り、まだ余りがあれば
    // 各内側スロット上限2まで2周目を配る。既存の1〜2面構成(内側スロットは高々1箇所)は
    // この方式でも旧方式(逐次に最大2ずつ)と完全に同じ結果になる
    // (内側スロットが1箇所ならラウンドロビンと逐次は数学的に同一)。
    // 3面以上で内側スロットが複数ある場合のみ、各スロットへ均等に配る点が新しい
    public static int[] Slots(int faces, int lines)
    {
        var slots = new int[faces + 1];
        int rest = lines;
        int innerCount = Mathf.Max(0, faces - 1);
        if (innerCount > 0)
        {
            for (int round = 0; round < 2 && rest > 0; round++)
                for (int i = 1; i <= innerCount && rest > 0; i++)
                {
                    if (slots[i] >= 2) continue;
                    slots[i]++;
                    rest--;
                }
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
        var edges = new List<PlatformEdge>();
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
            // スロット内の線のうち、隣接ホーム側の端の線だけが停車可能。
            // first/lastが同じ(スロット内が1線)場合、その1線は両側にホーム縁を持つ
            if (slots[s] > 0)
            {
                if (s > 0)
                {
                    stops.Add(first);
                    edges.Add(new PlatformEdge { platformIndex = s - 1, trackIndex = first, side = -1, mode = PlatformEdgeMode.Normal });
                }
                if (s < slots.Length - 1)
                {
                    stops.Add(last);
                    edges.Add(new PlatformEdge { platformIndex = s, trackIndex = last, side = 1, mode = PlatformEdgeMode.Normal });
                }
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
            edges = edges,
        };
        for (int i = 0; i < offsets.Count; i++) r.trackOffsets[i] = offsets[i] - x * 0.5f;
        foreach (var p in plats) r.platforms.Add(new Vector2(p.x - x * 0.5f, p.y));
        r.stopTracks.Sort();
        return r;
    }

    public static float Length(int cars) => cars * CarLength + 10f;
}
