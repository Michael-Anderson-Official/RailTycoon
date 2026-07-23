using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 駅の建て替え・撤去のロジック検証(バッチ)。-executeMethod StationEditTest.Run
// 建て替えで面/線が変わっても列車が壊れないこと、撤去で接続線路と通過列車が
// 消え払い戻しが入ることを確認する
public static class StationEditTest
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        TrackNetwork.Clear();
        SaveLoad.suppress = true;        // 実セーブを汚さない
        GameState.money = 100e8;

        var bc = new GameObject("BC").AddComponent<BuildController>();

        var a = MakeStation(new Vector3(-600, 0, 0), 0, 8, 2, 4, "A");
        var b = MakeStation(new Vector3(400, 0, 120), 0, 8, 2, 4, "B");
        var c = MakeStation(new Vector3(1300, 0, -200), 0, 8, 1, 2, "C");
        Connect(a, b);
        Connect(b, c);

        // A→B→C を走る4両列車。Bで折り返す前提の往復
        int ta; a.TryReserve(out ta);
        var trGo = new GameObject("Train");
        trGo.transform.SetParent(BuildController.WorldRoot, false);
        var trInit = trGo.AddComponent<Train>();
        TrackNetwork.trains.Add(trInit); // RemoveStationの明示解除を意味あるものにするため登録(M2-B.1)
        trInit.Init(TrainCatalog.Formations[5],
            new List<Station> { a, b, c }, new List<int> { ta, b.StopTracks[0], c.StopTracks[0] });

        bool pass = true;

        // --- 建て替え: B を 2面4線 → 1面2線 に縮小 ---
        double moneyBefore = GameState.money;
        int bLinesBefore = b.PlatformCount;
        bc.RebuildStation(b, 10, 1, 2);   // 両数も8→10へ
        int bLinesAfter = b.PlatformCount;
        var trainsAfterRebuild = Object.FindObjectsByType<Train>(FindObjectsSortMode.None);
        bool rebuildOk = b.cars == 10 && bLinesAfter == 2 && trainsAfterRebuild.Length == 1;
        // 列車の番線が新レイアウトで有効か(Bの番線が範囲内)
        var tr = trainsAfterRebuild.Length > 0 ? trainsAfterRebuild[0] : null;
        bool tracksValid = tr != null;
        if (tr != null)
            for (int i = 0; i < tr.route.Count; i++)
            {
                int trk = tr.routeTracks[i];
                if (trk < 0 || trk >= tr.route[i].occupied.Length || tr.route[i].PlatformNumberOf(trk) <= 0)
                    tracksValid = false;
            }
        Debug.Log("StationEditTest: rebuild B lines " + bLinesBefore + "→" + bLinesAfter +
            " cars=" + b.cars + " trains=" + trainsAfterRebuild.Length +
            " tracksValid=" + tracksValid + " moneyΔ=" + ((GameState.money - moneyBefore) / 1e8).ToString("F2"));
        pass &= rebuildOk && tracksValid;

        // --- 撤去: C を撤去。B-C線路と、Cを通る列車が消える ---
        double moneyBeforeRemove = GameState.money;
        bc.RemoveStation(c);
        var stationsAfter = TrackNetwork.stations.Count;
        var segsAfter = TrackNetwork.segments.Count;
        var trainsAfterRemove = Object.FindObjectsByType<Train>(FindObjectsSortMode.None);
        double refund = GameState.money - moneyBeforeRemove;
        // RemoveStation経由でもTrackNetwork.trainsから正しく解除されていること
        // (M2-B.1でOnEnable自己登録から明示登録へ変更した箇所の回帰検出)
        bool removeOk = stationsAfter == 2 && segsAfter == 1 && trainsAfterRemove.Length == 0 && refund > 0
            && TrackNetwork.trains.Count == 0;
        Debug.Log("StationEditTest: remove C stations=" + stationsAfter + " segments=" + segsAfter +
            " trains=" + trainsAfterRemove.Length + " refund=" + (refund / 1e8).ToString("F2") + "億円"
            + " registryCount=" + TrackNetwork.trains.Count);
        pass &= removeOk;

        // --- 撤去後、列車が予約していた駅Aの線が解放されているか(予約漏れ検査) ---
        int freeAtA = 0;
        foreach (int i in a.StopTracks) if (!a.occupied[i]) freeAtA++;
        bool noLeak = freeAtA == a.PlatformCount;   // 列車が消えたので全線空き
        Debug.Log("StationEditTest: A free platforms " + freeAtA + "/" + a.PlatformCount + " noLeak=" + noLeak);
        pass &= noLeak;

        Debug.Log("StationEditTest: " + (pass ? "PASS" : "FAIL"));
        EditorApplication.Exit(pass ? 0 : 1);
    }

    static Station MakeStation(Vector3 pos, float yaw, int cars, int faces, int lines, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(BuildController.WorldRoot, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        var st = go.AddComponent<Station>();
        st.cars = cars; st.faces = faces; st.lines = lines; st.stationName = name;
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
