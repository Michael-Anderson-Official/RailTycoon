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

    // M2-D: 面・線プリセット。「今回、完全自由配置エディターまで作る必要はない。
    // 汎用データモデル+プリセット方式でよい」との方針により、+/-ステッパーではなく
    // 代表的な構成をボタンで選ぶUIにする(内部データはcars/faces/linesのまま)
    public struct StationPreset { public string label; public int faces, lines; }
    public static readonly StationPreset[] StationPresets =
    {
        new StationPreset { label = "1面1線", faces = 1, lines = 1 },
        new StationPreset { label = "1面2線(島式)", faces = 1, lines = 2 },
        new StationPreset { label = "2面2線(相対式)", faces = 2, lines = 2 },
        new StationPreset { label = "2面3線", faces = 2, lines = 3 },
        new StationPreset { label = "2面4線", faces = 2, lines = 4 },
        new StationPreset { label = "3面2線", faces = 3, lines = 2 },
    };

    public void ApplyStationPreset(int presetIndex)
    {
        if (presetIndex < 0 || presetIndex >= StationPresets.Length) return;
        pFaces = StationPresets[presetIndex].faces;
        pLines = StationPresets[presetIndex].lines;
        ApplyPreviewParams();
    }

    Station trackFirst;
    GameObject trackMarker;
    public TrackBedType pTrackBedType = TrackBedType.Ballast;
    public Station TrackFirst => trackFirst;

    public static string TrackBedLabel(TrackBedType type)
        => type == TrackBedType.Slab ? "スラブ軌道" : "バラスト軌道";

    public void SetTrackBedType(TrackBedType type)
    {
        pTrackBedType = type;
        if (UIController.I != null) UIController.I.RefreshTrackBedButtons();
        UIController.Toast(TrackBedLabel(type) + "を選択");
    }

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

    // M2-C: SaveLoadがロードを一括コミットする際に使う。既存のWorldRootを破棄し、
    // 事前に(WorldRootの外で)組み立てた駅・線路・列車一式をまとめて差し替える。
    // 検証途中で失敗した場合はSaveLoad側が新root自体を破棄するため、ここが
    // 呼ばれるのは「全て成功した」ことが確定した後だけ
    internal static void ReplaceWorldRoot(Transform newRoot)
    {
        if (worldRoot != null) Object.DestroyImmediate(worldRoot.gameObject);
        newRoot.gameObject.name = "World";
        worldRoot = newRoot;
        // 旧WorldRoot配下の駅・線路・列車を指していた可能性のある、進行中の
        // 建設・経路選択・配車操作の状態を全てセッション初期状態へ戻す
        // (実装後レビューでCodex CLIが指摘。現状の製品コードではLoad()はBootstrap起動時
        // のみ呼ばれるため実際には空だが、ReplaceWorldRootを汎用APIとして安全にするため)
        if (Instance != null)
        {
            Instance.previewStation = null;
            Instance.rebuildTarget = null;
            Instance.trackFirst = null;
            if (Instance.trackMarker != null) Object.DestroyImmediate(Instance.trackMarker);
            Instance.trackMarker = null;
            Instance.selLines.Clear();
            Instance.routeSel.Clear();
            Instance.routeTrackSel.Clear();
            Instance.pendingStation = null;
            foreach (var m in Instance.routeMarkers) if (m != null) Object.DestroyImmediate(m);
            Instance.routeMarkers.Clear();
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
        if (m == Mode.Track) UIController.Toast(TrackBedLabel(pTrackBedType) + "：つなぎたい駅を2つ、順にタップ(繋がっている2駅を選ぶと撤去)");
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
                    // 駅を外したタップでは選択を解除しない(ズームや視点調整の合間に外れた
                    // タップで最初からやり直しになるのを避けるため。取り消したい場合は
                    // 選択中の駅をもう一度タップする)
                    UIController.Toast(trackFirst.stationName + "を選択中。接続先の駅をタップ" +
                        "(ズームすると狙いやすい。取り消すには選択中の駅をもう一度タップ)");
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
        string blocked = DescribePlacementObstruction(previewStation, null);
        if (blocked != null) { UIController.Toast(blocked); return; }
        double cost = GameState.StationCost(pCars, pFaces, pLines);
        if (!GameState.Spend(cost))
        {
            UIController.Toast("資金不足(" + (cost / 1e8).ToString("F1") + "億円必要)");
            return;
        }
        var st = previewStation;
        previewStation = null;
        st.preview = false;
        st.id = ++TrackNetwork.stationIdCounter;
        st.stationName = "駅" + (++TrackNetwork.nameCounter);
        st.gameObject.name = st.stationName;
        st.UpdateLabel();
        TrackNetwork.stations.Add(st);
        TrackNetwork.MarkDirty();
        st.ForceDev(0f); // 駅前に初期集落を発生させる
        SaveLoad.Save();
        UIController.Toast(st.stationName + "を建設(" + (cost / 1e8).ToString("F1") + "億円)");
    }

    // 駅を建てる/建て替える位置が、既存の駅や既設の線路と衝突していないか。
    // 衝突していれば理由の文言、問題なければnullを返す。
    // ignoreStation は建て替え対象(自分自身と、自分へ繋がる線路は除外する)
    public static string DescribePlacementObstruction(Station candidate, Station ignoreStation)
    {
        if (candidate == null || candidate.layout.trackOffsets == null) return null;

        // 駅どうしの重なり。構内(番線・ホーム)が触れるほど近いと線路も引けないので弾く
        foreach (var other in TrackNetwork.stations)
        {
            if (other == null || other == candidate || other == ignoreStation || other.preview) continue;
            if (Station.FootprintsOverlap(candidate, other, StationClearance))
                return other.stationName + "と近すぎます(駅どうしが重なります)";
        }

        // 既設の線路との衝突。線路の道床が駅構内へ入り込む位置には建てられない
        foreach (var seg in TrackNetwork.segments)
        {
            if (seg == null || seg.a == null || seg.b == null) continue;
            if (ignoreStation != null && (seg.a == ignoreStation || seg.b == ignoreStation)) continue;
            var center = seg.CenterPoints();
            for (int i = 0; i + 1 < center.Count; i++)
            {
                const int sub = 4;
                for (int k = 0; k < sub; k++)
                {
                    var p = Vector3.Lerp(center[i], center[i + 1], k / (float)sub);
                    if (candidate.FootprintContains(p, TrackSegment.HalfCorridorWidth))
                        return seg.a.stationName + "〜" + seg.b.stationName + "の線路と重なります";
                }
            }
        }
        return null;
    }

    // 駅どうしを離す最小限の余裕(構内の外側にこれだけ空ける)
    public const float StationClearance = 8f;

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
        // 建て替えで大きくなる場合、隣の駅や既設線路へめり込むことがある
        // (自分自身と、自分に繋がる線路は当然重なるので除外する)
        string blocked = DescribePlacementObstruction(previewStation, st);
        if (blocked != null) { UIController.Toast(blocked); return; }
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

    // 線路を1区間だけ撤去する。撤去後にその線路を使えなくなる列車・系統も畳む
    // (どの駅が繋がらなくなるかは経路によるため、RemoveStationと同じく
    // 「成立しなくなった系統は廃止し、その系統の列車は払い戻して撤去」で揃える)
    public void RemoveSegment(TrackSegment seg)
    {
        if (seg == null) return;
        var a = seg.a;
        var b = seg.b;
        double refund = seg.length * GameState.TrackCostPerM * 0.5;

        if (seg.go != null) DestroySafe(seg.go);
        TrackNetwork.segments.Remove(seg);
        TrackNetwork.MarkDirty();

        // 撤去後の線路網で、経路の隣り合う停車駅がまだ到達可能かを見る
        // (通過駅を挟む系統もあるので、直結ではなくFindPathで判定する)
        bool StillRunnable(List<Station> route)
        {
            for (int i = 0; i + 1 < route.Count; i++)
                if (TrackNetwork.FindPath(route[i], route[i + 1]) == null) return false;
            return true;
        }
        // 巡回運転(cyclic)の末尾→先頭についてはここで追加の手当てをしない。
        // 経路の隣り合う停車駅が全て到達可能(=StillRunnable)なら、その経路を逆に
        // 辿れば末尾→先頭も必ず到達可能なので、閉じる区間の直結が無くなっても
        // TryDepartはFindPathで迂回経路を見つけて発車できる(通過駅対応済みのため)。
        // SaveLoadのcyclic検証も直結ではなく到達可能性で見ているので通る
        var deadLines = Services.lines.FindAll(l => !StillRunnable(l.route));
        int removedTrains = 0;
        foreach (var t in FindObjectsByType<Train>(FindObjectsSortMode.None))
        {
            if (t.route == null) continue;
            bool dead = !StillRunnable(t.route)
                || (t.lineIds != null && t.lineIds.Exists(id => deadLines.Exists(l => l.id == id)));
            if (!dead) continue;
            refund += t.RefundValue;
            removedTrains++;
            t.ReleaseAll();
            TrackNetwork.trains.Remove(t);
            DestroySafe(t.gameObject);
        }
        foreach (var l in deadLines) Services.lines.Remove(l);
        selLines.RemoveAll(l => !Services.lines.Contains(l));

        // 組み直すのは、撤去した線路を実際に掴んでいた列車だけにする。
        // 無関係な列車まで組み直すと走行中の位置・速度が失われてダイヤが乱れる
        foreach (var t in FindObjectsByType<Train>(FindObjectsSortMode.None))
            if (t.HoldsSegment(seg)) t.ResyncToNetwork();

        // 端が空いたので頭端(車止め)へ戻す
        if (a != null) a.RebuildTrackVisual();
        if (b != null) b.RebuildTrackVisual();

        GameState.Refund(refund);
        SaveLoad.Save();
        if (UIController.I != null) UIController.I.RefreshTrainPanel();
        UIController.Toast((a != null ? a.stationName : "?") + "〜" + (b != null ? b.stationName : "?")
            + "の線路を撤去(払戻 " + (refund / 1e8).ToString("F1") + "億円"
            + (removedTrains > 0 ? "・列車" + removedTrains + "本撤去" : "")
            + (deadLines.Count > 0 ? "・系統" + deadLines.Count + "本廃止" : "") + ")");
    }

    // ---- 線路 ----

    void TapTrackStation(Station st)
    {
        if (trackFirst == null)
        {
            trackFirst = st;
            trackMarker = MakeMarker(st.transform.position, 30f, new Color(1f, 0.85f, 0.2f, 0.5f));
            if (UIController.I != null) UIController.I.RefreshTrackSelection();
            UIController.Toast(st.stationName + "を選択。接続先の駅をタップ");
            return;
        }
        if (trackFirst == st) { ClearTrackSel(); return; }
        var a = trackFirst;
        ClearTrackSel();
        // 既に繋がっている2駅を選び直した場合は、その線路の撤去とみなして確認を出す
        // (線路単体を撤去する唯一の導線。誤って貫通線路を敷いた場合もここで消せる)
        var existing = TrackNetwork.Find(a, st);
        if (existing != null)
        {
            if (UIController.I != null) UIController.I.ConfirmRemoveSegment(existing);
            else UIController.Toast("すでに接続されています");
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
        // 間に別の駅がある区間を直結すると、道床がその駅のホームを貫通して描画される。
        // 通過駅は系統側(FindPath)で表現できるので、線路自体は途中駅を経由して敷いてもらう
        var blocker = new TrackSegment { a = a, b = st, signA = bestSa, signB = bestSb }
            .FindStationCrossedByBed();
        if (blocker != null)
        {
            UIController.Toast(blocker.stationName + "のホームを線路が貫いてしまいます。" +
                blocker.stationName + "を経由して繋いでください(通過運転は系統側で設定できます)");
            return;
        }
        double cost = best * GameState.TrackCostPerM;
        if (!GameState.Spend(cost))
        {
            UIController.Toast("資金不足(" + (cost / 1e8).ToString("F1") + "億円必要)");
            return;
        }
        var seg = new TrackSegment
        {
            id = ++TrackNetwork.segmentIdCounter,
            a = a,
            b = st,
            signA = bestSa,
            signB = bestSb,
            bedType = pTrackBedType,
        };
        seg.Build(WorldRoot);
        TrackNetwork.segments.Add(seg);
        a.RebuildTrackVisual();     // 接続した端を貫通(車止め除去)に
        st.RebuildTrackVisual();
        TrackNetwork.MarkDirty();
        SaveLoad.Save();
        UIController.Toast(a.stationName + "〜" + st.stationName + " " + TrackBedLabel(seg.bedType)
            + "敷設(" + (cost / 1e8).ToString("F1") + "億円)");
    }

    void ClearTrackSel()
    {
        trackFirst = null;
        if (trackMarker != null) { Destroy(trackMarker); trackMarker = null; }
        if (UIController.I != null) UIController.I.RefreshTrackSelection();
    }

    public void CancelTrackSelection()
    {
        if (trackFirst == null) return;
        ClearTrackSel();
        UIController.Toast("駅の選択を解除しました");
    }

    // ---- 列車 ----

    // 系統作成中に停車駅をタップ(またはUIの駅検索結果をタップ)。番線選択待ちにする
    public void TapRouteStation(Station st)
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
            // 直結でなくても、線路で繋がっていれば経路に追加できる(間の駅は通過駅になる。
            // 実際の経由駅列はTrain.TryDepart側で改めて同じFindPathを呼んで求める)
            if (!TrackNetwork.Connected(last, st) && TrackNetwork.FindPath(last, st) == null)
            {
                UIController.Toast(last.stationName + "から線路でつながっていません");
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
        t.id = ++TrackNetwork.trainIdCounter;
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
            UIController.I.ClearStationSearch();
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
