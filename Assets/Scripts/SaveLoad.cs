using System;
using System.Collections.Generic;
using UnityEngine;

// セーブ/ロード。PlayerPrefs(WebGLではIndexedDB)にJSONで保存する。
// 保存タイミング: 建設・敷設・発車の直後+15秒ごとの自動保存。
//
// M2-C: v2スキーマを導入。v1(index参照・走行中列車/乗車旅客/GameRandom状態を
// 保存しない簡易形式)はもう書き込まないが、既存のv1セーブは読み取れる
// (MigrateV1ToV2で正規化して読み込む。この正規化は不可逆で、v1保存直前の
// continued worldとは一般に一致しない。決定的一致の保証はv2→v2のラウンドトリップに
// 限定する。CodexReviews/2026-07-23_m2c_design_review.md参照)。
public static class SaveLoad
{
    const string Key = "railtycoon_save";
    public static bool suppress; // リセット直後の再保存防止

    // ==================== v1 (読み取り専用。もう書き込まない) ====================

    [Serializable]
    public class StData
    {
        public float x, z, yaw;
        public int cars, faces, lines;
        public string name;
        public float dev;
        public int[] waitTo;
        public int[] waitN;
    }

    [Serializable]
    public class SegData { public int a, b, sa, sb; }

    [Serializable]
    public class TrData
    {
        public string typeId;
        public int cars;
        public int[] route;
        public int[] tracks;
        public int idx, dir;
        public int lineId = -1;   // 旧セーブ互換(単一系統)。読み込み時にlineIdsへ移行
        public int[] lineIds;
    }

    [Serializable]
    public class LnData
    {
        public int id, typeIdx;
        public string name;
        public int[] route;
        public int[] tracks;
    }

    [Serializable]
    public class GameData
    {
        public int v = 1;
        public double money;
        public long carried;
        public float minutes, speed;
        public int nameCounter;
        public int lineIdCounter;
        public List<StData> st = new List<StData>();
        public List<SegData> seg = new List<SegData>();
        public List<TrData> tr = new List<TrData>();
        public List<LnData> ln = new List<LnData>();
    }

    // ==================== v2 ====================

    // versionだけを軽量に読み取る。vへ初期値を与えない(JSONに欠落していれば0のまま
    // 残ることを利用して「version欠落」を検出する。JsonUtilityはJSONに無いフィールドを
    // 上書きしないため、初期値を付けると欠落をv1と誤認する危険がある)
    [Serializable]
    class VersionProbe { public int v; }

    [Serializable]
    public class StDataV2
    {
        public int id;              // 0=不正値(未割当)
        public float x, z, yaw;
        public int cars, faces, lines;
        public string name;
        public float dev;
        public double spawnAcc;
        public int[] waitToId;      // 目的地駅のstable id
        public int[] waitN;
    }

    [Serializable]
    public class SegDataV2
    {
        public int id;               // 0=不正値
        public int aId, bId, sa, sb;
        // v4への後方互換な追加フィールド。旧JSONでは欠落=0=Ballastとなるため、
        // schema versionを上げずに従来線路をそのまま復元できる。
        public int bedType;
    }

    [Serializable]
    public class LnDataV2
    {
        public int id, typeIdx;      // idは既存のServiceLine.id方式をそのまま流用
        public string name;
        public int[] routeIds;       // stable id列
        public int[] tracks;
    }

    [Serializable]
    public class OnboardGroupV2
    {
        public int destId;
        public int count;
        public float bx, by, bz;     // boardPos
    }

    [Serializable]
    public class TrDataV2
    {
        public int id;                  // 0=不正値
        public string typeId;
        public int cars;
        public int[] routeIds;          // stable id列
        public int[] tracks;
        public int[] lineIds;
        public int idx = -1;            // -1=欠落/不正
        public int dir;                 // +1/-1のみ有効
        public int curTrack = -1;       // -1=欠落/不正
        public bool cyclic;
        public int state = -1;          // -1=欠落/不正, 0=Dwell, 1=Run
        public int dwellPathKind = -1;  // Run時は無視。Dwell時のみ0=StationLocal,1=JustArrivedLeg
        public float dwellT, s, v;
        public int departStationId;     // 0=無し(Dwell/StationLocal)
        public int departTrack;
        public float releaseS;
        public bool released;
        public int legSegmentId;        // 0=無し(Dwell/StationLocal)
        public OnboardGroupV2[] onboard;
        public int departureCount, arrivalCount;
    }

    [Serializable]
    public class GameDataV2
    {
        public int v = 2;
        public double money;
        public long carried;
        public float minutes, speed;
        public uint randomState;
        public int nameCounter;
        public int stationIdCounter, segmentIdCounter, trainIdCounter, lineIdCounter;
        public StDataV2[] st;
        public SegDataV2[] seg;
        public TrDataV2[] tr;
        public LnDataV2[] ln;
    }

    // ==================== v3 (M2-D: ホーム縁の乗降モード) ====================
    // 駅構内の物理線路・ホーム本体数自体(cars/faces/lines)はv2のままStDataV3が
    // 継承し、新たにホーム縁(1本の物理線の片側、最大2件/線)の乗降モードだけを
    // 追加する。station以外(segment/line/train)はv2のスキーマから変更が無いため、
    // SegDataV2/LnDataV2/TrDataV2/OnboardGroupV2をそのまま再利用する

    [Serializable]
    public class PlatformEdgeOverrideData
    {
        public int trackIndex;
        public int side;   // -1または+1のみ有効
        public int mode;   // StationLayout.PlatformEdgeModeの値(0-3)
    }

    [Serializable]
    public class StDataV3
    {
        public int id;              // 0=不正値(未割当)
        public float x, z, yaw;
        public int cars, faces, lines;
        public string name;
        public float dev;
        public double spawnAcc;
        public int[] waitToId;      // 目的地駅のstable id
        public int[] waitN;
        // Normal(既定)以外のホーム縁のみを列挙する(null/空なら全てNormal)。
        // v1/v2からのmigrationでは常にnull(=全てNormal、既存挙動を維持)にする
        public PlatformEdgeOverrideData[] edgeOverrides;
    }

    [Serializable]
    public class GameDataV3
    {
        public int v = 3;
        public double money;
        public long carried;
        public float minutes, speed;
        public uint randomState;
        public int nameCounter;
        public int stationIdCounter, segmentIdCounter, trainIdCounter, lineIdCounter;
        public StDataV3[] st;
        public SegDataV2[] seg;
        public TrDataV2[] tr;
        public LnDataV2[] ln;
    }

    // ==================== v4 (通過駅(スキップストップ)を挟む多区間走行) ====================
    // station/segment/lineのスキーマ自体はv3から変更が無いため、StDataV3/SegDataV2/
    // LnDataV2をそのまま再利用する。TrDataV2に、通過駅を挟む区間(直結2駅の単一区間
    // ではないRun状態)の復元に必要なフィールドだけを追加したTrDataV3を新設する

    [Serializable]
    public class TrDataV3
    {
        public int id;                  // 0=不正値
        public string typeId;
        public int cars;
        public int[] routeIds;          // stable id列
        public int[] tracks;
        public int[] lineIds;
        public int idx = -1;            // -1=欠落/不正
        public int dir;                 // +1/-1のみ有効
        public int curTrack = -1;       // -1=欠落/不正
        public bool cyclic;
        public int state = -1;          // -1=欠落/不正, 0=Dwell, 1=Run
        public int dwellPathKind = -1;  // Run時は無視。Dwell時のみ0=StationLocal,1=JustArrivedLeg
        public float dwellT, s, v;
        public int departStationId;     // 0=無し(Dwell/StationLocal)
        public int departTrack;
        public float releaseS;
        public bool released;
        public int legSegmentId;        // 0=無し(Dwell/StationLocal)。Run+多区間ならtransitSegmentIdsの末尾と同じ
        public OnboardGroupV2[] onboard;
        public int departureCount, arrivalCount;
        // 通過駅(スキップストップ)を挟む区間の情報。null/長さ1以下 = 従来通り直結2駅の
        // 単一区間(通過駅なし)。非空の場合、末尾は必ずlegSegmentIdと一致する。
        // 弧長(早期解放チェックポイントの位置)は保存せず、ロード時に駅ジオメトリから
        // 都度再計算する(Train.RestoreState参照)
        public int[] transitSegmentIds;   // departStationからroute[idx]までの全区間(末尾=legSegmentId)
        public int[] transitStationIds;   // 通過駅のみ。transitSegmentIds.Length-1個
        public int[] transitTracks;       // 対応する番線。transitStationIdsと同数
        public int passedCheckpoints;     // 既に解放済みのチェックポイント数(0=未消化)
    }

    [Serializable]
    public class GameDataV4
    {
        public int v = 4;
        public double money;
        public long carried;
        public float minutes, speed;
        public uint randomState;
        public int nameCounter;
        public int stationIdCounter, segmentIdCounter, trainIdCounter, lineIdCounter;
        public StDataV3[] st;
        public SegDataV2[] seg;
        public TrDataV3[] tr;
        public LnDataV2[] ln;
    }

