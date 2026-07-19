using System.Collections.Generic;
using UnityEngine;

// 建設・選択のモード管理とタップ処理
public class BuildController : MonoBehaviour
{
    public static BuildController Instance;

    public enum Mode { View, Track, Station, Train }
    public Mode mode = Mode.View;

    // 駅建設パラメータ
    public int pCars = 6, pFaces = 2, pLines = 2;
    public float pYaw;
    public Station previewStation;
    public Station rebuildTarget;   // 建て替え対象(非nullなら駅モードは建て替え動作)

    Station trackFirst;
    GameObject trackMarker;

    public TrainCatalog.Formation selFormation;
    public readonly List<Station> routeSel = new List<Station>();
    public readonly List<int> routeTrackSel = new List<int>();
    public Station pendingStation;   // 番線選択待ちの駅
    readonly List<GameObject> routeMarkers = new List<GameObject>();

    static Transform worldRoot;

    public static Transform WorldRoot
    {
        get
        {
            if (worldRoot == null) worldRoot = new GameObject("World").transform;
            return worldRoot;
        }
    }

    void Awake() => Instance = this;

    public void SetMode(Mode m)
    {
        if (rebuildTarget != null) { rebuildTarget.SetRenderersVisible(true); rebuildTarget = null; }
        if (previewStation != null) { Destroy(previewStation.gameObject); previewStation = null; }
        ClearTrackSel();
        ClearRoute();
        mode = m;
        if (UIController.I != null) UIController.I.OnModeChanged();
        if (m == Mode.Track) UIController.Toast("つなぎたい駅を2つ、順にタップ");
        else if (m == Mode.Station) UIController.Toast("地面をタップして位置を選び、「ここに建設」で確定");
        else if (m == Mode.Train) UIController.Toast("編成を選んでから、停車駅を順にタップ→「発車!」");
    }

