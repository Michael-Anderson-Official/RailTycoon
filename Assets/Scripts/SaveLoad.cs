using System;
using System.Collections.Generic;
using UnityEngine;

// セーブ/ロード。PlayerPrefs(WebGLではIndexedDB)にJSONで保存する。
// 保存タイミング: 建設・敷設・発車の直後+15秒ごとの自動保存
public static class SaveLoad
{
    const string Key = "railtycoon_save";
    public static bool suppress; // リセット直後の再保存防止

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
        public int idx, dir;
    }

    [Serializable]
    public class GameData
    {
        public int v = 1;
        public double money;
        public long carried;
        public float minutes, speed;
        public int nameCounter;
        public List<StData> st = new List<StData>();
        public List<SegData> seg = new List<SegData>();
        public List<TrData> tr = new List<TrData>();
    }

    public static void Save()
    {
        if (suppress) return;
        var d = new GameData
        {
            money = GameState.money,
            carried = GameState.carried,
            minutes = GameState.gameMinutes,
            speed = GameState.timeScale,
            nameCounter = TrackNetwork.nameCounter,
        };
        var idxOf = new Dictionary<Station, int>();
        for (int i = 0; i < TrackNetwork.stations.Count; i++) idxOf[TrackNetwork.stations[i]] = i;

        foreach (var s in TrackNetwork.stations)
        {
            var sd = new StData
            {
                x = s.transform.position.x,
                z = s.transform.position.z,
                yaw = s.transform.eulerAngles.y,
                cars = s.cars, faces = s.faces, lines = s.lines,
                name = s.stationName,
                dev = s.dev,
            };
            var to = new List<int>();
            var n = new List<int>();
            foreach (var kv in s.waiting)
                if (kv.Key != null && idxOf.ContainsKey(kv.Key)) { to.Add(idxOf[kv.Key]); n.Add(kv.Value); }
            sd.waitTo = to.ToArray();
            sd.waitN = n.ToArray();
            d.st.Add(sd);
        }
        foreach (var g in TrackNetwork.segments)
            d.seg.Add(new SegData { a = idxOf[g.a], b = idxOf[g.b], sa = g.signA, sb = g.signB });

        foreach (var t in UnityEngine.Object.FindObjectsByType<Train>(FindObjectsSortMode.None))
        {
            if (t.fm == null || t.route == null) continue;
            var r = new List<int>();
            bool ok = true;
            foreach (var s in t.route)
            {
                if (s == null || !idxOf.ContainsKey(s)) { ok = false; break; }
                r.Add(idxOf[s]);
            }
            if (!ok || r.Count < 2) continue;
            d.tr.Add(new TrData
            {
                typeId = t.fm.type.id,
                cars = t.fm.cars,
                route = r.ToArray(),
                idx = Mathf.Clamp(t.idx, 0, r.Count - 1),
                dir = t.dir,
            });
        }
        PlayerPrefs.SetString(Key, JsonUtility.ToJson(d));
        PlayerPrefs.Save();
    }

    // 保存があれば復元してtrue。列車は保存時にいた駅(または直前の目的地)に停車状態で復帰する
    public static bool Load()
    {
        if (!PlayerPrefs.HasKey(Key)) return false;
        GameData d;
        try { d = JsonUtility.FromJson<GameData>(PlayerPrefs.GetString(Key)); }
        catch (Exception e)
        {
            Debug.LogError("SaveLoad: parse failed: " + e.Message);
            return false;
        }
        if (d == null || d.st == null || d.st.Count == 0) return false;

        GameState.money = d.money;
        GameState.carried = d.carried;
        GameState.gameMinutes = d.minutes;
        if (d.speed > 0) GameState.timeScale = d.speed;
        TrackNetwork.nameCounter = d.nameCounter;

        var sts = new List<Station>();
        foreach (var sd in d.st)
        {
            var go = new GameObject(sd.name);
            go.transform.SetParent(BuildController.WorldRoot, false);
            go.transform.SetPositionAndRotation(new Vector3(sd.x, 0, sd.z), Quaternion.Euler(0, sd.yaw, 0));
            var s = go.AddComponent<Station>();
            s.cars = sd.cars;
            s.faces = sd.faces;
            s.lines = sd.lines;
            s.stationName = sd.name;
            s.dev = sd.dev; // 発展は線路構築後にまとめて行う(線路上に建てないため)
            s.Build();
            TrackNetwork.stations.Add(s);
            sts.Add(s);
        }
        for (int i = 0; i < d.st.Count; i++)
        {
            var sd = d.st[i];
            if (sd.waitTo == null || sd.waitN == null) continue;
            for (int k = 0; k < sd.waitTo.Length && k < sd.waitN.Length; k++)
                if (sd.waitTo[k] >= 0 && sd.waitTo[k] < sts.Count)
                    sts[i].waiting[sts[sd.waitTo[k]]] = sd.waitN[k];
            sts[i].UpdateLabel();
        }
        if (d.seg != null)
            foreach (var gd in d.seg)
            {
                if (gd.a < 0 || gd.a >= sts.Count || gd.b < 0 || gd.b >= sts.Count) continue;
                var seg = new TrackSegment { a = sts[gd.a], b = sts[gd.b], signA = gd.sa, signB = gd.sb };
                seg.Build(BuildController.WorldRoot);
                TrackNetwork.segments.Add(seg);
            }
        TrackNetwork.MarkDirty();

        // 線路が揃ってから街を再構築(決定的なので駅devから同じ街が復元される)
        foreach (var s in sts) { s.developed = 0; CityGrid.Develop(s); }

        if (d.tr != null)
            foreach (var td in d.tr)
            {
                TrainCatalog.Formation fm = null;
                foreach (var f in TrainCatalog.Formations)
                    if (f.type.id == td.typeId && f.cars == td.cars) fm = f;
                if (fm == null || td.route == null || td.route.Length < 2) continue;
                var route = new List<Station>();
                bool ok = true;
                foreach (var ri in td.route)
                {
                    if (ri < 0 || ri >= sts.Count) { ok = false; break; }
                    route.Add(sts[ri]);
                }
                if (!ok) continue;
                int startIdx = Mathf.Clamp(td.idx, 0, route.Count - 1);
                int track;
                if (!route[startIdx].TryReserve(out track)) continue;
                var go = new GameObject("Train_" + fm.Label);
                go.transform.SetParent(BuildController.WorldRoot, false);
                go.AddComponent<Train>().Init(fm, route, track, startIdx, td.dir);
            }
        return true;
    }

    public static void ResetAll()
    {
        PlayerPrefs.DeleteKey(Key);
        PlayerPrefs.Save();
        suppress = true;
        TrackNetwork.Clear();
        GameState.money = 100e8;
        GameState.carried = 0;
        GameState.gameMinutes = 6 * 60;
        GameState.timeScale = 5f;
        UnityEngine.SceneManagement.SceneManager.LoadScene(0);
    }
}