    // v5で駅に階(level)が加わった。増えたのは駅だけなので、seg/tr/lnはv4の型を流用する
    [Serializable]
    public class StDataV4
    {
        public int id;
        public float x, z, yaw;
        public int cars, faces, lines;
        public string name;
        public float dev;
        public double spawnAcc;
        public int[] waitToId;
        public int[] waitN;
        public PlatformEdgeOverrideData[] edgeOverrides;
        public int level;           // 0=地上。v4以前からのmigrationでは常に0
    }

    [Serializable]
    public class GameDataV5
    {
        public int v = 5;
        public double money;
        public long carried;
        public float minutes, speed;
        public uint randomState;
        public int nameCounter;
        public int stationIdCounter, segmentIdCounter, trainIdCounter, lineIdCounter;
        public StDataV4[] st;
        public SegDataV2[] seg;
        public TrDataV3[] tr;
        public LnDataV2[] ln;
    }

    public static void Save()
    {
        if (suppress) return;
        var d = new GameDataV5
        {
            money = GameState.money,
            carried = GameState.carried,
            minutes = GameState.gameMinutes,
            speed = GameState.timeScale,
            randomState = GameRandom.GetState(),
            nameCounter = TrackNetwork.nameCounter,
            stationIdCounter = TrackNetwork.stationIdCounter,
            segmentIdCounter = TrackNetwork.segmentIdCounter,
            trainIdCounter = TrackNetwork.trainIdCounter,
            lineIdCounter = Services.idCounter,
        };

        var stList = new List<StDataV4>();
        foreach (var s in TrackNetwork.stations)
        {
            if (s.id == 0) continue; // preview等、未割当IDの駅は保存しない
            var sd = new StDataV4
            {
                id = s.id,
                x = s.transform.position.x,
                z = s.transform.position.z,
                yaw = s.transform.eulerAngles.y,
                level = s.level,
                cars = s.cars, faces = s.faces, lines = s.lines,
                name = s.stationName,
                dev = s.dev,
                spawnAcc = s.SpawnAccumulator,
            };
            var to = new List<int>();
            var n = new List<int>();
            foreach (var kv in s.waiting)
                if (kv.Key != null && kv.Key.id != 0) { to.Add(kv.Key.id); n.Add(kv.Value); }
            sd.waitToId = to.ToArray();
            sd.waitN = n.ToArray();
            // Normal(既定)以外のホーム縁のみ保存する(省略分はロード時に既定Normalとなる)
            var overrides = new List<PlatformEdgeOverrideData>();
            foreach (var e in s.PlatformEdges)
                if (e.mode != StationLayout.PlatformEdgeMode.Normal)
                    overrides.Add(new PlatformEdgeOverrideData { trackIndex = e.trackIndex, side = e.side, mode = (int)e.mode });
            sd.edgeOverrides = overrides.ToArray();
            stList.Add(sd);
        }
        d.st = stList.ToArray();

        var segList = new List<SegDataV2>();
        foreach (var g in TrackNetwork.segments)
        {
            if (g.id == 0 || g.a.id == 0 || g.b.id == 0) continue;
            segList.Add(new SegDataV2
            {
                id = g.id,
                aId = g.a.id,
                bId = g.b.id,
                sa = g.signA,
                sb = g.signB,
                bedType = (int)g.bedType,
            });
        }
        d.seg = segList.ToArray();

        var lnList = new List<LnDataV2>();
        foreach (var l in Services.lines)
        {
            var r = new List<int>();
            bool okL = true;
            foreach (var s in l.route)
            {
                if (s == null || s.id == 0) { okL = false; break; }
                r.Add(s.id);
            }
            if (!okL || r.Count < 2) continue;
            lnList.Add(new LnDataV2
            {
                id = l.id, typeIdx = l.typeIdx, name = l.name,
                routeIds = r.ToArray(),
                tracks = l.tracks != null ? l.tracks.ToArray() : null,
            });
        }
        d.ln = lnList.ToArray();

        var trList = new List<TrDataV3>();
        foreach (var t in TrackNetwork.trains)
        {
            if (t.id == 0 || t.fm == null || t.route == null) continue;
            var r = new List<int>();
            bool ok = true;
            foreach (var s in t.route)
            {
                if (s == null || s.id == 0) { ok = false; break; }
                r.Add(s.id);
            }
            if (!ok || r.Count < 2) continue;

            var td = new TrDataV3
            {
                id = t.id,
                typeId = t.fm.type.id,
                cars = t.fm.cars,
                routeIds = r.ToArray(),
                tracks = t.routeTracks != null ? t.routeTracks.ToArray() : null,
                lineIds = t.lineIds != null ? t.lineIds.ToArray() : null,
                idx = t.idx,
                dir = t.dir,
                curTrack = t.curTrack,
                cyclic = t.cyclic,
                dwellT = t.DwellRemaining,
                s = t.RouteS,
                v = t.V, // bit-exactな内部値をそのまま保存(SpeedKmh経由だと丸め誤差が伝播するため)
                releaseS = t.ReleaseS,
                released = t.DepartureTrackReleased,
                departStationId = t.DepartStation != null ? t.DepartStation.id : 0,
                departTrack = t.DepartTrack,
                departureCount = t.DepartureCount,
                arrivalCount = t.ArrivalCount,
            };

            if (!t.IsDwelling)
            {
                td.state = 1;
                td.dwellPathKind = -1;
                td.legSegmentId = t.CurSeg != null ? t.CurSeg.id : 0;
                // 通過駅(スキップストップ)を挟む区間なら、経路全体のセグメント列・通過駅・
                // 番線・解放済みチェックポイント数を保存する(直結2駅の単一区間なら
                // segmentIdsはlegSegmentId 1件のみ・station/tracksは空になる)
                t.GetTransitChainForSave(out var segIds, out var stIds, out var trkIds, out var passed);
                td.transitSegmentIds = segIds.ToArray();
                td.transitStationIds = stIds.ToArray();
                td.transitTracks = trkIds.ToArray();
                td.passedCheckpoints = passed;
            }
            else
            {
                td.state = 0;
                if (t.CurrentDwellPathKind == Train.DwellPathKind.JustArrivedLeg && t.DepartStation != null)
                {
                    td.dwellPathKind = 1;
                    var histSeg = TrackNetwork.Find(t.DepartStation, t.route[t.idx]);
                    td.legSegmentId = histSeg != null ? histSeg.id : 0;
                }
                else
                {
                    td.dwellPathKind = 0;
                    td.legSegmentId = 0;
                    td.departStationId = 0; // StationLocalではdepartStationは無意味なので保存しない
                }
            }

            var onboardList = new List<OnboardGroupV2>();
            foreach (var grp in t.Onboard)
            {
                if (grp.dest == null || grp.dest.id == 0) continue;
                onboardList.Add(new OnboardGroupV2
                {
                    destId = grp.dest.id, count = grp.count,
                    bx = grp.boardPos.x, by = grp.boardPos.y, bz = grp.boardPos.z,
                });
            }
            td.onboard = onboardList.ToArray();
            trList.Add(td);
        }
        d.tr = trList.ToArray();

        PlayerPrefs.SetString(Key, JsonUtility.ToJson(d));
        PlayerPrefs.Save();
    }

    // 保存があれば復元してtrue。
    // 12段階(parse→version判定→migration→validation→適用)に分離しており、
    // validation成功が確定するまで現在のワールド(TrackNetwork/GameState/Services)を
    // 一切変更しない。不正なJSON・未来version・参照整合性エラー等はロード失敗として
    // 扱い、現在のワールドは無傷のまま残る
    public static bool Load()
    {
        // migration/validationはあらゆる壊れた入力(配列内のnull要素等)を全て
        // 想定しきれるとは限らないため、Load全体を最終防衛線としてtry/catchする。
        // ApplyToWorld自身は既に内部でstaging+try/catchを持つが、その手前の
        // MigrateV1ToV2/Validateで予期しない例外が出た場合もLoad()の外へ漏らさず
        // falseを返す(呼び出し元のBootstrap.Awakeを壊さないため。
        // 実装後レビューでCodex CLIが指摘)
        try { return LoadInner(); }
        catch (Exception e)
        {
            Debug.LogError("SaveLoad: 予期しない例外でロードを中断: " + e.Message);
            return false;
        }
    }