    public void HandleTap(Ray ray)
    {
        Station tapped = null;
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, 20000f))
            tapped = hit.collider.GetComponentInParent<Station>();
        if (tapped != null && tapped.preview) tapped = null;

        Vector3 ground = Vector3.zero;
        bool hasGround = false;
        if (Mathf.Abs(ray.direction.y) > 1e-4f)
        {
            float t = -ray.origin.y / ray.direction.y;
            if (t > 0) { ground = ray.origin + ray.direction * t; hasGround = true; }
        }

        switch (mode)
        {
            case Mode.View:
                if (UIController.I != null)
                {
                    if (tapped != null) UIController.I.ShowStationInfo(tapped);
                    else UIController.I.HideStationInfo();
                }
                break;
            case Mode.Station:
                // 建て替え中はプレビューを駅位置に固定(地面タップで動かさない)
                if (hasGround && tapped == null && rebuildTarget == null) MovePreview(ground);
                break;
            case Mode.Track:
                if (tapped != null) TapTrackStation(tapped);
                else if (trackFirst != null)
                {
                    ClearTrackSel();
                    UIController.Toast("選択を解除しました");
                }
                else if (TrackNetwork.stations.Count < 2)
                    UIController.Toast("先に「駅」モードで駅を2つ建ててください(「ここに建設」で確定)");
                else
                    UIController.Toast("駅をタップしてください(ズームすると狙いやすい)");
                break;
            case Mode.Train:
                if (tapped != null) TapRouteStation(tapped);
                break;
        }
    }

    // ---- 駅建設 ----

    void MovePreview(Vector3 pos)
    {
        pos = new Vector3(Mathf.Round(pos.x / 5f) * 5f, 0, Mathf.Round(pos.z / 5f) * 5f);
        if (previewStation == null)
        {
            var go = new GameObject("StationPreview");
            go.transform.SetParent(WorldRoot, false);
            previewStation = go.AddComponent<Station>();
            previewStation.preview = true;
            previewStation.stationName = "(建設予定)";
        }
        previewStation.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, pYaw, 0));
        ApplyPreviewParams();
    }

    public void ApplyPreviewParams()
    {
        if (previewStation == null) return;
        previewStation.cars = pCars;
        previewStation.faces = pFaces;
        previewStation.lines = pLines;
        previewStation.transform.rotation = Quaternion.Euler(0, pYaw, 0);
        previewStation.Build();
    }

    public void ConfirmStation()
    {
        if (rebuildTarget != null) { ConfirmRebuild(); return; }
        if (previewStation == null)
        {
            UIController.Toast("先に地面をタップして位置を選んでください");
            return;
        }
        double cost = GameState.StationCost(pCars, pFaces, pLines);
        if (!GameState.Spend(cost))
        {
            UIController.Toast("資金不足(" + (cost / 1e8).ToString("F1") + "億円必要)");
            return;
        }
        var st = previewStation;
        previewStation = null;
        st.preview = false;
        st.stationName = "駅" + (++TrackNetwork.nameCounter);
        st.gameObject.name = st.stationName;
        st.UpdateLabel();
        TrackNetwork.stations.Add(st);
        TrackNetwork.MarkDirty();
        st.ForceDev(0f); // 駅前に初期集落を発生させる
        SaveLoad.Save();
        UIController.Toast(st.stationName + "を建設(" + (cost / 1e8).ToString("F1") + "億円)");
    }

    // ---- 駅の建て替え・撤去 ----

    // 情報パネルから呼ぶ。既存駅を建て替えモードに入れ、駅パラメータをコピーして
    // 実駅にプレビューを重ねる
    public void BeginRebuild(Station st)
    {
        SetMode(Mode.Station);            // 既存プレビュー/選択をクリア
        rebuildTarget = st;
        pCars = st.cars; pFaces = st.faces; pLines = st.lines;
        pYaw = st.transform.eulerAngles.y;
        st.SetRenderersVisible(false);    // 実駅を隠してプレビューを重ねる
        MovePreview(st.transform.position);
        if (UIController.I != null) UIController.I.OnModeChanged();
        UIController.Toast(st.stationName + "を建て替え中。両数/面/線を変えて「建て替え確定」");
    }

    void ConfirmRebuild()
    {
        var st = rebuildTarget;
        if (st == null) return;
        if (!RebuildStation(st, pCars, pFaces, pLines)) return; // 資金不足時は建て替えモード継続
        rebuildTarget = null;
        if (previewStation != null) { Destroy(previewStation.gameObject); previewStation = null; }
        SetMode(Mode.View);
        if (UIController.I != null) UIController.I.ShowStationInfo(st);
    }

    // 駅パラメータを変更してメッシュ・接続線路を作り直し、通過列車を再同期する
    public bool RebuildStation(Station st, int cars, int faces, int lines)
    {
        double oldCost = GameState.StationCost(st.cars, st.faces, st.lines);
        double newCost = GameState.StationCost(cars, faces, lines);
        double delta = newCost - oldCost;
        if (delta > 0 && !GameState.Spend(delta))
        {
            UIController.Toast("資金不足(差額" + (delta / 1e8).ToString("F1") + "億円必要)");
            return false;
        }
        if (delta < 0) GameState.Refund(-delta * 0.5); // 縮小は差額の半分を払い戻し

        st.cars = cars; st.faces = faces; st.lines = lines;
        st.Build();                         // メッシュ・レイアウト・occupied再生成
        st.SetRenderersVisible(true);

        // 接続する線路は駅端(End)が動くので作り直す
        foreach (var seg in TrackNetwork.segments)
            if (seg.a == st || seg.b == st) seg.Build(WorldRoot);

        // この駅を通る列車を現在駅に復帰(予約取り直し・番線整合)
        foreach (var t in FindObjectsByType<Train>(FindObjectsSortMode.None))
            if (t.RouteHas(st)) t.ResyncToNetwork();

        TrackNetwork.MarkDirty();
        SaveLoad.Save();
        UIController.Toast(st.stationName + "を建て替え(" + cars + "両" + faces + "面" + lines + "線)");
        return true;
    }

    // 駅を撤去。接続線路と通過列車も消し、半額を払い戻す
    public void RemoveStation(Station st)
    {
        double refund = GameState.StationCost(st.cars, st.faces, st.lines) * 0.5;
        int removedTrains = 0;
        foreach (var t in FindObjectsByType<Train>(FindObjectsSortMode.None))
        {
            if (!t.RouteHas(st)) continue;
            refund += t.RefundValue;
            removedTrains++;
            t.ReleaseAll();               // 隣駅などに残る予約を解放してから破棄
            DestroySafe(t.gameObject);
        }
        for (int i = TrackNetwork.segments.Count - 1; i >= 0; i--)
        {
            var seg = TrackNetwork.segments[i];
            if (seg.a != st && seg.b != st) continue;
            refund += seg.length * GameState.TrackCostPerM * 0.5;
            if (seg.go != null) DestroySafe(seg.go);
            TrackNetwork.segments.RemoveAt(i);
        }
        TrackNetwork.stations.Remove(st);
        DestroySafe(st.gameObject);
        GameState.Refund(refund);
        TrackNetwork.MarkDirty();
        SaveLoad.Save();
        UIController.Toast(st.stationName + "を撤去(払戻 " + (refund / 1e8).ToString("F1") + "億円"
            + (removedTrains > 0 ? "・列車" + removedTrains + "本撤去" : "") + ")");
    }

    // ---- 線路 ----

    void TapTrackStation(Station st)
    {
        if (trackFirst == null)
        {
            trackFirst = st;
            trackMarker = MakeMarker(st.transform.position, 30f, new Color(1f, 0.85f, 0.2f, 0.5f));
            UIController.Toast(st.stationName + "を選択。接続先の駅をタップ");
            return;
        }
        if (trackFirst == st) { ClearTrackSel(); return; }
        var a = trackFirst;
        ClearTrackSel();
        if (TrackNetwork.Connected(a, st))
        {
            UIController.Toast("すでに接続されています");
            return;
        }
        int bestSa = 1, bestSb = 1;
        float best = float.MaxValue;
        for (int sa = -1; sa <= 1; sa += 2)
            for (int sb = -1; sb <= 1; sb += 2)
            {
                float d = Vector3.Distance(a.End(sa), st.End(sb));
                if (d < best) { best = d; bestSa = sa; bestSb = sb; }
            }
        if (best < 12f)
        {
            UIController.Toast("駅同士が近すぎて接続できません(駅を少し離して建ててください)");
            return;
        }
        double cost = best * GameState.TrackCostPerM;
        if (!GameState.Spend(cost))
        {
            UIController.Toast("資金不足(" + (cost / 1e8).ToString("F1") + "億円必要)");
            return;
        }
        var seg = new TrackSegment { a = a, b = st, signA = bestSa, signB = bestSb };
        seg.Build(WorldRoot);
        TrackNetwork.segments.Add(seg);
        TrackNetwork.MarkDirty();
        SaveLoad.Save();
        UIController.Toast(a.stationName + "〜" + st.stationName + " 線路敷設(" + (cost / 1e8).ToString("F1") + "億円)");
    }

    void ClearTrackSel()
    {
        trackFirst = null;
        if (trackMarker != null) { Destroy(trackMarker); trackMarker = null; }
    }

    // ---- 列車 ----

    void TapRouteStation(Station st)
    {
        if (selFormation == null)
        {
            UIController.Toast("先に編成を選んでください");
            return;
        }
        if (st.cars < selFormation.cars)
        {
            UIController.Toast(st.stationName + "は" + st.cars + "両対応。" + selFormation.cars + "両は停車できません");
            return;
        }
        if (routeSel.Count > 0)
        {
            var last = routeSel[routeSel.Count - 1];
            if (last == st) return;
            if (routeSel.Contains(st))
            {
                UIController.Toast("すでに経路に含まれています");
                return;
            }
            if (!TrackNetwork.Connected(last, st))
            {
                UIController.Toast(last.stationName + "と直結する線路がありません");
                return;
            }
        }
        // 駅を選んだら番線選択待ちにする。UIが番線ボタンを出す
        pendingStation = st;
        if (UIController.I != null) UIController.I.ShowPlatformPicker(st);
        UIController.Toast(st.stationName + "の番線を選んでください(全" + st.PlatformCount + "番線)");
    }

    // UIの番線ボタンから呼ばれる。番線を確定して経路に追加
    public void AddRouteStop(int platformNo)
    {
        if (pendingStation == null) return;
        var st = pendingStation;
        int track = st.TrackOfPlatform(platformNo);
        routeSel.Add(st);
        routeTrackSel.Add(track);
        routeMarkers.Add(MakeMarker(st.transform.position, 26f, new Color(0.2f, 0.8f, 1f, 0.5f)));
        pendingStation = null;
        if (UIController.I != null)
        {
            UIController.I.HidePlatformPicker();
            UIController.I.UpdateRouteLabel();
        }
        UIController.Toast(st.stationName + " " + platformNo + "番線を経路に追加");
    }

    public void LaunchTrain()
    {
        if (selFormation == null || routeSel.Count < 2)
        {
            UIController.Toast("編成を選び、駅を2つ以上タップしてください");
            return;
        }
        int track = routeTrackSel[0];
        if (!routeSel[0].TryReserveSpecific(track))
        {
            UIController.Toast(routeSel[0].stationName + " " + routeSel[0].PlatformNumberOf(track) + "番線が塞がっています");
            return;
        }
        if (!GameState.Spend(selFormation.CostYen))
        {
            routeSel[0].Release(track);
            UIController.Toast("資金不足(" + (selFormation.CostYen / 1e8).ToString("F1") + "億円必要)");
            return;
        }
        var go = new GameObject("Train_" + selFormation.Label);
        go.transform.SetParent(WorldRoot, false);
        go.AddComponent<Train>().Init(selFormation, new List<Station>(routeSel), new List<int>(routeTrackSel));
        SaveLoad.Save();
        UIController.Toast(selFormation.Label + "が営業開始!");
        ClearRoute();
        if (UIController.I != null) UIController.I.UpdateRouteLabel();
    }

    public void ClearRoute()
    {
        routeSel.Clear();
        routeTrackSel.Clear();
        pendingStation = null;
        foreach (var m in routeMarkers) if (m != null) Destroy(m);
        routeMarkers.Clear();
        if (UIController.I != null)
        {
            UIController.I.HidePlatformPicker();
            UIController.I.UpdateRouteLabel();
        }
    }

    // ---- 共通 ----

    // 再生中はDestroy、エディタ(バッチテスト)ではDestroyImmediate
    static void DestroySafe(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }

    static GameObject MakeMarker(Vector3 pos, float radius, Color c)
    {
        var md = new RailKit.MeshData();
        const int N = 28;
        int b = md.v.Count;
        md.v.Add(Vector3.zero);
        for (int i = 0; i <= N; i++)
        {
            float a = i / (float)N * Mathf.PI * 2f;
            md.v.Add(new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius));
        }
        for (int i = 1; i <= N; i++)
        {
            md.t.Add(b);
            md.t.Add(b + i + 1);
            md.t.Add(b + i);
        }
        var go = RailKit.MeshGO("Marker", md.ToMesh(), MatLib.Tinted("Marker", c), WorldRoot);
        go.transform.position = pos + Vector3.up * 1.6f;
        return go;
    }
}
