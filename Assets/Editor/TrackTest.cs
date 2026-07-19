using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 線路敷設フローの自動テスト。実際のHandleTap(レイキャスト→駅選択→敷設)を
// そのまま呼んで、セグメント生成と費用控除を検証する
public static class TrackTest
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        TrackNetwork.Clear();
        var bcGo = new GameObject("BC");
        var bc = bcGo.AddComponent<BuildController>();
        BuildController.Instance = bc; // EditモードはAwakeが呼ばれない

        var a = MakeStation(new Vector3(-400, 0, 0), 0, 6, 2, 2, "A");
        var b = MakeStation(new Vector3(500, 0, 120), 30, 6, 2, 2, "B");
        Physics.SyncTransforms();

        double m0 = GameState.money;
        bc.SetMode(BuildController.Mode.Track);
        bc.HandleTap(RayAt(a.transform.position));
        bc.HandleTap(RayAt(b.transform.position));
        Debug.Log("TrackTest: A-B segments=" + TrackNetwork.segments.Count +
            " spent=" + ((m0 - GameState.money) / 1e8).ToString("F2") + "億");
        if (TrackNetwork.segments.Count > 0)
        {
            var seg = TrackNetwork.segments[0];
            Debug.Log("TrackTest: seg len=" + seg.length.ToString("F0") +
                " goExists=" + (seg.go != null) +
                " meshChildren=" + (seg.go != null ? seg.go.transform.childCount : -1));
        }

        // 近接ペア(スロート端同士が50m未満)の再現
        var c = MakeStation(new Vector3(-400, 0, 260), 0, 6, 2, 2, "C");
        Physics.SyncTransforms();
        bc.HandleTap(RayAt(a.transform.position));
        bc.HandleTap(RayAt(c.transform.position));
        Debug.Log("TrackTest: after A-C(close) segments=" + TrackNetwork.segments.Count);

        // 地面タップ(駅なし)の挙動
        bc.HandleTap(RayAt(new Vector3(1500, 0, 1500)));
        Debug.Log("TrackTest: done");
        EditorApplication.Exit(0);
    }

    static Ray RayAt(Vector3 p) => new Ray(p + Vector3.up * 300f, Vector3.down);

    static Station MakeStation(Vector3 pos, float yaw, int cars, int faces, int lines, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(BuildController.WorldRoot, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        var st = go.AddComponent<Station>();
        st.cars = cars;
        st.faces = faces;
        st.lines = lines;
        st.stationName = name;
        st.Build();
        TrackNetwork.stations.Add(st);
        return st;
    }
}