    static bool LoadInner()
    {
        if (!PlayerPrefs.HasKey(Key)) return false;
        string json = PlayerPrefs.GetString(Key);

        VersionProbe probe;
        try { probe = JsonUtility.FromJson<VersionProbe>(json); }
        catch (Exception e) { Debug.LogError("SaveLoad: version probe failed: " + e.Message); return false; }
        if (probe == null) return false;

        GameDataV5 v5;
        bool migrated;
        if (probe.v == 1)
        {
            GameData v1;
            try { v1 = JsonUtility.FromJson<GameData>(json); }
            catch (Exception e) { Debug.LogError("SaveLoad: v1 parse failed: " + e.Message); return false; }
            if (v1 == null || v1.st == null || v1.st.Count == 0) return false;
            v5 = MigrateV4ToV5(MigrateV3ToV4(MigrateV2ToV3(MigrateV1ToV2(v1))));
            migrated = true;
        }
        else if (probe.v == 2)
        {
            GameDataV2 v2;
            try { v2 = JsonUtility.FromJson<GameDataV2>(json); }
            catch (Exception e) { Debug.LogError("SaveLoad: v2 parse failed: " + e.Message); return false; }
            if (v2 == null || v2.st == null || v2.st.Length == 0) return false;
            v5 = MigrateV4ToV5(MigrateV3ToV4(MigrateV2ToV3(v2)));
            migrated = true;
        }
        else if (probe.v == 3)
        {
            GameDataV3 v3;
            try { v3 = JsonUtility.FromJson<GameDataV3>(json); }
            catch (Exception e) { Debug.LogError("SaveLoad: v3 parse failed: " + e.Message); return false; }
            if (v3 == null || v3.st == null || v3.st.Length == 0) return false;
            v5 = MigrateV4ToV5(MigrateV3ToV4(v3));
            migrated = true;
        }
        else if (probe.v == 4)
        {
            GameDataV4 v4;
            try { v4 = JsonUtility.FromJson<GameDataV4>(json); }
            catch (Exception e) { Debug.LogError("SaveLoad: v4 parse failed: " + e.Message); return false; }
            if (v4 == null || v4.st == null || v4.st.Length == 0) return false;
            v5 = MigrateV4ToV5(v4);
            migrated = true;
        }
        else if (probe.v == 5)
        {
            try { v5 = JsonUtility.FromJson<GameDataV5>(json); }
            catch (Exception e) { Debug.LogError("SaveLoad: v5 parse failed: " + e.Message); return false; }
            migrated = false;
        }
        else
        {
            Debug.LogError("SaveLoad: unsupported version " + probe.v + " (0=欠落, 未来versionは非対応)");
            return false;
        }
        if (v5 == null || v5.st == null || v5.st.Length == 0) return false;

        string error;
        if (!Validate(v5, out error))
        {
            Debug.LogError("SaveLoad: validation failed: " + error);
            return false;
        }

        if (!ApplyToWorld(v5, out error))
        {
            Debug.LogError("SaveLoad: apply failed: " + error);
            return false;
        }
        if (migrated) Debug.Log("SaveLoad: 旧セーブをv5へ正規化して読み込みました(次回保存時にv5として書き戻ります)");
        return true;
    }

    // v4(駅の階が存在しない)をv5形式へ正規化する。全ての駅を地上(level=0)とする。
    // v4の時点で駅は地上にしか建てられなかったので、既存挙動と完全に一致する
    static GameDataV5 MigrateV4ToV5(GameDataV4 v4)
    {
        var d = new GameDataV5
        {
            money = v4.money, carried = v4.carried, minutes = v4.minutes, speed = v4.speed,
            randomState = v4.randomState, nameCounter = v4.nameCounter,
            stationIdCounter = v4.stationIdCounter, segmentIdCounter = v4.segmentIdCounter,
            trainIdCounter = v4.trainIdCounter, lineIdCounter = v4.lineIdCounter,
            seg = v4.seg, tr = v4.tr, ln = v4.ln,
        };
        var srcSt = v4.st ?? new StDataV3[0];
        var stList = new StDataV4[srcSt.Length];
        for (int i = 0; i < srcSt.Length; i++)
        {
            var s = srcSt[i];
            stList[i] = new StDataV4
            {
                id = s.id, x = s.x, z = s.z, yaw = s.yaw,
                cars = s.cars, faces = s.faces, lines = s.lines,
                name = s.name, dev = s.dev, spawnAcc = s.spawnAcc,
                waitToId = s.waitToId, waitN = s.waitN, edgeOverrides = s.edgeOverrides,
                level = 0,
            };
        }
        d.st = stList;
        return d;
    }

    // v3(通過駅を挟む多区間の概念が存在しない)をv4形式へ正規化する。全てのRun列車は
    // 直結2駅の単一区間として扱う(transitSegmentIds等は空のまま)。既存挙動と完全に
    // 一致する(v3セーブの時点でそもそも通過駅を挟む区間は存在し得なかったため)
    static GameDataV4 MigrateV3ToV4(GameDataV3 v3)
    {
        var d = new GameDataV4
        {
            money = v3.money, carried = v3.carried, minutes = v3.minutes, speed = v3.speed,
            randomState = v3.randomState, nameCounter = v3.nameCounter,
            stationIdCounter = v3.stationIdCounter, segmentIdCounter = v3.segmentIdCounter,
            trainIdCounter = v3.trainIdCounter, lineIdCounter = v3.lineIdCounter,
            st = v3.st, seg = v3.seg, ln = v3.ln,
        };
        var srcTr = v3.tr ?? new TrDataV2[0]; // 欠落(null)はtrなしとして扱う(Validate側と同じ規約)
        var trList = new TrDataV3[srcTr.Length];
        for (int i = 0; i < srcTr.Length; i++)
        {
            var t = srcTr[i];
            trList[i] = new TrDataV3
            {
                id = t.id, typeId = t.typeId, cars = t.cars,
                routeIds = t.routeIds, tracks = t.tracks, lineIds = t.lineIds,
                idx = t.idx, dir = t.dir, curTrack = t.curTrack, cyclic = t.cyclic,
                state = t.state, dwellPathKind = t.dwellPathKind,
                dwellT = t.dwellT, s = t.s, v = t.v,
                departStationId = t.departStationId, departTrack = t.departTrack,
                releaseS = t.releaseS, released = t.released, legSegmentId = t.legSegmentId,
                onboard = t.onboard, departureCount = t.departureCount, arrivalCount = t.arrivalCount,
                transitSegmentIds = null, transitStationIds = null, transitTracks = null, passedCheckpoints = 0,
            };
        }
        d.tr = trList;
        return d;
    }

    // v2(ホーム縁の乗降モードが存在しない)をv3形式へ正規化する。全てのホーム縁は
    // 既定Normalとする(不可逆ではあるが、既定Normal=既存挙動そのままなので
    // 「v2保存直前のcontinued world」と一致しないのはstate/s/v等、既にM2-Cで
    // 不可逆と整理済みの項目に限られる。ホーム縁自体はv2に概念が無いだけで
    // 挙動上の相違は生まない)
    static GameDataV3 MigrateV2ToV3(GameDataV2 v2)
    {
        var d = new GameDataV3
        {
            money = v2.money, carried = v2.carried, minutes = v2.minutes, speed = v2.speed,
            randomState = v2.randomState, nameCounter = v2.nameCounter,
            stationIdCounter = v2.stationIdCounter, segmentIdCounter = v2.segmentIdCounter,
            trainIdCounter = v2.trainIdCounter, lineIdCounter = v2.lineIdCounter,
            seg = v2.seg, tr = v2.tr, ln = v2.ln,
        };
        var stList = new StDataV3[v2.st.Length];
        for (int i = 0; i < v2.st.Length; i++)
        {
            var sd = v2.st[i];
            stList[i] = new StDataV3
            {
                id = sd.id, x = sd.x, z = sd.z, yaw = sd.yaw,
                cars = sd.cars, faces = sd.faces, lines = sd.lines,
                name = sd.name, dev = sd.dev, spawnAcc = sd.spawnAcc,
                waitToId = sd.waitToId, waitN = sd.waitN,
                edgeOverrides = null, // 全てNormal
            };
        }
        d.st = stList;
        return d;
    }

    // v1(index参照・state/s/v/onboard/spawnAcc等が存在しない)をv2形式へ正規化する。
    // 不可逆: 走行中列車はDwellへ、乗車中旅客は空へ、GameRandomは既定初期値へ
    // 正規化される。「v1保存直前のcontinued world」とは一致しない
    static bool SegmentConnects(SegDataV2[] segs, int aId, int bId)
    {
        foreach (var g in segs)
            if ((g.aId == aId && g.bId == bId) || (g.aId == bId && g.bId == aId)) return true;
        return false;
    }

    // 通過駅(スキップストップ)対応: aIdからbIdへ、直結でなくても線路を辿って到達できるか。
    // Train.TryDepart/TrackNetwork.FindPathと同じBFSだが、ロード検証時点ではまだ生きた
    // TrackNetworkが存在しないため、セーブデータ自身のseg配列だけで完結させる
    static bool RoutePathExists(SegDataV2[] segs, int aId, int bId)
    {
        if (aId == bId) return true;
        var seen = new HashSet<int> { aId };
        var q = new Queue<int>();
        q.Enqueue(aId);
        while (q.Count > 0)
        {
            int cur = q.Dequeue();
            foreach (var g in segs)
            {
                int other = g.aId == cur ? g.bId : (g.bId == cur ? g.aId : -1);
                if (other == -1) continue;
                if (other == bId) return true;
                if (seen.Add(other)) q.Enqueue(other);
            }
        }
        return false;
    }

