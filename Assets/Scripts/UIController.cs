using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;

// uGUIを全てコードで組む。モードごとのパネル表示切替とHUD更新
public class UIController : MonoBehaviour
{
    public static UIController I;
    public CameraRig rig;

#if UNITY_WEBGL && !UNITY_EDITOR
    // WebGLはブラウザのwindow.promptで駅名入力(iOS Safariでも確実にキーボードが出る)
    [DllImport("__Internal")] static extern string RailPromptName(string def);
#endif

    Text moneyText, clockText, carriedText, toastText, costText, routeText, infoText, fmInfoText;
    Text carsVal, facesVal, linesVal, stationTitle, confirmBtnLabel;
    Station infoStation;
    float removeArmedUntil;
    GameObject stationPanel, trainPanel, infoPanel, toastBg, platformRow, renameModal;
    InputField renameInput;
    // 列車パネル(タブ制): 系統をつくる / 列車を配置
    GameObject serviceTab, dispatchView, lineListView, createView;
    RectTransform lineListRows, dispatchLineRows, itineraryRows;
    Image tabServiceBtn, tabDispatchBtn;
    Image[] typeBtns;
    readonly Dictionary<BuildController.Mode, Image> modeBtns = new Dictionary<BuildController.Mode, Image>();
    readonly List<KeyValuePair<TrainCatalog.Formation, Image>> fmBtns = new List<KeyValuePair<TrainCatalog.Formation, Image>>();
    readonly List<KeyValuePair<float, Image>> speedBtns = new List<KeyValuePair<float, Image>>();
    float toastUntil;

    static readonly Color PanelBg = new Color(0.10f, 0.12f, 0.18f, 0.82f);
    static readonly Color BtnBg = new Color(0.22f, 0.26f, 0.36f, 0.95f);
    static readonly Color BtnActive = new Color(0.95f, 0.60f, 0.15f, 0.95f);
    static readonly Color TxtCol = new Color(0.94f, 0.95f, 0.97f);

    BuildController BC => BuildController.Instance;

