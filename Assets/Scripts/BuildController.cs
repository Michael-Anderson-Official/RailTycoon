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

    // 列車モードのサブ状態: 系統一覧/系統作成中/配車
    public enum TrainSub { Manage, CreateLine, Dispatch }
    public TrainSub trainSub = TrainSub.Manage;
    public int newLineType = 3;      // 作成中の系統の種別(既定=普通)
    public readonly List<ServiceLine> selLines = new List<ServiceLine>(); // 配車で組む運用(順に走る)

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
        trainSub = TrainSub.Manage;
        selLines.Clear();
        mode = m;
        if (UIController.I != null) UIController.I.OnModeChanged();
        if (m == Mode.Track) UIController.Toast("つなぎたい駅を2つ、順にタップ");
        else if (m == Mode.Station) UIController.Toast("地面をタップして位置を選び、「ここに建設」で確定");
        else if (m == Mode.Train) UIController.Toast("運行系統を作るか、系統に列車を配置しましょう");
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
                if (trainSub == TrainSub.CreateLine && tapped != null) TapRouteStation(tapped);
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

        // 系統の番線も新レイアウトへ整合(無効になった番線は停車線へ)
        foreach (var l in Services.lines)
            for (int i = 0; i < l.route.Count; i++)
                if (l.route[i] == st)
                {
                    int trk = l.tracks[i];
                    if (trk < 0 || trk >= st.occupied.Length || st.PlatformNumberOf(trk) <= 0)
                        l.tracks[i] = st.StopTracks[0];
                }

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
            TrackNetwork.trains.Remove(t);
            DestroySafe(t.gameObject);
        }
        var neighbors = new List<Station>();
        for (int i = TrackNetwork.segments.Count - 1; i >= 0; i--)
        {
            var seg = TrackNetwork.segments[i];
            if (seg.a != st && seg.b != st) continue;
            var other = seg.Other(st);
            if (other != null && other != st && !neighbors.Contains(other)) neighbors.Add(other);
            refund += seg.length * GameState.TrackCostPerM * 0.5;
            if (seg.go != null) DestroySafe(seg.go);
            TrackNetwork.segments.RemoveAt(i);
        }
        foreach (var nb in neighbors) nb.RebuildTrackVisual();   // 端が空いたので頭端(車止め)に戻す
        // この駅を含む運行系統は成立しないので廃止(列車は上でRouteHasにより撤去済み)
        int removedLines = Services.lines.RemoveAll(l => l.route.Contains(st));
        selLines.RemoveAll(l => !Services.lines.Contains(l));
        TrackNetwork.stations.Remove(st);
        DestroySafe(st.gameObject);
        GameState.Refund(refund);
        TrackNetwork.MarkDirty();
        SaveLoad.Save();
        if (UIController.I != null) UIController.I.RefreshTrainPanel();
        UIController.Toast(st.stationName + "を撤去(払戻 " + (refund / 1e8).ToString("F1") + "億円"
            + (removedTrains > 0 ? "・列車" + removedTrains + "本撤去" : "")
            + (removedLines > 0 ? "・系統" + removedLines + "本廃止" : "") + ")");
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
        a.RebuildTrackVisual();     // 接続した端を貫通(車止め除去)に
        st.RebuildTrackVisual();
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

    // 系統作成中に停車駅をタップ。番線選択待ちにする
    void TapRouteStation(Station st)
    {
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
        // 駅を選んだら番線選択待ちにする。UIが番線ボタンを、駅側が3D番線ラベルを出す
        if (pendingStation != null && pendingStation != st) pendingStation.HidePlatformNumbers();
        pendingStation = st;
        st.ShowPlatformNumbers();
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
        st.HidePlatformNumbers();
        pendingStation = null;
        if (UIController.I != null)
        {
            UIController.I.HidePlatformPicker();
            UIController.I.UpdateRouteLabel();
        }
        UIController.Toast(st.stationName + " " + platformNo + "番線を経路に追加");
    }

    // ---- 運行系統 ----

    public void GoManageTab()
    {
        if (trainSub == TrainSub.CreateLine) ClearRoute();
        trainSub = TrainSub.Manage;
        if (UIController.I != null) UIController.I.RefreshTrainPanel();
    }

    // --- 配車の運用(複数系統)を組み立てる操作 ---
    public void AddToItinerary(ServiceLine l)
    {
        if (l != null) selLines.Add(l);
        if (UIController.I != null) UIController.I.RefreshTrainPanel();
    }

    public void RemoveFromItinerary(int i)
    {
        if (i >= 0 && i < selLines.Count) selLines.RemoveAt(i);
        if (UIController.I != null) UIController.I.RefreshTrainPanel();
    }

    public void MoveItinerary(int i, int delta)
    {
        int j = i + delta;
        if (i < 0 || i >= selLines.Count || j < 0 || j >= selLines.Count) return;
        var tmp = selLines[i]; selLines[i] = selLines[j]; selLines[j] = tmp;
        if (UIController.I != null) UIController.I.RefreshTrainPanel();
    }

    public void GoDispatchTab()
    {
        if (trainSub == TrainSub.CreateLine) ClearRoute();
        trainSub = TrainSub.Dispatch;
        if (UIController.I != null) UIController.I.RefreshTrainPanel();
    }

    public void BeginCreateLine()
    {
        ClearRoute();
        trainSub = TrainSub.CreateLine;
        if (UIController.I != null) UIController.I.RefreshTrainPanel();
        UIController.Toast("種別を選び、停車駅を順にタップ→番線を選ぶ→「系統を保存」");
    }

    public void CancelCreateLine()
    {
        ClearRoute();
        trainSub = TrainSub.Manage;
        if (UIController.I != null) UIController.I.RefreshTrainPanel();
    }

    public void SetNewLineType(int typeIdx)
    {
        newLineType = ServiceType.Clamp(typeIdx);
        if (UIController.I != null) UIController.I.RefreshTrainPanel();
    }

    // 現在の経路(routeSel/routeTrackSel)から運行系統を作成
    public void SaveNewLine()
    {
        if (routeSel.Count < 2)
        {
            UIController.Toast("停車駅を2つ以上選んでください");
            return;
        }
        var line = new ServiceLine
        {
            id = ++Services.idCounter,
            typeIdx = newLineType,
            route = new List<Station>(routeSel),
            tracks = new List<int>(routeTrackSel),
        };
        Services.lines.Add(line);
        ClearRoute();
        trainSub = TrainSub.Manage;
        SaveLoad.Save();
        if (UIController.I != null) UIController.I.RefreshTrainPanel();
        UIController.Toast(line.DisplayName + " を作成しました");
    }

    // 系統を廃止。この系統を運用に含む列車も撤去し半額払い戻し
    public void DeleteLine(ServiceLine line)
    {
        if (line == null) return;
        double refund = 0; int n = 0;
        foreach (var t in FindObjectsByType<Train>(FindObjectsSortMode.None))
        {
            if (t.lineIds == null || !t.lineIds.Contains(line.id)) continue;
            refund += t.RefundValue; n++;
            t.ReleaseAll();
            TrackNetwork.trains.Remove(t);
            DestroySafe(t.gameObject);
        }
        Services.lines.Remove(line);
        selLines.RemoveAll(l => l == line);
        GameState.Refund(refund);
        SaveLoad.Save();
        if (UIController.I != null) UIController.I.RefreshTrainPanel();
        UIController.Toast(line.DisplayName + " を廃止"
            + (n > 0 ? "(列車" + n + "本撤去・払戻" + (refund / 1e8).ToString("F1") + "億円)" : ""));
    }

    // 運用(selLines)を1本の経路に連結。連続する重複駅と、先頭==末尾の折り返し駅をマージ
    public static void BuildItinerary(List<ServiceLine> lines,
        out List<Station> route, out List<int> tracks, out List<int> lineIds)
    {
        route = new List<Station>();
        tracks = new List<int>();
        lineIds = new List<int>();
        foreach (var l in lines)
        {
            lineIds.Add(l.id);
            for (int i = 0; i < l.route.Count; i++)
            {
                if (route.Count > 0 && route[route.Count - 1] == l.route[i]) continue;
                route.Add(l.route[i]);
                tracks.Add(l.tracks[i]);
            }
        }
        while (route.Count >= 2 && route[0] == route[route.Count - 1])
        {
            route.RemoveAt(route.Count - 1);
            tracks.RemoveAt(tracks.Count - 1);
        }
    }

    // 組んだ運用に編成を1本配属して購入。経路上で最初に空く番線に投入
    public void DispatchTrain()
    {
        if (selFormation == null) { UIController.Toast("編成を選んでください"); return; }
        if (selLines.Count == 0) { UIController.Toast("運用に系統を1つ以上追加してください"); return; }
        BuildItinerary(selLines, out var route, out var tracks, out var lineIds);
        if (route.Count < 2) { UIController.Toast("停車駅が足りません"); return; }
        foreach (var s in route)
            if (s.cars < selFormation.cars)
            {
                UIController.Toast(s.stationName + "は" + s.cars + "両対応で" + selFormation.cars + "両は停まれません");
                return;
            }
        int startIdx = -1, startTrack = -1;
        for (int i = 0; i < route.Count; i++)
            if (route[i].TryReserveSpecific(tracks[i])) { startIdx = i; startTrack = tracks[i]; break; }
        if (startIdx < 0) { UIController.Toast("経路上に空いている番線がありません(先行列車を動かしてから)"); return; }
        if (!GameState.Spend(selFormation.CostYen))
        {
            route[startIdx].Release(startTrack);
            UIController.Toast("資金不足(" + (selFormation.CostYen / 1e8).ToString("F1") + "億円必要)");
            return;
        }
        var go = new GameObject("Train_" + selFormation.Label);
        go.transform.SetParent(WorldRoot, false);
        var t = go.AddComponent<Train>();
        TrackNetwork.trains.Add(t);
        t.Init(selFormation, route, tracks, startIdx, 1);
        t.lineIds = lineIds;
        SaveLoad.Save();
        if (UIController.I != null) UIController.I.RefreshTrainPanel();
        string names = "";
        foreach (var l in selLines) names += (names.Length > 0 ? "→" : "") + l.TypeName;
        UIController.Toast(selFormation.Label + " を配置(運用: " + names + ")");
    }

    public void ClearRoute()
    {
        routeSel.Clear();
        routeTrackSel.Clear();
        if (pendingStation != null) pendingStation.HidePlatformNumbers();
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