    static GameDataV2 MigrateV1ToV2(GameData v1)
    {
        var d = new GameDataV2
        {
            money = v1.money,
            carried = v1.carried,
            minutes = v1.minutes,
            speed = v1.speed,
            randomState = 0x9E3779B9u, // GameRandom.Seed(0)相当の既定初期値(0は不動点のため代替)
            nameCounter = v1.nameCounter,
            lineIdCounter = v1.lineIdCounter,
        };

        int nextStationId = 0;
        var stationIdOfIndex = new int[v1.st.Count];
        var stList = new List<StDataV2>();
        for (int i = 0; i < v1.st.Count; i++)
        {
            var sd = v1.st[i];
            int id = ++nextStationId;
            stationIdOfIndex[i] = id;
            stList.Add(new StDataV2
            {
                id = id, x = sd.x, z = sd.z, yaw = sd.yaw,
                cars = sd.cars, faces = sd.faces, lines = sd.lines,
                name = sd.name, dev = sd.dev, spawnAcc = 0.0,
            });
        }
        for (int i = 0; i < v1.st.Count; i++)
        {
            var sd = v1.st[i];
            if (sd.waitTo == null || sd.waitN == null) continue;
            var to = new List<int>(); var n = new List<int>();
            for (int k = 0; k < sd.waitTo.Length && k < sd.waitN.Length; k++)
            {
                if (sd.waitTo[k] < 0 || sd.waitTo[k] >= stationIdOfIndex.Length) continue;
                to.Add(stationIdOfIndex[sd.waitTo[k]]);
                n.Add(sd.waitN[k]);
            }
            stList[i].waitToId = to.ToArray();
            stList[i].waitN = n.ToArray();
        }
        d.st = stList.ToArray();

        int nextSegId = 0;
        var segList = new List<SegDataV2>();
        if (v1.seg != null)
            foreach (var gd in v1.seg)
            {
                if (gd.a < 0 || gd.a >= stationIdOfIndex.Length || gd.b < 0 || gd.b >= stationIdOfIndex.Length) continue;
                segList.Add(new SegDataV2
                {
                    id = ++nextSegId, aId = stationIdOfIndex[gd.a], bId = stationIdOfIndex[gd.b],
                    sa = gd.sa, sb = gd.sb,
                });
            }
        d.seg = segList.ToArray();

        var lnList = new List<LnDataV2>();
        if (v1.ln != null)
            foreach (var ld in v1.ln)
            {
                if (ld.route == null || ld.route.Length < 2) continue;
                var routeIds = new List<int>(); bool ok = true;
                foreach (var ri in ld.route)
                {
                    if (ri < 0 || ri >= stationIdOfIndex.Length) { ok = false; break; }
                    routeIds.Add(stationIdOfIndex[ri]);
                }
                if (!ok) continue;
                // v2では「tracks配列長==経路長」を要求するため、v1の(欠落し得る)
                // tracks配列を経路長へ正規化する。系統(ServiceLine)の-1(動的選択)は
                // Trainの発車ロジックとは異なりBuildController.DispatchTrain側が
                // 対応していない(TryReserveSpecific(-1)は常にfalseになり配車不能に
                // なる、実装後レビューでCodex CLIが指摘)ため、系統のtracksは欠落分を
                // その駅の先頭停車線へ具体的に解決する(旧v1 Load()と同じ挙動)
                var lnTracks = new int[routeIds.Count];
                for (int i = 0; i < lnTracks.Length; i++)
                {
                    int t = (ld.tracks != null && i < ld.tracks.Length) ? ld.tracks[i] : -1;
                    if (t < 0)
                    {
                        var origSt = v1.st[ld.route[i]];
                        var layout = StationLayout.Compute(origSt.faces, origSt.lines);
                        t = layout.stopTracks.Count > 0 ? layout.stopTracks[0] : 0;
                    }
                    lnTracks[i] = t;
                }
                lnList.Add(new LnDataV2
                {
                    id = ld.id, typeIdx = ld.typeIdx, name = ld.name,
                    routeIds = routeIds.ToArray(), tracks = lnTracks,
                });
            }
        d.ln = lnList.ToArray();

        int nextTrainId = 0;
        var trList = new List<TrDataV2>();
        if (v1.tr != null)
            foreach (var td in v1.tr)
            {
                if (td.route == null || td.route.Length < 2) continue;
                var routeIds = new List<int>(); bool ok = true;
                foreach (var ri in td.route)
                {
                    if (ri < 0 || ri >= stationIdOfIndex.Length) { ok = false; break; }
                    routeIds.Add(stationIdOfIndex[ri]);
                }
                if (!ok) continue;
                int startIdx = Mathf.Clamp(td.idx, 0, routeIds.Count - 1);
                int curTrack = (td.tracks != null && startIdx < td.tracks.Length && td.tracks[startIdx] >= 0) ? td.tracks[startIdx] : -1;
                if (curTrack < 0)
                {
                    // 旧セーブにtrack指定が無い(または壊れている)場合、そのStationの
                    // 停車線レイアウトから先頭の停車線を割り当てる(旧Load()の
                    // 「tracks無しなら各駅の停車線を自動割当」と同じフォールバック)
                    var origSt = v1.st[td.route[startIdx]];
                    var layout = StationLayout.Compute(origSt.faces, origSt.lines);
                    curTrack = layout.stopTracks.Count > 0 ? layout.stopTracks[0] : 0;
                }
                var lineIds = td.lineIds != null && td.lineIds.Length > 0 ? td.lineIds
                    : (td.lineId >= 0 ? new[] { td.lineId } : new int[0]);
                // v2では「tracks配列長==経路長」を要求するため、v1の(欠落し得る)
                // tracks配列を経路長へ正規化する(欠落分は-1=動的選択とする)
                var trTracks = new int[routeIds.Count];
                for (int i = 0; i < trTracks.Length; i++)
                    trTracks[i] = (td.tracks != null && i < td.tracks.Length) ? td.tracks[i] : -1;
                trList.Add(new TrDataV2
                {
                    id = ++nextTrainId,
                    typeId = td.typeId, cars = td.cars,
                    routeIds = routeIds.ToArray(), tracks = trTracks, lineIds = lineIds,
                    idx = startIdx, dir = td.dir >= 0 ? 1 : -1, curTrack = curTrack,
                    // v1にはcyclicの概念が無いため、移行後のネットワーク(d.seg)から
                    // 経路末尾-先頭間が接続されているかを見て導出する(Train.Init()と同じ判定)
                    cyclic = routeIds.Count >= 2 && SegmentConnects(d.seg, routeIds[routeIds.Count - 1], routeIds[0]),
                    state = 0, dwellPathKind = 0, dwellT = 8f, s = 0f, v = 0f,
                    departStationId = 0, departTrack = 0, releaseS = 0f, released = true,
                    legSegmentId = 0, onboard = new OnboardGroupV2[0],
                    departureCount = 0, arrivalCount = 0,
                });
            }
        d.tr = trList.ToArray();

        d.stationIdCounter = nextStationId;
        d.segmentIdCounter = nextSegId;
        d.trainIdCounter = nextTrainId;
        return d;
    }