    public void Build()
    {
        I = this;
        var canvasGo = gameObject;
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1000, 1600);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        BuildTopBar();
        BuildToolbar();
        BuildStationPanel();
        BuildTrainPanel();
        BuildInfoPanel();
        BuildRenameModal();
        BuildToast();
        OnModeChanged();
    }

    // ---- パーツ生成ヘルパー ----

    RectTransform Rect(string name, Transform parent, Vector2 amin, Vector2 amax, Vector2 omin, Vector2 omax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = amin;
        rt.anchorMax = amax;
        rt.offsetMin = omin;
        rt.offsetMax = omax;
        return rt;
    }

    Image Panel(string name, Transform parent, Vector2 amin, Vector2 amax, Vector2 omin, Vector2 omax, Color c)
    {
        var rt = Rect(name, parent, amin, amax, omin, omax);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = c;
        return img;
    }

    Text Label(string name, Transform parent, string txt, int size, Vector2 amin, Vector2 amax, Vector2 omin, Vector2 omax, TextAnchor align)
    {
        var rt = Rect(name, parent, amin, amax, omin, omax);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = MatLib.JpFont;
        t.fontSize = size;
        t.text = txt;
        t.alignment = align;
        t.color = TxtCol;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    Image Btn(string name, Transform parent, string txt, int size, Vector2 amin, Vector2 amax, Vector2 omin, Vector2 omax, UnityEngine.Events.UnityAction onClick, Color? bg = null)
    {
        var img = Panel(name, parent, amin, amax, omin, omax, bg ?? BtnBg);
        var b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener(onClick);
        Label("t", img.transform, txt, size, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);
        return img;
    }

    // ---- 各部 ----

    void BuildTopBar()
    {
        var bar = Panel("TopBar", transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(6, -104), new Vector2(-6, -6), PanelBg);
        moneyText = Label("Money", bar.transform, "", 30, new Vector2(0, 0.5f), new Vector2(0.55f, 1), new Vector2(14, 0), Vector2.zero, TextAnchor.MiddleLeft);
        clockText = Label("Clock", bar.transform, "", 26, new Vector2(0.55f, 0.5f), new Vector2(1, 1), Vector2.zero, new Vector2(-12, 0), TextAnchor.MiddleRight);
        carriedText = Label("Carried", bar.transform, "", 24, new Vector2(0, 0), new Vector2(0.45f, 0.5f), new Vector2(14, 0), Vector2.zero, TextAnchor.MiddleLeft);
        float[] speeds = { 1, 5, 20 };
        for (int i = 0; i < 3; i++)
        {
            float sp = speeds[i];
            var b = Btn("Sp" + sp, bar.transform, "×" + sp, 24,
                new Vector2(0.40f + 0.12f * i, 0.06f), new Vector2(0.51f + 0.12f * i, 0.48f),
                Vector2.zero, Vector2.zero, () => SetSpeed(sp));
            speedBtns.Add(new KeyValuePair<float, Image>(sp, b));
        }
        Btn("Rot", bar.transform, "視点⟳", 24, new Vector2(0.765f, 0.06f), new Vector2(0.875f, 0.48f),
            Vector2.zero, Vector2.zero, () => { if (rig != null) rig.RotateStep(); });
        Btn("Reset", bar.transform, "初期化", 22, new Vector2(0.885f, 0.06f), new Vector2(0.995f, 0.48f),
            Vector2.zero, Vector2.zero, OnResetTap, new Color(0.45f, 0.2f, 0.22f, 0.95f));
        SetSpeed(GameState.timeScale);
    }

    float resetArmedUntil;

    void OnResetTap()
    {
        if (Time.unscaledTime < resetArmedUntil)
        {
            SaveLoad.ResetAll();
            return;
        }
        resetArmedUntil = Time.unscaledTime + 3.5f;
        Toast("全データを消して最初から始めますか?実行するならもう一度「初期化」をタップ");
    }

    void SetSpeed(float s)
    {
        GameState.timeScale = s;
        foreach (var kv in speedBtns) kv.Value.color = Mathf.Approximately(kv.Key, s) ? BtnActive : BtnBg;
    }

    void BuildToolbar()
    {
        var bar = Panel("Toolbar", transform, Vector2.zero, new Vector2(1, 0), new Vector2(6, 6), new Vector2(-6, 96), PanelBg);
        var modes = new[] { BuildController.Mode.View, BuildController.Mode.Track, BuildController.Mode.Station, BuildController.Mode.Train };
        var names = new[] { "見る", "線路", "駅", "列車" };
        for (int i = 0; i < 4; i++)
        {
            var m = modes[i];
            var img = Btn("M" + names[i], bar.transform, names[i], 30,
                new Vector2(i * 0.2f + 0.005f, 0.08f), new Vector2((i + 1) * 0.2f - 0.005f, 0.92f),
                Vector2.zero, Vector2.zero, () => BC.SetMode(m));
            modeBtns[m] = img;
        }
        cabBtn = Btn("MCab", bar.transform, "展望", 30,
            new Vector2(0.805f, 0.08f), new Vector2(0.995f, 0.92f),
            Vector2.zero, Vector2.zero, OnCabTap);
    }

    Image cabBtn;
    int cabIdx;

    void OnCabTap()
    {
        var trains = Object.FindObjectsByType<Train>(FindObjectsSortMode.None);
        if (trains.Length == 0)
        {
            Toast("列車がいません。先に「列車」モードで走らせてください");
            return;
        }
        cabIdx = rig.cabTrain == null ? 0 : (cabIdx + 1) % trains.Length;
        rig.EnterCab(trains[cabIdx]);
        cabBtn.color = BtnActive;
        Toast("前面展望: " + trains[cabIdx].fm.Label +
            (trains.Length > 1 ? "(もう一度タップで次の列車、" : "(") + "「見る」で戻る)");
    }

    void BuildStationPanel()
    {
        var p = Panel("StationPanel", transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, Vector2.zero, PanelBg);
        var rt = p.rectTransform;
        rt.pivot = new Vector2(0, 0.5f);
        rt.anchoredPosition = new Vector2(8, 40);
        rt.sizeDelta = new Vector2(330, 620);
        stationPanel = p.gameObject;
        float y = -14;
        stationTitle = Label("Title", p.transform, "駅を建設", 30, new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, y - 44), new Vector2(-14, y), TextAnchor.MiddleLeft);
        y -= 56;
        carsVal = ParamRow(p.transform, "対応両数", ref y, () => ChangeCars(-1), () => ChangeCars(1));
        facesVal = ParamRow(p.transform, "ホーム(面)", ref y, () => ChangeFaces(-1), () => ChangeFaces(1));
        linesVal = ParamRow(p.transform, "番線(線)", ref y, () => ChangeLines(-1), () => ChangeLines(1));
        Btn("Yaw", p.transform, "向きを45°回転", 26, new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, y - 56), new Vector2(-14, y), RotatePreview);
        y -= 68;
        costText = Label("Cost", p.transform, "", 26, new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, y - 44), new Vector2(-14, y), TextAnchor.MiddleLeft);
        y -= 52;
        var confirmImg = Btn("Confirm", p.transform, "ここに建設", 30, new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, y - 66), new Vector2(-14, y), () => BC.ConfirmStation(), BtnActive);
        confirmBtnLabel = confirmImg.GetComponentInChildren<Text>();
        y -= 78;
        Label("Hint", p.transform, "地面をタップして位置を選択\n→「ここに建設」で確定", 22, new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, y - 70), new Vector2(-14, y), TextAnchor.UpperLeft);
    }

    Text ParamRow(Transform parent, string title, ref float y, UnityEngine.Events.UnityAction minus, UnityEngine.Events.UnityAction plus)
    {
        Label("L" + title, parent, title, 26, new Vector2(0, 1), new Vector2(0.45f, 1), new Vector2(14, y - 56), new Vector2(0, y), TextAnchor.MiddleLeft);
        Btn("-" + title, parent, "-", 34, new Vector2(0.46f, 1), new Vector2(0.62f, 1), new Vector2(0, y - 56), new Vector2(0, y), minus);
        var v = Label("V" + title, parent, "", 28, new Vector2(0.62f, 1), new Vector2(0.82f, 1), new Vector2(0, y - 56), new Vector2(0, y), TextAnchor.MiddleCenter);
        Btn("+" + title, parent, "+", 34, new Vector2(0.82f, 1), new Vector2(0.98f, 1), new Vector2(0, y - 56), new Vector2(0, y), plus);
        y -= 64;
        return v;
    }

    void ChangeCars(int d) { BC.pCars = Mathf.Clamp(BC.pCars + d, 2, 10); BC.ApplyPreviewParams(); }
    void ChangeFaces(int d)
    {
        BC.pFaces = Mathf.Clamp(BC.pFaces + d, 1, 4);
        BC.pLines = Mathf.Clamp(BC.pLines, BC.pFaces, 8);
        BC.ApplyPreviewParams();
    }
    void ChangeLines(int d) { BC.pLines = Mathf.Clamp(BC.pLines + d, Mathf.Max(1, BC.pFaces), 8); BC.ApplyPreviewParams(); }
    void RotatePreview() { BC.pYaw = Mathf.Repeat(BC.pYaw + 45f, 360f); BC.ApplyPreviewParams(); }

    void BuildTrainPanel()
    {
        var p = Panel("TrainPanel", transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, Vector2.zero, PanelBg);
        var rt = p.rectTransform;
        rt.pivot = new Vector2(1, 0.5f);
        rt.anchoredPosition = new Vector2(-8, 40);
        rt.sizeDelta = new Vector2(362, 968);
        trainPanel = p.gameObject;

        tabServiceBtn = Btn("TabS", p.transform, "系統をつくる", 23, new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(10, -62), new Vector2(-3, -10), () => SetTab(0));
        tabDispatchBtn = Btn("TabD", p.transform, "列車を配置", 23, new Vector2(0.5f, 1), new Vector2(1, 1), new Vector2(3, -62), new Vector2(-10, -10), () => SetTab(1));

        serviceTab = Rect("ServiceTab", p.transform, Vector2.zero, Vector2.one, new Vector2(10, 12), new Vector2(-10, -70)).gameObject;
        dispatchView = Rect("DispatchView", p.transform, Vector2.zero, Vector2.one, new Vector2(10, 12), new Vector2(-10, -70)).gameObject;
        BuildServiceTab(serviceTab.transform);
        BuildDispatchView(dispatchView.transform);
    }

    void BuildServiceTab(Transform parent)
    {
        // 一覧ビュー(Manage)
        lineListView = Rect("LineList", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
        Btn("NewLine", lineListView.transform, "＋ 新しい系統を作る", 25, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -58), new Vector2(0, -2), () => BC.BeginCreateLine(), BtnActive);
        Label("LLTitle", lineListView.transform, "運行系統一覧", 22, new Vector2(0, 1), new Vector2(1, 1), new Vector2(2, -98), new Vector2(-2, -66), TextAnchor.MiddleLeft);
        lineListRows = Rect("Rows", lineListView.transform, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0, -102));

        // 作成ビュー(CreateLine)
        createView = Rect("Create", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
        Label("CT", createView.transform, "種別を選択", 22, new Vector2(0, 1), new Vector2(1, 1), new Vector2(2, -34), new Vector2(-2, -2), TextAnchor.MiddleLeft);
        typeBtns = new Image[ServiceType.Names.Length];
        for (int i = 0; i < ServiceType.Names.Length; i++)
        {
            int ti = i;
            float w = 1f / ServiceType.Names.Length;
            typeBtns[i] = Btn("Ty" + i, createView.transform, ServiceType.Names[i], 22,
                new Vector2(i * w + 0.01f, 1), new Vector2((i + 1) * w - 0.01f, 1),
                new Vector2(0, -92), new Vector2(0, -40), () => BC.SetNewLineType(ti));
        }
        var prRt = Rect("PlatformRow", createView.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -152), new Vector2(0, -100));
        platformRow = prRt.gameObject;
        platformRow.SetActive(false);
        routeText = Label("Route", createView.transform, "", 21, new Vector2(0, 1), new Vector2(1, 1), new Vector2(2, -280), new Vector2(-2, -160), TextAnchor.UpperLeft);
        Btn("SaveLine", createView.transform, "系統を保存", 26, new Vector2(0, 1), new Vector2(0.6f, 1), new Vector2(0, -344), new Vector2(-3, -290), () => BC.SaveNewLine(), BtnActive);
        Btn("CancelLine", createView.transform, "やめる", 24, new Vector2(0.62f, 1), new Vector2(1, 1), new Vector2(3, -344), new Vector2(0, -290), () => BC.CancelCreateLine());
    }

    void BuildDispatchView(Transform parent)
    {
        float y = -2;
        Label("DT", parent, "編成を選ぶ", 21, new Vector2(0, 1), new Vector2(1, 1), new Vector2(2, y - 28), new Vector2(-2, y), TextAnchor.MiddleLeft);
        y -= 34;
        foreach (var fm in TrainCatalog.Formations)
        {
            var f = fm;
            var img = Btn("F" + f.Label, parent, f.Label + "  " + (f.CostYen / 1e8).ToString("F0") + "億円", 21,
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, y - 46), new Vector2(0, y), () => SelectFormation(f));
            fmBtns.Add(new KeyValuePair<TrainCatalog.Formation, Image>(f, img));
            y -= 50;
        }
        fmInfoText = Label("FmInfo", parent, "編成を選択してください", 19, new Vector2(0, 1), new Vector2(1, 1), new Vector2(2, y - 42), new Vector2(-2, y), TextAnchor.UpperLeft);
        y -= 48;
        Label("DLT", parent, "系統を運用に追加(タップ)", 21, new Vector2(0, 1), new Vector2(1, 1), new Vector2(2, y - 28), new Vector2(-2, y), TextAnchor.MiddleLeft);
        y -= 32;
        dispatchLineRows = Rect("DRows", parent, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, y - 150), new Vector2(0, y));
        y -= 156;
        Label("ITT", parent, "この列車の運用(順に走る)", 21, new Vector2(0, 1), new Vector2(1, 1), new Vector2(2, y - 28), new Vector2(-2, y), TextAnchor.MiddleLeft);
        y -= 32;
        itineraryRows = Rect("ITRows", parent, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, y - 176), new Vector2(0, y));
        y -= 182;
        Btn("Dispatch", parent, "この運用で配置(購入)", 24, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, y - 54), new Vector2(0, y), () => BC.DispatchTrain(), BtnActive);
    }

    // 運用(順序付き)の行: 番号・種別色ラベル・上下・削除
    void BuildItineraryRows()
    {
        if (itineraryRows == null) return;
        for (int i = itineraryRows.childCount - 1; i >= 0; i--) Destroy(itineraryRows.GetChild(i).gameObject);
        if (BC.selLines.Count == 0)
        {
            Label("ite", itineraryRows, "上の系統をタップして運用を組む", 19,
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(2, -52), new Vector2(-2, -2), TextAnchor.UpperLeft);
            return;
        }
        float y = -2;
        for (int i = 0; i < BC.selLines.Count; i++)
        {
            int idx = i;
            var l = BC.selLines[i];
            var tc = l.TypeColor;
            Label("N" + i, itineraryRows, (i + 1) + ".", 20, new Vector2(0, 1), new Vector2(0.1f, 1), new Vector2(2, y - 46), new Vector2(0, y), TextAnchor.MiddleCenter);
            Btn("IL" + i, itineraryRows, l.DisplayName, 18, new Vector2(0.1f, 1), new Vector2(0.62f, 1), new Vector2(0, y - 46), new Vector2(-2, y), () => { }, new Color(tc.r, tc.g, tc.b, 0.85f));
            Btn("Up" + i, itineraryRows, "↑", 22, new Vector2(0.62f, 1), new Vector2(0.74f, 1), new Vector2(1, y - 46), new Vector2(-1, y), () => BC.MoveItinerary(idx, -1));
            Btn("Dn" + i, itineraryRows, "↓", 22, new Vector2(0.74f, 1), new Vector2(0.86f, 1), new Vector2(1, y - 46), new Vector2(-1, y), () => BC.MoveItinerary(idx, 1));
            Btn("Rm" + i, itineraryRows, "×", 22, new Vector2(0.86f, 1), new Vector2(1, 1), new Vector2(1, y - 46), new Vector2(0, y), () => BC.RemoveFromItinerary(idx), new Color(0.55f, 0.22f, 0.24f, 0.95f));
            y -= 52;
        }
    }

    void SetTab(int t)
    {
        if (t == 0) BC.GoManageTab();
        else BC.GoDispatchTab();
    }

    // 列車パネルの表示をBC.trainSub/系統一覧に合わせて更新
    public void RefreshTrainPanel()
    {
        if (serviceTab == null) return;
        bool dispatch = BC.trainSub == BuildController.TrainSub.Dispatch;
        serviceTab.SetActive(!dispatch);
        dispatchView.SetActive(dispatch);
        if (tabServiceBtn != null) tabServiceBtn.color = dispatch ? BtnBg : BtnActive;
        if (tabDispatchBtn != null) tabDispatchBtn.color = dispatch ? BtnActive : BtnBg;

        if (!dispatch)
        {
            bool creating = BC.trainSub == BuildController.TrainSub.CreateLine;
            lineListView.SetActive(!creating);
            createView.SetActive(creating);
            if (creating)
            {
                for (int i = 0; i < typeBtns.Length; i++)
                    typeBtns[i].color = i == BC.newLineType ? ServiceType.Colors[i] : BtnBg;
                UpdateRouteLabel();
            }
            else BuildLineRows(lineListRows, false);
        }
        else
        {
            BuildLineRows(dispatchLineRows, true);
            BuildItineraryRows();
            foreach (var kv in fmBtns) kv.Value.color = kv.Key == BC.selFormation ? BtnActive : BtnBg;
        }
    }

    void BuildLineRows(RectTransform container, bool forDispatch)
    {
        if (container == null) return;
        for (int i = container.childCount - 1; i >= 0; i--) Destroy(container.GetChild(i).gameObject);
        if (Services.lines.Count == 0)
        {
            Label("empty", container, forDispatch ? "先に「系統をつくる」で作成してください" : "系統がありません。上のボタンで作成", 20,
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(2, -56), new Vector2(-2, -2), TextAnchor.UpperLeft);
            return;
        }
        float y = -2;
        foreach (var line in Services.lines)
        {
            var l = line;
            var tc = l.TypeColor;
            string label = l.DisplayName + "  ×" + l.TrainCount + "本";
            if (forDispatch)
            {
                Btn("L" + l.id, container, "＋ " + label, 19, new Vector2(0, 1), new Vector2(1, 1),
                    new Vector2(0, y - 46), new Vector2(0, y), () => BC.AddToItinerary(l), new Color(tc.r, tc.g, tc.b, 0.85f));
                y -= 52;
                continue;
            }
            else
            {
                Btn("L" + l.id, container, label, 20, new Vector2(0, 1), new Vector2(0.78f, 1),
                    new Vector2(0, y - 52), new Vector2(-3, y), () => { }, new Color(tc.r, tc.g, tc.b, 0.85f));
                Btn("Del" + l.id, container, "廃止", 20, new Vector2(0.8f, 1), new Vector2(1, 1),
                    new Vector2(3, y - 52), new Vector2(0, y), () => BC.DeleteLine(l), new Color(0.55f, 0.22f, 0.24f, 0.95f));
            }
            y -= 58;
        }
    }

    void SelectFormation(TrainCatalog.Formation f)
    {
        BC.selFormation = f;
        foreach (var kv in fmBtns) kv.Value.color = kv.Key == f ? BtnActive : BtnBg;
        fmInfoText.text = f.Label + ": 定員" + f.Capacity + "人 / 最高" + f.type.maxSpeedKmh + "km/h\n停車駅は" + f.cars + "両以上対応が必要";
    }

    public void UpdateRouteLabel()
    {
        if (routeText == null) return;
        if (BC.routeSel.Count == 0)
        {
            routeText.text = "駅をタップ→番線を選ぶと経路になります";
            return;
        }
        var names = new List<string>();
        for (int i = 0; i < BC.routeSel.Count; i++)
        {
            var s = BC.routeSel[i];
            int pf = s.PlatformNumberOf(BC.routeTrackSel[i]);
            names.Add(s.stationName + "(" + pf + "番)");
        }
        routeText.text = "経路: " + string.Join(" → ", names.ToArray());
    }

    // pendingStationの番線ボタンを列車パネル内に動的生成
    public void ShowPlatformPicker(Station st)
    {
        HidePlatformPicker();
        if (platformRow == null) return;
        platformRow.SetActive(true);
        int n = st.PlatformCount;
        for (int i = 0; i < n; i++)
        {
            int pf = i + 1;
            float w = 1f / n;
            Btn("PF" + pf, platformRow.transform, pf + "番線", n > 4 ? 20 : 24,
                new Vector2(i * w + 0.01f, 0.1f), new Vector2((i + 1) * w - 0.01f, 0.9f),
                Vector2.zero, Vector2.zero, () => BC.AddRouteStop(pf), new Color(0.2f, 0.55f, 0.75f, 0.95f));
        }
    }

    public void HidePlatformPicker()
    {
        if (platformRow == null) return;
        for (int i = platformRow.transform.childCount - 1; i >= 0; i--)
            Destroy(platformRow.transform.GetChild(i).gameObject);
        platformRow.SetActive(false);
    }

    void BuildInfoPanel()
    {
        var p = Panel("InfoPanel", transform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, Vector2.zero, PanelBg);
        var rt = p.rectTransform;
        rt.pivot = new Vector2(0.5f, 1);
        rt.anchoredPosition = new Vector2(0, -120);
        rt.sizeDelta = new Vector2(480, 380);
        infoPanel = p.gameObject;
        infoText = Label("Info", p.transform, "", 25, Vector2.zero, Vector2.one, new Vector2(16, 184), new Vector2(-16, -12), TextAnchor.UpperLeft);
        Btn("Rename", p.transform, "名前を変更", 26, new Vector2(0.04f, 0), new Vector2(0.96f, 0), new Vector2(0, 126), new Vector2(0, 174), OnRenameTap, new Color(0.24f, 0.42f, 0.5f, 0.95f));
        Btn("Rebuild", p.transform, "建て替え", 26, new Vector2(0.04f, 0), new Vector2(0.49f, 0), new Vector2(0, 70), new Vector2(0, 118), OnRebuildTap, BtnActive);
        Btn("Remove", p.transform, "撤去", 26, new Vector2(0.51f, 0), new Vector2(0.96f, 0), new Vector2(0, 70), new Vector2(0, 118), OnRemoveTap, new Color(0.55f, 0.22f, 0.24f, 0.95f));
        Btn("Close", p.transform, "閉じる", 24, new Vector2(0.3f, 0), new Vector2(0.7f, 0), new Vector2(0, 12), new Vector2(0, 60), HideStationInfo);
        infoPanel.SetActive(false);
    }

    // 駅名入力: WebGLはブラウザのpromptを使い、それ以外(エディタ等)は自前のモーダル
    void OnRenameTap()
    {
        if (infoStation == null) return;
#if UNITY_WEBGL && !UNITY_EDITOR
        ApplyRename(RailPromptName(infoStation.stationName));
#else
        renameInput.text = infoStation.stationName;
        renameModal.SetActive(true);
        renameInput.Select();
        renameInput.ActivateInputField();
#endif
    }

    void OnRenameOk()
    {
        renameModal.SetActive(false);
        ApplyRename(renameInput.text);
    }

    void ApplyRename(string name)
    {
        if (infoStation == null) return;
        name = (name ?? "").Trim();
        if (name.Length == 0) { Toast("駅名が空だったので変更しませんでした"); return; }
        if (name.Length > 12) name = name.Substring(0, 12);
        infoStation.stationName = name;
        infoStation.gameObject.name = name;
        infoStation.UpdateLabel();
        ShowStationInfo(infoStation);   // 情報パネルを更新表示
        SaveLoad.Save();
        Toast("駅名を「" + name + "」に変更しました");
    }

    // エディタ/非WebGL用の駅名入力モーダル(uGUI InputField)
    void BuildRenameModal()
    {
        var overlay = Panel("RenameModal", transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0.55f));
        renameModal = overlay.gameObject;
        var box = Panel("Box", overlay.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, PanelBg);
        box.rectTransform.sizeDelta = new Vector2(580, 300);
        Label("T", box.transform, "駅名を変更", 30, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -66), new Vector2(-24, -16), TextAnchor.MiddleLeft);
        var ifImg = Panel("IF", box.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -156), new Vector2(-24, -82), new Color(0.96f, 0.97f, 0.99f, 1f));
        renameInput = ifImg.gameObject.AddComponent<InputField>();
        var txt = Label("Txt", ifImg.transform, "", 30, Vector2.zero, Vector2.one, new Vector2(14, 4), new Vector2(-14, -4), TextAnchor.MiddleLeft);
        txt.color = new Color(0.1f, 0.1f, 0.13f);
        txt.supportRichText = false;
        var ph = Label("PH", ifImg.transform, "駅名を入力", 30, Vector2.zero, Vector2.one, new Vector2(14, 4), new Vector2(-14, -4), TextAnchor.MiddleLeft);
        ph.color = new Color(0.45f, 0.45f, 0.5f);
        renameInput.textComponent = txt;
        renameInput.placeholder = ph;
        renameInput.targetGraphic = ifImg;
        renameInput.characterLimit = 12;
        renameInput.lineType = InputField.LineType.SingleLine;
        Btn("OK", box.transform, "決定", 28, new Vector2(0, 0), new Vector2(0.48f, 0), new Vector2(24, 24), new Vector2(0, 88), OnRenameOk, BtnActive);
        Btn("Cancel", box.transform, "キャンセル", 26, new Vector2(0.52f, 0), new Vector2(1, 0), new Vector2(0, 24), new Vector2(-24, 88), () => renameModal.SetActive(false));
        renameModal.SetActive(false);
    }

    void OnRebuildTap()
    {
        if (infoStation != null) BC.BeginRebuild(infoStation);
    }

    void OnRemoveTap()
    {
        if (infoStation == null) return;
        if (Time.unscaledTime < removeArmedUntil)
        {
            var st = infoStation;
            HideStationInfo();
            BC.RemoveStation(st);
            return;
        }
        removeArmedUntil = Time.unscaledTime + 3.5f;
        Toast(infoStation.stationName + "を撤去しますか?接続線路と通過列車も消えます。もう一度「撤去」で確定");
    }

    void BuildToast()
    {
        var p = Panel("Toast", transform, new Vector2(0.05f, 0), new Vector2(0.95f, 0), new Vector2(0, 104), new Vector2(0, 168), new Color(0, 0, 0, 0.65f));
        toastBg = p.gameObject;
        toastText = Label("T", p.transform, "", 28, Vector2.zero, Vector2.one, new Vector2(12, 4), new Vector2(-12, -4), TextAnchor.MiddleCenter);
        toastBg.SetActive(false);
    }

    public static void Toast(string msg)
    {
        if (I == null) { Debug.Log("Toast: " + msg); return; }
        I.toastText.text = msg;
        I.toastBg.SetActive(true);
        I.toastUntil = Time.unscaledTime + 4.5f;
    }

    public void ShowStationInfo(Station st)
    {
        infoStation = st;
        removeArmedUntil = 0;
        infoPanel.SetActive(true);
        infoText.text = st.stationName + "  (" + st.cars + "両対応・" + st.faces + "面" + st.lines + "線)\n"
            + "待ち客: " + st.TotalWaiting + "人 / 上限" + st.WaitingCap + "\n"
            + "発展レベル: " + st.DevLevel + "\n"
            + "接続駅: " + TrackNetwork.Reachable(st).Count + "駅";
    }

    public void HideStationInfo()
    {
        infoStation = null;
        infoPanel.SetActive(false);
    }

    public void OnModeChanged()
    {
        if (rig != null) rig.ExitCab();
        if (cabBtn != null) cabBtn.color = BtnBg;
        var mode = BC.mode;
        foreach (var kv in modeBtns) kv.Value.color = kv.Key == mode ? BtnActive : BtnBg;
        stationPanel.SetActive(mode == BuildController.Mode.Station);
        trainPanel.SetActive(mode == BuildController.Mode.Train);
        if (mode == BuildController.Mode.Train) RefreshTrainPanel();
        if (mode != BuildController.Mode.View) HideStationInfo();
        UpdateRouteLabel();
    }

    void Update()
    {
        moneyText.text = "資金 " + GameState.MoneyLabel;
        clockText.text = rig != null && rig.cabTrain != null
            ? GameState.ClockLabel + "  " + rig.cabTrain.SpeedKmh.ToString("F0") + "km/h"
            : GameState.ClockLabel;
        carriedText.text = "輸送人員 " + GameState.carried + "人";
        if (stationPanel.activeSelf)
        {
            carsVal.text = BC.pCars + "両";
            facesVal.text = BC.pFaces + "面";
            linesVal.text = BC.pLines + "線";
            double newCost = GameState.StationCost(BC.pCars, BC.pFaces, BC.pLines);
            var rt = BC.rebuildTarget;
            if (rt != null)
            {
                stationTitle.text = "駅を建て替え";
                confirmBtnLabel.text = "建て替え確定";
                double delta = newCost - GameState.StationCost(rt.cars, rt.faces, rt.lines);
                costText.text = delta > 0 ? "追加費用 " + (delta / 1e8).ToString("F1") + "億円"
                    : delta < 0 ? "払戻 " + (-delta * 0.5 / 1e8).ToString("F1") + "億円"
                    : "費用なし";
            }
            else
            {
                stationTitle.text = "駅を建設";
                confirmBtnLabel.text = "ここに建設";
                costText.text = "建設費 " + (newCost / 1e8).ToString("F1") + "億円";
            }
        }
        if (toastBg.activeSelf && Time.unscaledTime > toastUntil) toastBg.SetActive(false);
    }
}
