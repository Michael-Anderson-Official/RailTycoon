using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// セーブ→全消去→ロードの往復テスト(バッチ)。駅・線路・列車・資金・待ち客が戻るか確認
public static class SaveTest
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        TrackNetwork.Clear();
        SaveLoad.suppress = false;

        var a = MakeStation(new Vector3(-500, 0, 0), 10, 10, 2, 4, "駅1");
        var b = MakeStation(new Vector3(600, 0, 200), -30, 6, 1, 2, "駅2");
        TrackNetwork.nameCounter = 2;
        var seg = new TrackSegment { id = ++TrackNetwork.segmentIdCounter, a = a, b = b, signA = 1, signB = -1 };
        seg.Build(BuildController.WorldRoot);
        TrackNetwork.segments.Add(seg);
        a.ForceDev(2.5f);
        a.waiting[b] = 42;
        GameState.money = 55.5e8;
        GameState.carried = 1234;

        int track;
        a.TryReserve(out track);
        var trGo = new GameObject("Train");
        trGo.transform.SetParent(BuildController.WorldRoot, false);
        var tr = trGo.AddComponent<Train>();
        tr.id = ++TrackNetwork.trainIdCounter; // M2-C: id=0の列車はSaveLoad.Saveから除外されるため必須
        TrackNetwork.trains.Add(tr); // SaveLoad.SaveはTrackNetwork.trainsを列挙するため明示登録が必要(M2-B.1)
        tr.Init(TrainCatalog.Formations[0], new List<Station> { a, b },
            new List<int> { track, b.StopTracks[0] });

        SaveLoad.Save();

        // 全消去
        Object.DestroyImmediate(BuildController.WorldRoot.gameObject);
        TrackNetwork.Clear();
        GameState.money = 0;
        GameState.carried = 0;

        bool ok = SaveLoad.Load();
        var trains = Object.FindObjectsByType<Train>(FindObjectsSortMode.None);
        Debug.Log("SaveTest: loaded=" + ok +
            " stations=" + TrackNetwork.stations.Count +
            " segments=" + TrackNetwork.segments.Count +
            " trains=" + trains.Length +
            " money=" + (GameState.money / 1e8).ToString("F1") +
            " carried=" + GameState.carried +
            " nameCounter=" + TrackNetwork.nameCounter);
        if (TrackNetwork.stations.Count == 2)
        {
            var s0 = TrackNetwork.stations[0];
            int wait = s0.TotalWaiting;
            Debug.Log("SaveTest: st0 name=" + s0.stationName + " cars=" + s0.cars + " faces=" + s0.faces +
                " lines=" + s0.lines + " dev=" + s0.dev.ToString("F1") + " waiting=" + wait +
                " yaw=" + s0.transform.eulerAngles.y.ToString("F0"));
        }
        bool pass = ok && TrackNetwork.stations.Count == 2 && TrackNetwork.segments.Count == 1 &&
            trains.Length == 1 && System.Math.Abs(GameState.money - 55.5e8) < 1;
        Debug.Log("SaveTest: " + (pass ? "PASS" : "FAIL"));
        PlayerPrefs.DeleteKey("railtycoon_save"); // テスト後始末
        EditorApplication.Exit(pass ? 0 : 1);
    }

    static Station MakeStation(Vector3 pos, float yaw, int cars, int faces, int lines, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(BuildController.WorldRoot, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        var st = go.AddComponent<Station>();
        st.id = ++TrackNetwork.stationIdCounter; // M2-C: id=0の駅はSaveLoad.Saveから除外されるため必須
        st.cars = cars;
        st.faces = faces;
        st.lines = lines;
        st.stationName = name;
        st.Build();
        TrackNetwork.stations.Add(st);
        return st;
    }
}