    // 参照整合性・値域・重複ID・重複予約claimを、GameObjectを一切生成せずに検証する。
    // ここを通過すれば ApplyToWorld は原則として失敗しない
    static bool Validate(GameDataV5 d, out string error)
    {
        error = null;
        // JsonUtilityはJSONに存在しない配列フィールドをnullのまま残す(空配列にはならない)。
        // 以降ApplyToWorldでも同じdを使い回すため、ここで一度だけ正規化しておく
        if (d.st == null) d.st = new StDataV4[0];
        if (d.seg == null) d.seg = new SegDataV2[0];
        if (d.ln == null) d.ln = new LnDataV2[0];
        if (d.tr == null) d.tr = new TrDataV3[0];
        if (double.IsNaN(d.money) || double.IsInfinity(d.money)) { error = "money不正"; return false; }
        if (float.IsNaN(d.minutes) || float.IsInfinity(d.minutes)) { error = "minutes不正"; return false; }
        // speedを未検証のままにすると、欠落(=0)時にApply側の「0以下なら維持」という
        // 分岐でロード前の値が残ってしまい、結果がロード前の状態に依存してしまう
        // (実装後レビューでCodex CLIが指摘)。欠落・不正値は明示的にロード失敗とする
        if (float.IsNaN(d.speed) || float.IsInfinity(d.speed) || d.speed <= 0) { error = "speedが不正(欠落または0以下)"; return false; }
        // randomStateは0を許容しない。GameRandom.SetState(0)は0を不動点回避のため
        // 0x9E3779B9へ代入するが、それは「欠落フィールドがたまたま0になった」ケースを
        // 正規の値として誤って受理してしまうことになる
        if (d.randomState == 0) { error = "randomStateが不正(欠落または0)"; return false; }

        var stationIds = new HashSet<int>();
        var layoutById = new Dictionary<int, StationLayout.Result>();
        int maxStationId = 0;
        foreach (var sd in d.st)
        {
            if (sd.id <= 0) { error = "station idが不正(0以下): " + sd.id; return false; }
            if (!stationIds.Add(sd.id)) { error = "station id重複: " + sd.id; return false; }
            // UIが許す範囲(cars 2-10, faces 1-4, lines 1-8)を超える巨大値は、
            // StationLayout.Computeの計算コスト・配列サイズの観点からも拒否する
            // (実装後レビューでCodex CLIが指摘)
            if (sd.cars < 2 || sd.cars > 10 || sd.faces < 1 || sd.faces > 4 || sd.lines < 1 || sd.lines > 8)
            { error = "station構成が範囲外: " + sd.id; return false; }
            // M2-D: 面数より線数が1つ少なくても各面が物理線に接続できる
            // (例: 3面2線、外側|1番線|島式|2番線|外側)。最低構成はMax(1,faces-1)
            if (sd.lines < Mathf.Max(1, sd.faces - 1)) { error = "station構成が不正(lines不足): " + sd.id; return false; }
            // 階はUIが出せる範囲(地上〜4階)のみ。将来の地下は負値で広げる
            if (sd.level < 0 || sd.level > 3) { error = "station levelが範囲外: " + sd.id; return false; }
            if (float.IsNaN(sd.x) || float.IsInfinity(sd.x) || float.IsNaN(sd.z) || float.IsInfinity(sd.z)
                || float.IsNaN(sd.yaw) || float.IsInfinity(sd.yaw)) { error = "station座標が不正: " + sd.id; return false; }
            // devは行き先抽選の重み・spawnAcc更新レートに使われるため、NaN/Infinityが
            // 混入すると旅客生成ロジック全体へ伝播する(実装後レビューでCodex CLIが指摘)
            if (sd.dev < 0 || float.IsNaN(sd.dev) || float.IsInfinity(sd.dev)) { error = "dev不正: " + sd.id; return false; }
            if (sd.spawnAcc < 0 || double.IsNaN(sd.spawnAcc) || double.IsInfinity(sd.spawnAcc)) { error = "spawnAcc不正: " + sd.id; return false; }
            if ((sd.waitToId == null) != (sd.waitN == null)) { error = "waiting配列が片方だけnull: " + sd.id; return false; }
            long cap = (long)sd.faces * sd.cars * 60;
            long total = 0;
            if (sd.waitToId != null && sd.waitN != null)
            {
                if (sd.waitToId.Length != sd.waitN.Length) { error = "waiting配列長不一致: " + sd.id; return false; }
                foreach (var n in sd.waitN) { if (n < 0) { error = "waiting人数が負: " + sd.id; return false; } total += n; }
            }
            if (total > cap) { error = "WaitingCap超過: station " + sd.id; return false; }
            var stLayout = StationLayout.Compute(sd.faces, sd.lines);
            layoutById[sd.id] = stLayout;
            if (sd.id > maxStationId) maxStationId = sd.id;

            // M2-D: ホーム縁の上書き設定を検証する。重複キー・不正side・不正mode・
            // 実在しない(trackIndex,side)の組を全て拒否する
            if (sd.edgeOverrides != null)
            {
                var seenKeys = new HashSet<long>();
                foreach (var ov in sd.edgeOverrides)
                {
                    if (ov.side != -1 && ov.side != 1) { error = "edgeOverrideのsideが不正: station " + sd.id; return false; }
                    if (ov.mode < 0 || ov.mode > 3) { error = "edgeOverrideのmodeが不正: station " + sd.id; return false; }
                    long key = ((long)ov.trackIndex << 32) | (uint)(ov.side + 1);
                    if (!seenKeys.Add(key)) { error = "edgeOverrideが重複: station " + sd.id + " track " + ov.trackIndex; return false; }
                    bool exists = false;
                    foreach (var e in stLayout.edges)
                        if (e.trackIndex == ov.trackIndex && e.side == ov.side) { exists = true; break; }
                    if (!exists) { error = "edgeOverrideが実在しないホーム縁を参照: station " + sd.id + " track " + ov.trackIndex + " side " + ov.side; return false; }
                }
            }
        }
        if (d.stationIdCounter < maxStationId) { error = "stationIdCounterが既存最大IDより小さい"; return false; }

        // trackが-1(動的選択、TryReserveForに委ねる)か、その駅の実在する停車線か
        bool TrackValid(int track, int stationId) =>
            track == -1 || (track >= 0 && layoutById.TryGetValue(stationId, out var lay) && lay.stopTracks.Contains(track));
        // 「現在位置」を表すtrack(curTrack/発車駅departTrack)は-1(未確定)を許さない
        bool CurrentTrackValid(int track, int stationId) =>
            track >= 0 && layoutById.TryGetValue(stationId, out var lay) && lay.stopTracks.Contains(track);

        foreach (var sd in d.st)
        {
            if (sd.waitToId == null) continue;
            foreach (var wid in sd.waitToId)
                if (!stationIds.Contains(wid)) { error = "waitingが不明な駅を参照: station " + sd.id + " -> " + wid; return false; }
        }

        var segIds = new HashSet<int>();
        var segEndpoints = new HashSet<long>(); // (aId,bId)無向ペアの重複検出用
        int maxSegId = 0;
        foreach (var gd in d.seg)
        {
            if (gd.id <= 0) { error = "segment idが不正(0以下): " + gd.id; return false; }
            if (!segIds.Add(gd.id)) { error = "segment id重複: " + gd.id; return false; }
            if (!stationIds.Contains(gd.aId) || !stationIds.Contains(gd.bId)) { error = "segmentの端点が不明: " + gd.id; return false; }
            if (gd.aId == gd.bId) { error = "segmentの端点が同一駅: " + gd.id; return false; }
            if (gd.sa != 1 && gd.sa != -1) { error = "segment signA不正: " + gd.id; return false; }
            if (gd.sb != 1 && gd.sb != -1) { error = "segment signB不正: " + gd.id; return false; }
            if (gd.bedType < (int)TrackBedType.Ballast || gd.bedType > (int)TrackBedType.Slab)
            { error = "segment bedType不正: " + gd.id; return false; }
            long key = gd.aId < gd.bId ? ((long)gd.aId << 32) | (uint)gd.bId : ((long)gd.bId << 32) | (uint)gd.aId;
            if (!segEndpoints.Add(key)) { error = "同一駅間に重複線路: " + gd.aId + "-" + gd.bId; return false; }
            if (gd.id > maxSegId) maxSegId = gd.id;
        }
        if (d.segmentIdCounter < maxSegId) { error = "segmentIdCounterが既存最大IDより小さい"; return false; }

        var lineIds = new HashSet<int>();
        // 経路上の隣接する停車駅同士が、直結でなくても線路を辿って到達できるか
        // (到達できない経路を持つ列車はロード成功後、TryDepart()が経路を見つけられず
        // 永久に発車できなくなる。実装後レビューでCodex CLIが指摘。M2-Eで通過駅
        // (スキップストップ)対応により、直結必須からRoutePathExists(到達可能)へ緩和)
        bool RouteAdjacent(int[] routeIds)
        {
            for (int i = 0; i + 1 < routeIds.Length; i++)
                if (!RoutePathExists(d.seg, routeIds[i], routeIds[i + 1])) return false;
            return true;
        }

        foreach (var ld in d.ln)
        {
            if (ld.id <= 0) { error = "line id不正: " + ld.id; return false; }
            if (!lineIds.Add(ld.id)) { error = "line id重複: " + ld.id; return false; }
            if (ld.routeIds == null || ld.routeIds.Length < 2) { error = "line経路が短すぎる: " + ld.id; return false; }
            foreach (var sid in ld.routeIds)
                if (!stationIds.Contains(sid)) { error = "lineが不明な駅を参照: " + ld.id; return false; }
            if (!RouteAdjacent(ld.routeIds)) { error = "lineの経路上の隣接駅が線路で結ばれていない: " + ld.id; return false; }
            // ServiceLineのtracksは-1(動的選択)を許さない。BuildController.DispatchTrain
            // はTryReserveSpecific(tracks[i])を無条件に呼ぶため(TryReserveForのような
            // 動的解決に対応していない)、-1が入っていると空き番線があっても配車不能に
            // なる(実装後レビューでCodex CLIが指摘)。Trainのroute Tracksとは異なり、
            // 必ず具体的な番線を要求する
            if (ld.tracks == null || ld.tracks.Length != ld.routeIds.Length)
            { error = "lineのtracksが未指定または経路長と不一致: " + ld.id; return false; }
            for (int i = 0; i < ld.tracks.Length; i++)
                if (!CurrentTrackValid(ld.tracks[i], ld.routeIds[i])) { error = "lineの番線が不正(-1不可): " + ld.id + " stop " + i; return false; }
        }
        if (d.lineIdCounter < 0) { error = "lineIdCounterが不正"; return false; }
        foreach (var ld in d.ln) if (d.lineIdCounter < ld.id) { error = "lineIdCounterが既存最大IDより小さい"; return false; }

        var trainIds = new HashSet<int>();
        // 予約claim検証用: (stationId,trackIndex)->trainId, (segmentId,fromStationId)->trainId
        var platformClaims = new HashSet<long>();
        var blockClaims = new HashSet<long>();
        var segById = new Dictionary<int, SegDataV2>();
        foreach (var gd in d.seg) segById[gd.id] = gd;

        int maxTrainId = 0;
        foreach (var td in d.tr)
        {
            if (td.id <= 0) { error = "train idが不正(0以下): " + td.id; return false; }
            if (!trainIds.Add(td.id)) { error = "train id重複: " + td.id; return false; }
            TrainCatalog.Formation formation = null;
            foreach (var f in TrainCatalog.Formations)
                if (f.type.id == td.typeId && f.cars == td.cars) { formation = f; break; }
            if (formation == null) { error = "未知の編成: " + td.typeId + "/" + td.cars + "両 (train " + td.id + ")"; return false; }
            if (td.routeIds == null || td.routeIds.Length < 2) { error = "train経路が短すぎる: " + td.id; return false; }
            foreach (var sid in td.routeIds)
                if (!stationIds.Contains(sid)) { error = "trainが不明な駅を参照: " + td.id; return false; }
            if (!RouteAdjacent(td.routeIds)) { error = "trainの経路上の隣接駅が線路で結ばれていない: " + td.id; return false; }
            // cyclic=trueの場合、TryDepart()は末尾到達時に先頭駅への経路を探す
            // (Train.cs内のcyclic分岐、通過駅を挟んでもよい)。末尾-先頭間が到達不能だと
            // 永久に発車できなくなる(実装後レビューでCodex CLIが指摘)
            if (td.cyclic && !RoutePathExists(d.seg, td.routeIds[td.routeIds.Length - 1], td.routeIds[0]))
            { error = "trainがcyclic=trueだが経路の末尾-先頭が線路で到達できない: " + td.id; return false; }
            if (td.idx < 0 || td.idx >= td.routeIds.Length) { error = "train idx範囲外: " + td.id; return false; }
            if (td.dir != 1 && td.dir != -1) { error = "train dir不正: " + td.id; return false; }
            if (float.IsNaN(td.s) || float.IsInfinity(td.s) || td.s < 0) { error = "train sが不正(負値/NaN): " + td.id; return false; }
            if (td.departureCount < 0 || td.arrivalCount < 0) { error = "train統計カウンタが負: " + td.id; return false; }
            if (float.IsNaN(td.v) || float.IsInfinity(td.v) || td.v < 0) { error = "train vが不正(負値/NaN): " + td.id; return false; }
            if (float.IsNaN(td.dwellT) || float.IsInfinity(td.dwellT)) { error = "train dwellTがNaN: " + td.id; return false; }
            if (float.IsNaN(td.releaseS) || float.IsInfinity(td.releaseS)) { error = "train releaseSがNaN: " + td.id; return false; }
            if (td.tracks != null)
            {
                if (td.tracks.Length != td.routeIds.Length) { error = "trainのtracks配列長が経路長と不一致: " + td.id; return false; }
                for (int i = 0; i < td.tracks.Length; i++)
                    if (!TrackValid(td.tracks[i], td.routeIds[i])) { error = "trainの番線が不正: " + td.id + " stop " + i; return false; }
            }
            if (td.lineIds != null)
                foreach (var lid in td.lineIds)
                    if (!lineIds.Contains(lid)) { error = "trainが不明な系統を参照: " + td.id + " -> " + lid; return false; }

            int destStationId = td.routeIds[td.idx];
            if (!CurrentTrackValid(td.curTrack, destStationId)) { error = "train curTrackが現在駅の有効な停車線でない: " + td.id; return false; }

            long onboardTotal = 0;
            if (td.onboard != null)
                foreach (var ob in td.onboard)
                {
                    if (!stationIds.Contains(ob.destId)) { error = "onboardが不明な駅を参照: " + td.id + " -> " + ob.destId; return false; }
                    if (ob.count <= 0) { error = "onboardの人数が不正: " + td.id; return false; }
                    if (float.IsNaN(ob.bx) || float.IsInfinity(ob.bx) || float.IsNaN(ob.by) || float.IsInfinity(ob.by)
                        || float.IsNaN(ob.bz) || float.IsInfinity(ob.bz)) { error = "onboardの乗車位置が不正: " + td.id; return false; }
                    onboardTotal += ob.count;
                }
            if (onboardTotal > formation.Capacity) { error = "onboard人数が定員超過: " + td.id; return false; }

            if (td.state == 1) // Run
            {
                if (td.legSegmentId == 0 || !segById.TryGetValue(td.legSegmentId, out var seg))
                { error = "Run列車のlegSegmentが不明: " + td.id; return false; }
                if (td.departStationId == 0 || !stationIds.Contains(td.departStationId))
                { error = "Run列車のdepartStationが不明: " + td.id; return false; }

                // 通過駅(スキップストップ)を挟む多区間かどうか。null/長さ1以下なら
                // 従来通り直結2駅の単一区間(legSegmentは発車駅から直接到着駅へ繋がる)
                bool hasTransit = td.transitSegmentIds != null && td.transitSegmentIds.Length > 1;
                int finalSegFromId; // legSegment(=末尾の区間)が実際にどの駅から入るか
                if (hasTransit)
                {
                    int m = td.transitSegmentIds.Length;
                    if (td.transitStationIds == null || td.transitStationIds.Length != m - 1)
                    { error = "transitStationIds長が不正: " + td.id; return false; }
                    if (td.transitTracks == null || td.transitTracks.Length != m - 1)
                    { error = "transitTracks長が不正: " + td.id; return false; }
                    if (td.transitSegmentIds[m - 1] != td.legSegmentId)
                    { error = "transitSegmentIdsの末尾がlegSegmentIdと不一致: " + td.id; return false; }
                    if (td.passedCheckpoints < 0 || td.passedCheckpoints > (m - 1) * 2)
                    { error = "passedCheckpointsが範囲外: " + td.id; return false; }

                    // チェーンが実際にdepartStation→到着駅を連続して繋いでいるか、
                    // 各通過駅・番線の妥当性を1ホップずつ検証する
                    int prevId = td.departStationId;
                    for (int i = 0; i < m; i++)
                    {
                        if (!segById.TryGetValue(td.transitSegmentIds[i], out var hopSeg))
                        { error = "transitSegmentが不明: " + td.id + " at " + i; return false; }
                        int otherEnd = hopSeg.aId == prevId ? hopSeg.bId : (hopSeg.bId == prevId ? hopSeg.aId : -1);
                        if (otherEnd == -1) { error = "transitSegmentsが連続していない: " + td.id + " at " + i; return false; }
                        int expected = i < m - 1 ? td.transitStationIds[i] : destStationId;
                        if (otherEnd != expected) { error = "transitSegmentsの経由駅が不一致: " + td.id + " at " + i; return false; }
                        if (i < m - 1)
                        {
                            if (!stationIds.Contains(td.transitStationIds[i])) { error = "transitStationが不明: " + td.id; return false; }
                            if (!TrackValid(td.transitTracks[i], td.transitStationIds[i])) { error = "transitTrackが不正: " + td.id; return false; }
                        }
                        prevId = expected;
                    }
                    finalSegFromId = td.transitStationIds[m - 2];
                }
                else
                {
                    if (td.transitStationIds != null && td.transitStationIds.Length > 0)
                    { error = "transitStationIdsがあるのにtransitSegmentIdsが不正: " + td.id; return false; }
                    finalSegFromId = td.departStationId;
                }

                bool legConnectsProperly = (seg.aId == finalSegFromId && seg.bId == destStationId)
                    || (seg.bId == finalSegFromId && seg.aId == destStationId);
                if (!legConnectsProperly)
                { error = "Run列車のlegSegmentが発着駅を結んでいない: " + td.id; return false; }
                if (!CurrentTrackValid(td.departTrack, td.departStationId))
                { error = "Run列車のdepartTrackが発車駅の有効な停車線でない: " + td.id; return false; }

                // 到着駅の番線は常に確保済み(TryDepart時点で確保してから発車するため)
                long platKey = ((long)destStationId << 32) | (uint)td.curTrack;
                if (!platformClaims.Add(platKey)) { error = "到着番線の二重予約: station " + destStationId + " track " + td.curTrack; return false; }
                long blockKey = ((long)td.legSegmentId << 32) | (uint)finalSegFromId;
                if (!blockClaims.Add(blockKey)) { error = "閉塞の二重予約: segment " + td.legSegmentId + " from " + finalSegFromId; return false; }
                // released==falseの間は発車駅の番線もまだ解放されていない(SimTickの
                // releaseS通過処理まで占有継続)。[Train.cs:221-224]で確認
                if (!td.released)
                {
                    long depPlatKey = ((long)td.departStationId << 32) | (uint)td.departTrack;
                    if (!platformClaims.Add(depPlatKey)) { error = "発車駅番線の二重予約: station " + td.departStationId + " track " + td.departTrack; return false; }
                }

                // 通過区間・通過駅番線のうち、まだ解放されていない(passedCheckpoints未満で
                // ない)分をclaimへ追加する(通過駅ごとに「区間」「番線」の2チェックポイントが
                // ある規約。最終区間はblockKeyで既にclaim済みなのでここには含めない)
                if (hasTransit)
                {
                    int m = td.transitSegmentIds.Length;
                    int prevId2 = td.departStationId;
                    for (int i = 0; i < m - 1; i++)
                    {
                        int segCpIndex = i * 2, trackCpIndex = i * 2 + 1;
                        if (segCpIndex >= td.passedCheckpoints)
                        {
                            long bKey = ((long)td.transitSegmentIds[i] << 32) | (uint)prevId2;
                            if (!blockClaims.Add(bKey)) { error = "通過区間の二重予約: segment " + td.transitSegmentIds[i] + " (train " + td.id + ")"; return false; }
                        }
                        if (trackCpIndex >= td.passedCheckpoints)
                        {
                            long pKey = ((long)td.transitStationIds[i] << 32) | (uint)td.transitTracks[i];
                            if (!platformClaims.Add(pKey)) { error = "通過駅番線の二重予約: station " + td.transitStationIds[i] + " track " + td.transitTracks[i] + " (train " + td.id + ")"; return false; }
                        }
                        prevId2 = td.transitStationIds[i];
                    }
                }
            }
            else if (td.state == 0) // Dwell
            {
                if (td.dwellPathKind == 1)
                {
                    if (td.legSegmentId == 0 || !segById.TryGetValue(td.legSegmentId, out var seg))
                    { error = "JustArrivedLeg列車のlegSegmentが不明: " + td.id; return false; }
                    if (td.departStationId == 0 || !stationIds.Contains(td.departStationId))
                    { error = "JustArrivedLeg列車のdepartStationが不明: " + td.id; return false; }
                    bool depIsEndpoint = seg.aId == td.departStationId || seg.bId == td.departStationId;
                    bool destIsOtherEndpoint = (seg.aId == td.departStationId && seg.bId == destStationId) || (seg.bId == td.departStationId && seg.aId == destStationId);
                    if (!depIsEndpoint || !destIsOtherEndpoint)
                    { error = "JustArrivedLeg列車のlegSegmentが発着駅を結んでいない: " + td.id; return false; }
                    if (!CurrentTrackValid(td.departTrack, td.departStationId))
                    { error = "JustArrivedLeg列車のdepartTrackが発車駅の有効な停車線でない(path再構築に必要): " + td.id; return false; }
                }
                else if (td.dwellPathKind != 0)
                { error = "train dwellPathKind不正: " + td.id; return false; }

                long platKey = ((long)destStationId << 32) | (uint)td.curTrack;
                if (!platformClaims.Add(platKey)) { error = "停車番線の二重予約: station " + destStationId + " track " + td.curTrack; return false; }
            }
            else
            {
                error = "train state不正: " + td.id;
                return false;
            }
            if (td.id > maxTrainId) maxTrainId = td.id;
        }
        if (d.trainIdCounter < maxTrainId) { error = "trainIdCounterが既存最大IDより小さい"; return false; }

        return true;
    }

