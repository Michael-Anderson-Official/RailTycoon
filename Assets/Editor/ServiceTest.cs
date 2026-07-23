using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 運行系統の検証(バッチ): 系統作成→列車配車→セーブ往復→廃止。
// -executeMethod ServiceTest.Run
public static class ServiceTest
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        TrackNetwork.Clear();
        Services.Clear();
        SaveLoad.suppress = false;
        GameState.money = 100e8;

        var bc = new GameObject("BC").AddComponent<BuildController>();
        var a = MakeStation(new Vector3(-600, 0, 0), "A");
        var b = MakeStation(new Vector3(400, 0, 120), "B");
        var c = MakeStation(new Vector3(1300, 0, -160), "C");
        Connect(a, b); Connect(b, c);

        bool pass = true;

        // --- 系統作成: 特急 A→C(停車 A,B,C) ---
        bc.routeSel.Add(a); bc.routeTrackSel.Add(a.StopTracks[0]);
        bc.routeSel.Add(b); bc.routeTrackSel.Add(b.StopTracks[0]);
        bc.routeSel.Add(c); bc.routeTrackSel.Add(c.StopTracks[0]);
        bc.newLineType = 0; // 特急
        bc.SaveNewLine();
        bool createOk = Services.lines.Count == 1 && Services.lines[0].typeIdx == 0 && Services.lines[0].route.Count == 3;
        var line = Services.lines[0];
        Debug.Log("ServiceTest: create lines=" + Services.lines.Count + " name=" + line.DisplayName + " stops=" + line.route.Count);
        pass &= createOk;

        // --- 配車: 4両を2本 ---
        bc.selFormation = TrainCatalog.Formations[5];
        bc.selLines.Add(line);
        bc.DispatchTrain();
        bc.DispatchTrain();
        var trains = Object.FindObjectsByType<Train>(FindObjectsSortMode.None);
        bool dispatchOk = trains.Length == 2;
        foreach (var t in trains) if (t.lineIds == null || !t.lineIds.Contains(line.id)) dispatchOk = false;
        // TrackNetwork.trainsもDispatchTrain経由で正しく登録されていること
        // (M2-B.1でOnEnable自己登録から明示登録へ変更した箇所の回帰検出)
        bool registryOk = TrackNetwork.trains.Count == 2;
        Debug.Log("ServiceTest: dispatch trains=" + trains.Length + " lineTrainCount=" + line.TrainCount
            + " registryCount=" + TrackNetwork.trains.Count);
        pass &= dispatchOk && line.TrainCount == 2 && registryOk;

        // --- セーブ→全消去→ロード ---
        SaveLoad.Save();
        Object.DestroyImmediate(BuildController.WorldRoot.gameObject);
        TrackNetwork.Clear();
        Services.Clear();
        bool loaded = SaveLoad.Load();
        var trains2 = Object.FindObjectsByType<Train>(FindObjectsSortMode.None);
        // SaveLoad.Load経由でもTrackNetwork.trainsが正しく再構築されていること(同上の回帰検出)
        bool loadOk = loaded && Services.lines.Count == 1 && Services.lines[0].typeIdx == 0
            && Services.lines[0].route.Count == 3 && trains2.Length == 2 && TrackNetwork.trains.Count == 2;
        var line2 = Services.lines.Count > 0 ? Services.lines[0] : null;
        if (line2 != null) foreach (var t in trains2) if (t.lineIds == null || !t.lineIds.Contains(line2.id)) loadOk = false;
        Debug.Log("ServiceTest: reload loaded=" + loaded + " lines=" + Services.lines.Count
            + " trains=" + trains2.Length + " lineTrainCount=" + (line2 != null ? line2.TrainCount : -1));
        pass &= loadOk;

        // --- 廃止: 系統を消すと配属列車も消える ---
        double before = GameState.money;
        if (line2 != null) bc.DeleteLine(line2);
        var trains3 = Object.FindObjectsByType<Train>(FindObjectsSortMode.None);
        // DeleteLine経由でTrackNetwork.trainsからも正しく解除されていること(同上の回帰検出)
        bool delOk = Services.lines.Count == 0 && trains3.Length == 0 && GameState.money > before
            && TrackNetwork.trains.Count == 0;
        Debug.Log("ServiceTest: delete lines=" + Services.lines.Count + " trains=" + trains3.Length
            + " refund=" + ((GameState.money - before) / 1e8).ToString("F2") + "億円"
            + " registryCount=" + TrackNetwork.trains.Count);
        pass &= delOk;

        // --- 複数系統の運用: 各停A→B→C と 各停C→B→A を連結 ---
        // 上でセーブ全消去したので、復元後の駅を使う
        var ra = TrackNetwork.stations[0];
        var rb = TrackNetwork.stations[1];
        var rc = TrackNetwork.stations[2];
        var lFwd = new ServiceLine { id = ++Services.idCounter, typeIdx = 3, route = new List<Station> { ra, rb, rc }, tracks = new List<int> { ra.StopTracks[0], rb.StopTracks[0], rc.StopTracks[0] } };
        var lBack = new ServiceLine { id = ++Services.idCounter, typeIdx = 3, route = new List<Station> { rc, rb, ra }, tracks = new List<int> { rc.StopTracks[0], rb.StopTracks[0], ra.StopTracks[0] } };
        Services.lines.Add(lFwd); Services.lines.Add(lBack);
        BuildController.BuildItinerary(new List<ServiceLine> { lFwd, lBack }, out var itRoute, out var itTracks, out var itIds);
        // A,B,C + (Cスキップ)B,A → A,B,C,B,A → 先頭末尾A重複マージ → A,B,C,B (4駅), 2系統
        bool itinOk = itRoute.Count == 4 && itIds.Count == 2 && itTracks.Count == 4;
        Debug.Log("ServiceTest: itinerary route=" + itRoute.Count + " lines=" + itIds.Count);
        pass &= itinOk;

        bc.selFormation = TrainCatalog.Formations[5];
        bc.selLines.Clear(); bc.selLines.Add(lFwd); bc.selLines.Add(lBack);
        bc.DispatchTrain();
        var mt = Object.FindObjectsByType<Train>(FindObjectsSortMode.None);
        bool multiOk = mt.Length == 1 && mt[0].lineIds.Count == 2 && mt[0].route.Count == 4 && mt[0].cyclic;
        Debug.Log("ServiceTest: multiline train=" + mt.Length + " lineIds=" + (mt.Length > 0 ? mt[0].lineIds.Count : -1)
            + " routeLen=" + (mt.Length > 0 ? mt[0].route.Count : -1) + " cyclic=" + (mt.Length > 0 && mt[0].cyclic));
        pass &= multiOk;

        Debug.Log("ServiceTest: " + (pass ? "PASS" : "FAIL"));
        PlayerPrefs.DeleteKey("railtycoon_save");
        EditorApplication.Exit(pass ? 0 : 1);
    }

    static Station MakeStation(Vector3 pos, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(BuildController.WorldRoot, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.identity);
        var st = go.AddComponent<Station>();
        st.cars = 8; st.faces = 2; st.lines = 4; st.stationName = name;
        st.Build();
        TrackNetwork.stations.Add(st);
        return st;
    }

    static void Connect(Station a, Station b)
    {
        int bestSa = 1, bestSb = 1;
        float best = float.MaxValue;
        for (int sa = -1; sa <= 1; sa += 2)
            for (int sb = -1; sb <= 1; sb += 2)
            {
                float d = Vector3.Distance(a.End(sa), b.End(sb));
                if (d < best) { best = d; bestSa = sa; bestSb = sb; }
            }
        var seg = new TrackSegment { a = a, b = b, signA = bestSa, signB = bestSb };
        seg.Build(BuildController.WorldRoot);
        TrackNetwork.segments.Add(seg);
    }
}