    // Validateを通過したデータをワールドへ適用する。実際のワールド生成は
    // 一時的なstaging root配下(現在のBuildController.WorldRootとは別)で行い、
    // TrackNetwork/Services/GameStateへは一切触れない。生成が最後まで成功した
    // 場合にのみ、既存ワールドを破棄してstaging rootへ丸ごと差し替える(commit)。
    // 途中で例外が起きた場合はstaging rootだけを破棄し、既存ワールド・GameState・
    // TrackNetwork/Servicesは一切変更されない(実装後レビューでCodex CLIが指摘した
    // 「ロードがトランザクショナルでない」Critical指摘への対応)
    static bool ApplyToWorld(GameDataV5 d, out string error)
    {
        error = null;
        var stagingRoot = new GameObject("LoadStaging");
        stagingRoot.SetActive(false); // 構築中にUpdate等が走らないよう非アクティブに保つ
        try
        {
            // 駅はstable ID昇順で生成する(旅客の行き先抽選・乗車順の決定性のため。
            // Station.Tick/Train.BoardはいずれもTrackNetwork.stationsの登録順に依存する)
            var sortedSt = new List<StDataV4>(d.st);
            sortedSt.Sort((x, y) => x.id.CompareTo(y.id));
            var stagedStations = new List<Station>();
            var stById = new Dictionary<int, Station>();
            foreach (var sd in sortedSt)
            {
                var go = new GameObject(sd.name);
                go.transform.SetParent(stagingRoot.transform, false);
                go.transform.SetPositionAndRotation(
                    new Vector3(sd.x, RailDimensions.HeightOfLevel(sd.level), sd.z),
                    Quaternion.Euler(0, sd.yaw, 0));
                var s = go.AddComponent<Station>();
                s.id = sd.id;
                s.level = sd.level;
                s.cars = sd.cars;
                s.faces = sd.faces;
                s.lines = sd.lines;
                s.stationName = sd.name;
                s.dev = sd.dev;
                s.Build();
                s.RestoreSpawnAccumulator(sd.spawnAcc);
                // M2-D: ホーム縁の上書き設定を反映する(Validateで実在性を確認済みのため
                // 失敗しない前提)。省略された縁は既定Normalのまま
                if (sd.edgeOverrides != null)
                    foreach (var ov in sd.edgeOverrides)
                        s.SetPlatformEdgeMode(ov.trackIndex, ov.side, (StationLayout.PlatformEdgeMode)ov.mode);
                stagedStations.Add(s);
                stById[sd.id] = s;
            }
            foreach (var sd in sortedSt)
            {
                if (sd.waitToId == null || sd.waitN == null) continue;
                var st = stById[sd.id];
                for (int k = 0; k < sd.waitToId.Length && k < sd.waitN.Length; k++)
                    if (stById.TryGetValue(sd.waitToId[k], out var dest))
                        st.waiting[dest] = sd.waitN[k];
                st.UpdateLabel();
            }

            var stagedSegments = new List<TrackSegment>();
            var segById = new Dictionary<int, TrackSegment>();
            foreach (var gd in d.seg)
            {
                var seg = new TrackSegment
                {
                    id = gd.id,
                    a = stById[gd.aId],
                    b = stById[gd.bId],
                    signA = gd.sa,
                    signB = gd.sb,
                    bedType = (TrackBedType)gd.bedType,
                };
                seg.Build(stagingRoot.transform);
                stagedSegments.Add(seg);
                segById[gd.id] = seg;
            }
            // RebuildTrackVisual/CityGrid.DevelopはTrackNetwork.stations/segments(現行の
            // 生きたワールド)を参照するため、staging中には呼べない。commit後にまとめて行う

            var stagedLines = new List<ServiceLine>();
            foreach (var ld in d.ln)
            {
                var route = new List<Station>();
                var tracks = new List<int>();
                for (int i = 0; i < ld.routeIds.Length; i++)
                {
                    route.Add(stById[ld.routeIds[i]]);
                    // -1は「動的選択(TryReserveForに任せる)」を意味する正当な値であり、
                    // ここで固定番線へ解決してしまうと将来の番線選択結果が変わってしまう
                    // (実装後レビューでCodex CLIが指摘)。Validateで既に「-1または有効な
                    // 停車線」であることを保証済みなので、ここでは値をそのまま使う
                    int trk = (ld.tracks != null && i < ld.tracks.Length) ? ld.tracks[i] : -1;
                    tracks.Add(trk);
                }
                stagedLines.Add(new ServiceLine { id = ld.id, typeIdx = ld.typeIdx, name = ld.name, route = route, tracks = tracks });
            }

            // stable ID昇順で生成する(同tickでの番線・閉塞競合の決定性のため。
            // Bootstrap.SimTickはTrackNetwork.trainsの登録順で列車を処理する)
            var sortedTr = new List<TrDataV3>(d.tr);
            sortedTr.Sort((x, y) => x.id.CompareTo(y.id));
            var stagedTrains = new List<Train>();
            var stagedSpecs = new List<Train.RestoreSpec>();
            foreach (var td in sortedTr)
            {
                TrainCatalog.Formation fm = null;
                foreach (var f in TrainCatalog.Formations)
                    if (f.type.id == td.typeId && f.cars == td.cars) fm = f;

                var route = new List<Station>();
                foreach (var sid in td.routeIds) route.Add(stById[sid]);
                var tracks = new List<int>();
                for (int i = 0; i < route.Count; i++)
                {
                    // -1は動的選択の正当な値としてそのまま保持する(直上のコメント参照)
                    int tr = (td.tracks != null && i < td.tracks.Length) ? td.tracks[i] : -1;
                    tracks.Add(tr);
                }

                // 通過駅(スキップストップ)を挟む多区間なら、保存されたID列を生きた
                // Station/TrackSegment参照へ解決する(Validateで存在・整合性を確認済み)
                List<TrackSegment> transitSegs = null;
                List<Station> transitStations = null;
                List<int> transitTracksResolved = null;
                if (td.transitSegmentIds != null && td.transitSegmentIds.Length > 1)
                {
                    transitSegs = new List<TrackSegment>();
                    foreach (var sid in td.transitSegmentIds) transitSegs.Add(segById[sid]);
                    transitStations = new List<Station>();
                    foreach (var stid in td.transitStationIds) transitStations.Add(stById[stid]);
                    transitTracksResolved = new List<int>(td.transitTracks);
                }

                var spec = new Train.RestoreSpec
                {
                    formation = fm,
                    route = route,
                    routeTracks = tracks,
                    lineIds = td.lineIds != null ? new List<int>(td.lineIds) : new List<int>(),
                    // v2で保存されたcyclicの値をそのまま復元する(TrackNetwork.Connectedで
                    // 再計算しない)。ネットワークに後から線路が追加され、保存時にはcyclic=false
                    // だった列車の端点が偶然接続されただけで折返し挙動が変わってしまうのを防ぐため
                    // (実装後レビューでCodex CLIが指摘。v1 migrationでは移行後のネットワークから
                    // 改めて計算する。MigrateV1ToV2参照)
                    cyclic = td.cyclic,
                    idx = td.idx,
                    dir = td.dir,
                    curTrack = td.curTrack,
                    isRunning = td.state == 1,
                    dwellPathKind = td.dwellPathKind == 1 ? Train.DwellPathKind.JustArrivedLeg : Train.DwellPathKind.StationLocal,
                    dwellT = td.dwellT,
                    s = td.s,
                    v = td.v,
                    departStation = td.departStationId != 0 && stById.TryGetValue(td.departStationId, out var dst) ? dst : null,
                    departTrack = td.departTrack,
                    releaseS = td.releaseS,
                    released = td.released,
                    legSegment = td.legSegmentId != 0 && segById.TryGetValue(td.legSegmentId, out var lseg) ? lseg : null,
                    transitSegments = transitSegs,
                    transitStations = transitStations,
                    transitTracksAtStations = transitTracksResolved,
                    passedCheckpoints = td.passedCheckpoints,
                    onboard = BuildOnboard(td.onboard, stById),
                    departureCount = td.departureCount,
                    arrivalCount = td.arrivalCount,
                };

                var go = new GameObject("Train_" + (fm != null ? fm.Label : td.typeId));
                go.transform.SetParent(stagingRoot.transform, false);
                var trn = go.AddComponent<Train>();
                trn.id = td.id;
                trn.RestoreState(spec);
                stagedTrains.Add(trn);
                stagedSpecs.Add(spec);
            }

            // ここまでで全オブジェクトの構築に成功した。まだ生きたTrackNetwork/
            // Services/GameStateには一切触れていない。commit前に、例外を起こし得る
            // 残りの処理(視覚再構築・番線閉塞予約の反映)もstagedオブジェクトに対して
            // ここで済ませてしまう(実装後レビューでCodex CLIが指摘: 「commit=旧World
            // 破棄後」に例外を起こし得る処理を残すとロールバックできなくなるため、
            // 例外の可能性がある処理は全てWorldRoot差し替えより前に完了させる)
            foreach (var s in stagedStations) s.RebuildTrackVisual(stagedSegments);

            // 番線・閉塞予約をstagedオブジェクトへ反映する(Validateで一意性を確認済みの
            // ため失敗しない前提。TryReserveSpecific/TryEnterはstation/segment自身の
            // 状態のみを変更し、生きたTrackNetworkには依存しない)
            for (int i = 0; i < sortedTr.Count; i++)
            {
                var td = sortedTr[i];
                var spec = stagedSpecs[i];
                var trn = stagedTrains[i];
                spec.route[td.idx].TryReserveSpecific(td.curTrack);
                if (spec.isRunning)
                {
                    bool hasTransit = spec.transitSegments != null && spec.transitSegments.Count > 1;
                    Station finalSegFrom = hasTransit ? spec.transitStations[spec.transitStations.Count - 1] : spec.departStation;
                    spec.legSegment.TryEnter(finalSegFrom, trn);
                    // released==falseの間は発車駅の番線もまだ解放されていない
                    // (SimTickのreleaseS通過処理まで占有継続。[Train.cs:221-224]参照)
                    if (!spec.released) spec.departStation.TryReserveSpecific(spec.departTrack);

                    // 通過区間・通過駅番線のうち、まだ解放されていない(passedCheckpoints未満で
                    // ない)分を反映する(Validateの同じ規約に対応する適用側)
                    if (hasTransit)
                    {
                        int m = spec.transitSegments.Count;
                        Station prevSt = spec.departStation;
                        for (int k = 0; k < m - 1; k++)
                        {
                            int segCpIndex = k * 2, trackCpIndex = k * 2 + 1;
                            if (segCpIndex >= td.passedCheckpoints) spec.transitSegments[k].TryEnter(prevSt, trn);
                            if (trackCpIndex >= td.passedCheckpoints) spec.transitStations[k].TryReserveSpecific(spec.transitTracksAtStations[k]);
                            prevSt = spec.transitStations[k];
                        }
                    }
                }
            }

            // ここから先は例外の可能性が極めて低い単純な代入・リスト操作のみ。
            // commit本体(既存ワールドの破棄とGameState/TrackNetwork/Servicesの更新)
            BuildController.ReplaceWorldRoot(stagingRoot.transform);
            stagingRoot.SetActive(true);

            GameState.money = d.money;
            GameState.carried = d.carried;
            GameState.gameMinutes = d.minutes;
            GameState.timeScale = d.speed; // Validateでfinite>0を保証済み
            GameRandom.SetState(d.randomState);
            GameState.paused = false; // pausedはセーブスキーマ対象外(UIのセッション制御のため)

            TrackNetwork.Clear();
            Services.Clear();
            TrackNetwork.nameCounter = d.nameCounter;
            TrackNetwork.stationIdCounter = d.stationIdCounter;
            TrackNetwork.segmentIdCounter = d.segmentIdCounter;
            TrackNetwork.trainIdCounter = d.trainIdCounter;
            Services.idCounter = d.lineIdCounter;

            foreach (var s in stagedStations) TrackNetwork.stations.Add(s);
            foreach (var g in stagedSegments) TrackNetwork.segments.Add(g);
            foreach (var l in stagedLines) Services.lines.Add(l);
            foreach (var t in stagedTrains) TrackNetwork.trains.Add(t);
            TrackNetwork.MarkDirty();

            // CityGrid.Developは現状CityGrid.Enabled=false(2026-07-20時点で意図的に
            // 無効化済み)のため即returnする既定のno-opであり、街の再構築は事実上
            // 発生しない。将来のCityGrid復活マイルストーンでEnabled=trueに戻す際は、
            // TrackNetwork.stations/segments(生きたワールド)へ依存する現在の実装
            // ([CityGrid.cs:106,115,126])を、このcommit境界の外(=WorldRoot差し替え
            // より前、stagedリストを直接参照する形)へ移すこと。念のため、万一有効化
            // されていても例外でcommit済みの正常なワールドを巻き込まないよう保護する
            try { foreach (var s in TrackNetwork.stations) { s.developed = 0; CityGrid.Develop(s); } }
            catch (Exception ce) { Debug.LogError("SaveLoad: CityGrid.Develop failed post-commit (ignored): " + ce.Message); }

            return true;
        }
        catch (Exception e)
        {
            UnityEngine.Object.DestroyImmediate(stagingRoot);
            error = "適用中に例外: " + e.Message;
            return false;
        }
    }

    static List<(Station dest, int count, Vector3 boardPos)> BuildOnboard(OnboardGroupV2[] groups, Dictionary<int, Station> stById)
    {
        var list = new List<(Station, int, Vector3)>();
        if (groups == null) return list;
        foreach (var g in groups)
            if (stById.TryGetValue(g.destId, out var dest))
                list.Add((dest, g.count, new Vector3(g.bx, g.by, g.bz)));
        return list;
    }

    public static void ResetAll()
    {
        PlayerPrefs.DeleteKey(Key);
        PlayerPrefs.Save();
        suppress = true;
        TrackNetwork.Clear();
        Services.Clear();
        GameState.money = 100e8;
        GameState.carried = 0;
        GameState.gameMinutes = 6 * 60;
        GameState.timeScale = 5f;
        GameState.paused = false;
        GameRandom.Seed(0);
        UnityEngine.SceneManagement.SceneManager.LoadScene(0);
    }
}
