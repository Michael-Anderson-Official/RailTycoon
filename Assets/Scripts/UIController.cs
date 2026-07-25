using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;

// uGUIを全てコードで組む。セーフエリアと縦横画面へ追従し、モードごとの操作を
// 「選ぶ→地図で指定→確定」の順に見せる。prefabを持たない方針は維持する。
public class UIController : MonoBehaviour
{
    public static UIController I;
    public CameraRig rig;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern string RailPromptName(string def);
#endif

    Text moneyText, clockText, carriedText, toastText, costText, routeText, infoText;
    Text carsVal, facesVal, linesVal, stationTitle, confirmBtnLabel, trackHintText, pauseLabel;
    Text onboardingTitle, onboardingBody, onboardingButtonLabel;
    Station infoStation;

    RectTransform safeRoot, topBarRt, toolbarRt, trackPanelRt, stationPanelRt, trainPanelRt;
    RectTransform infoPanelRt, toastRt, cameraToolsRt, onboardingRt, edgeBoxRt;
    GameObject trackPanel, stationPanel, trainPanel, infoPanel, toastBg, cameraTools, onboardingPanel;
    GameObject renameModal, edgePanel, settingsPanel, confirmModal;
    InputField renameInput, stationSearchInput;
    RectTransform trainContent, stationSearchRows, platformRow, edgeRows;
    ScrollRect trainScroll;
    Text confirmTitle, confirmBody;
    Action pendingConfirm;

    Image cabBtn, pauseBtn, stationConfirmImage, saveLineImage, dispatchImage;
    Button stationConfirmButton;
    readonly Dictionary<BuildController.Mode, Image> modeBtns =
        new Dictionary<BuildController.Mode, Image>();
    readonly Dictionary<TrackBedType, Image> trackBedBtns =
        new Dictionary<TrackBedType, Image>();
    readonly List<KeyValuePair<float, Image>> speedBtns =
        new List<KeyValuePair<float, Image>>();
    readonly List<Image> stationPresetBtns = new List<Image>();

    float toastUntil, nextSlowRefresh;
    Vector2Int lastScreenSize = new Vector2Int(-1, -1);
    Rect lastSafeArea;
    BuildController.TrainSub lastTrainSub = (BuildController.TrainSub)(-1);
    int onboardingStage = -1;
    int cabIdx;

    public const float PortraitTopHeight = 154f;
    public const float LandscapeTopHeight = 104f;
    public const float PortraitToolbarHeight = 112f;
    public const float LandscapeToolbarHeight = 96f;
    public const float MinimumPrimaryButtonHeight = 54f;

    static readonly Color PanelBg = new Color(0.055f, 0.075f, 0.12f, 0.94f);
    static readonly Color PanelSoft = new Color(0.10f, 0.13f, 0.20f, 0.96f);
    static readonly Color BtnBg = new Color(0.16f, 0.21f, 0.31f, 1f);
    static readonly Color BtnActive = new Color(0.84f, 0.0f, 0.39f, 1f); // 京王レッド
    static readonly Color BtnSelected = new Color(0.96f, 0.52f, 0.08f, 1f);
    static readonly Color BtnBlue = new Color(0.08f, 0.39f, 0.67f, 1f);
    static readonly Color Danger = new Color(0.64f, 0.12f, 0.18f, 1f);
    static readonly Color TxtCol = new Color(0.96f, 0.97f, 0.99f);
    static readonly Color MutedTxt = new Color(0.70f, 0.75f, 0.82f);

    BuildController BC => BuildController.Instance;
    bool IsPortrait => Screen.height > Screen.width * 1.08f;

    public void Build()
    {
        I = this;
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1000f, 1600f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        safeRoot = Rect("SafeArea", transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        BuildTopBar();
        BuildToolbar();
        BuildTrackPanel();
        BuildStationPanel();
        BuildTrainPanel();
        BuildInfoPanel();
        BuildCameraTools();
        BuildOnboarding();
        BuildRenameModal();
        BuildEdgeModal();
        BuildSettingsModal();
        BuildConfirmModal();
        BuildToast();

        ApplyResponsiveLayout();
        OnModeChanged();
        RefreshOnboarding();
    }

    // ---- 基本パーツ ----

    RectTransform Rect(string name, Transform parent, Vector2 amin, Vector2 amax,
        Vector2 omin, Vector2 omax)
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

    Image Panel(string name, Transform parent, Vector2 amin, Vector2 amax,
        Vector2 omin, Vector2 omax, Color color)
    {
        var rt = Rect(name, parent, amin, amax, omin, omax);
        var image = rt.gameObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    Text Label(string name, Transform parent, string value, int size,
        Vector2 amin, Vector2 amax, Vector2 omin, Vector2 omax, TextAnchor align)
    {
        var rt = Rect(name, parent, amin, amax, omin, omax);
        var text = rt.gameObject.AddComponent<Text>();
        text.font = MatLib.JpFont;
        text.fontSize = size;
        text.text = value;
        text.alignment = align;
        text.color = TxtCol;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = Mathf.Max(13, size - 8);
        text.resizeTextMaxSize = size;
        return text;
    }

    Image Btn(string name, Transform parent, string value, int size,
        Vector2 amin, Vector2 amax, Vector2 omin, Vector2 omax,
        UnityEngine.Events.UnityAction onClick, Color? bg = null)
    {
        var image = Panel(name, parent, amin, amax, omin, omax, bg ?? BtnBg);
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.88f);
        colors.pressedColor = new Color(0.68f, 0.68f, 0.68f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(0.42f, 0.42f, 0.42f, 0.72f);
        colors.fadeDuration = 0.06f;
        button.colors = colors;
        Label("Text", image.transform, value, size, Vector2.zero, Vector2.one,
            new Vector2(7f, 3f), new Vector2(-7f, -3f), TextAnchor.MiddleCenter);
        return image;
    }

    static void SetButtonEnabled(Image image, bool enabled)
    {
        if (image == null) return;
        var button = image.GetComponent<Button>();
        if (button != null) button.interactable = enabled;
    }

    RectTransform FlowColumn(string name, Transform parent, int padding = 0, float spacing = 10f)
    {
        var rt = Rect(name, parent, new Vector2(0f, 1f), Vector2.one, Vector2.zero, Vector2.zero);
        rt.pivot = new Vector2(0.5f, 1f);
        var layout = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(padding, padding, padding, padding);
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = rt.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return rt;
    }

    RectTransform FlowRow(string name, Transform parent, float height, float spacing = 8f)
    {
        var rt = Rect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
        var layout = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        layout.childAlignment = TextAnchor.MiddleCenter;
        return rt;
    }

    Text FlowLabel(string name, Transform parent, string value, int size, float height,
        TextAnchor align = TextAnchor.MiddleLeft, Color? color = null)
    {
        var text = Label(name, parent, value, size, Vector2.zero, Vector2.one,
            Vector2.zero, Vector2.zero, align);
        if (color.HasValue) text.color = color.Value;
        var le = text.gameObject.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
        le.flexibleWidth = 1f;
        return text;
    }

    Image FlowButton(string name, Transform parent, string value, int size, float height,
        UnityEngine.Events.UnityAction onClick, Color? color = null, float width = -1f,
        float flexibleWidth = 1f)
    {
        var image = Btn(name, parent, value, size, Vector2.zero, Vector2.one,
            Vector2.zero, Vector2.zero, onClick, color);
        var le = image.gameObject.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
        if (width > 0f)
        {
            le.minWidth = width;
            le.preferredWidth = width;
        }
        le.flexibleWidth = flexibleWidth;
        return image;
    }

    RectTransform ScrollColumn(string name, Transform parent, Vector2 omin, Vector2 omax,
        out ScrollRect scroll)
    {
        var root = Rect(name, parent, Vector2.zero, Vector2.one, omin, omax);
        scroll = root.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 44f;

        var viewport = Rect("Viewport", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = FlowColumn("Content", viewport, 14, 10f);
        scroll.viewport = viewport;
        scroll.content = content;
        return content;
    }

    // ---- HUD / ナビゲーション ----

    void BuildTopBar()
    {
        var bar = Panel("TopBar", safeRoot, new Vector2(0f, 1f), Vector2.one,
            new Vector2(6f, -PortraitTopHeight), new Vector2(-6f, -6f), PanelBg);
        topBarRt = bar.rectTransform;
        moneyText = Label("Money", bar.transform, "", 30, new Vector2(0f, 0.50f),
            new Vector2(0.58f, 1f), new Vector2(14f, 0f), Vector2.zero, TextAnchor.MiddleLeft);
        clockText = Label("Clock", bar.transform, "", 26, new Vector2(0.58f, 0.50f),
            Vector2.one, Vector2.zero, new Vector2(-14f, 0f), TextAnchor.MiddleRight);
        carriedText = Label("Carried", bar.transform, "", 22, Vector2.zero,
            new Vector2(0.33f, 0.49f), new Vector2(14f, 0f), Vector2.zero, TextAnchor.MiddleLeft);

        pauseBtn = Btn("Pause", bar.transform, "停止", 21, new Vector2(0.34f, 0.06f),
            new Vector2(0.46f, 0.45f), Vector2.zero, Vector2.zero, TogglePause);
        pauseLabel = pauseBtn.GetComponentInChildren<Text>();

        float[] speeds = { 1f, 5f, 20f };
        for (int i = 0; i < speeds.Length; i++)
        {
            float speed = speeds[i];
            var image = Btn("Speed" + speed, bar.transform, "×" + speed, 21,
                new Vector2(0.47f + i * 0.115f, 0.06f),
                new Vector2(0.575f + i * 0.115f, 0.45f),
                Vector2.zero, Vector2.zero, () => SetSpeed(speed));
            speedBtns.Add(new KeyValuePair<float, Image>(speed, image));
        }
        Btn("Settings", bar.transform, "設定", 21, new Vector2(0.825f, 0.06f),
            new Vector2(0.99f, 0.45f), Vector2.zero, Vector2.zero,
            () => settingsPanel.SetActive(true));
        SetSpeed(GameState.timeScale);
    }

    void TogglePause()
    {
        GameState.paused = !GameState.paused;
        RefreshSpeedButtons();
    }

    void SetSpeed(float speed)
    {
        GameState.timeScale = speed;
        GameState.paused = false;
        RefreshSpeedButtons();
    }

    void RefreshSpeedButtons()
    {
        if (pauseBtn != null)
        {
            pauseBtn.color = GameState.paused ? BtnSelected : BtnBg;
            pauseLabel.text = GameState.paused ? "再開" : "停止";
        }
        foreach (var kv in speedBtns)
            kv.Value.color = !GameState.paused && Mathf.Approximately(kv.Key, GameState.timeScale)
                ? BtnActive : BtnBg;
    }

    void BuildToolbar()
    {
        var bar = Panel("Toolbar", safeRoot, Vector2.zero, new Vector2(1f, 0f),
            new Vector2(6f, 6f), new Vector2(-6f, PortraitToolbarHeight), PanelBg);
        toolbarRt = bar.rectTransform;
        var modes = new[]
        {
            BuildController.Mode.View, BuildController.Mode.Track,
            BuildController.Mode.Station, BuildController.Mode.Train,
        };
        var names = new[] { "運行", "線路", "駅", "系統" };
        for (int i = 0; i < modes.Length; i++)
        {
            var mode = modes[i];
            var image = Btn("Mode" + mode, bar.transform, names[i], 27,
                new Vector2(i * 0.2f + 0.005f, 0.08f),
                new Vector2((i + 1) * 0.2f - 0.005f, 0.92f),
                Vector2.zero, Vector2.zero, () => BC.SetMode(mode));
            modeBtns[mode] = image;
        }
        cabBtn = Btn("Cab", bar.transform, "車窓", 27, new Vector2(0.805f, 0.08f),
            new Vector2(0.995f, 0.92f), Vector2.zero, Vector2.zero, OnCabTap);
    }

    void OnCabTap()
    {
        var trains = FindObjectsByType<Train>(FindObjectsSortMode.None);
        if (trains.Length == 0)
        {
            Toast("まだ列車がありません。「系統」で運行系統と列車を配置してください");
            return;
        }
        cabIdx = rig != null && rig.cabTrain != null ? (cabIdx + 1) % trains.Length : 0;
        if (rig != null) rig.EnterCab(trains[cabIdx]);
        cabBtn.color = BtnActive;
        RefreshOnboarding();
        Toast("車窓: " + trains[cabIdx].fm.Label +
            (trains.Length > 1 ? "（もう一度で次の列車）" : "") + "／「運行」で戻る");
    }

    // ---- 線路 ----

    void BuildTrackPanel()
    {
        var panel = Panel("TrackPanel", safeRoot, Vector2.zero, Vector2.zero,
            Vector2.zero, Vector2.zero, PanelBg);
        trackPanel = panel.gameObject;
        trackPanelRt = panel.rectTransform;
        Label("Title", panel.transform, "線路を敷く", 31, new Vector2(0f, 1f),
            new Vector2(0.72f, 1f), new Vector2(16f, -62f), new Vector2(0f, -10f),
            TextAnchor.MiddleLeft);
        Btn("Close", panel.transform, "閉じる", 21, new Vector2(0.74f, 1f), Vector2.one,
            new Vector2(0f, -60f), new Vector2(-12f, -10f), () => BC.SetMode(BuildController.Mode.View));

        trackHintText = Label("Step", panel.transform, "", 23, new Vector2(0f, 1f),
            new Vector2(1f, 1f), new Vector2(16f, -120f), new Vector2(-16f, -68f),
            TextAnchor.MiddleLeft);
        var ballast = Btn("Ballast", panel.transform, "バラスト\n低コスト", 23,
            new Vector2(0f, 1f), new Vector2(0.5f, 1f),
            new Vector2(16f, -196f), new Vector2(-5f, -130f),
            () => BC.SetTrackBedType(TrackBedType.Ballast));
        var slab = Btn("Slab", panel.transform, "スラブ\n高耐久", 23,
            new Vector2(0.5f, 1f), Vector2.one,
            new Vector2(5f, -196f), new Vector2(-16f, -130f),
            () => BC.SetTrackBedType(TrackBedType.Slab));
        trackBedBtns[TrackBedType.Ballast] = ballast;
        trackBedBtns[TrackBedType.Slab] = slab;
        Btn("ClearSelection", panel.transform, "駅の選択を解除", 22,
            new Vector2(0f, 1f), new Vector2(0.52f, 1f),
            new Vector2(16f, -262f), new Vector2(-5f, -208f),
            () => BC.CancelTrackSelection());
        Label("Help", panel.transform, "地図はドラッグで移動、ピンチ／±で拡大縮小できます", 19,
            new Vector2(0.52f, 1f), Vector2.one,
            new Vector2(7f, -262f), new Vector2(-16f, -208f), TextAnchor.MiddleLeft).color = MutedTxt;
    }

    public void RefreshTrackBedButtons()
    {
        if (BC == null) return;
        foreach (var kv in trackBedBtns)
            kv.Value.color = kv.Key == BC.pTrackBedType ? BtnSelected : BtnBg;
    }

    public void RefreshTrackSelection()
    {
        if (trackHintText == null || BC == null) return;
        if (TrackNetwork.stations.Count < 2)
            trackHintText.text = "先に駅を2つ建ててください";
        else if (BC.TrackFirst == null)
            trackHintText.text = "1 / 2　始点の駅を地図でタップ";
        else
            trackHintText.text = "2 / 2　" + BC.TrackFirst.stationName + " → 接続先をタップ";
    }

    // ---- 駅 ----

    void BuildStationPanel()
    {
        var panel = Panel("StationPanel", safeRoot, Vector2.zero, Vector2.zero,
            Vector2.zero, Vector2.zero, PanelBg);
        stationPanel = panel.gameObject;
        stationPanelRt = panel.rectTransform;
        float y = -10f;
        stationTitle = Label("Title", panel.transform, "駅を建てる", 31,
            new Vector2(0f, 1f), new Vector2(0.72f, 1f),
            new Vector2(16f, y - 52f), new Vector2(0f, y), TextAnchor.MiddleLeft);
        Btn("Close", panel.transform, "閉じる", 21, new Vector2(0.74f, 1f), Vector2.one,
            new Vector2(0f, y - 50f), new Vector2(-12f, y), () => BC.SetMode(BuildController.Mode.View));
        y -= 58f;
        var mapHint = Label("MapHint", panel.transform, "1　地図をタップして建設位置を決める", 22,
            new Vector2(0f, 1f), Vector2.one, new Vector2(16f, y - 40f),
            new Vector2(-16f, y), TextAnchor.MiddleLeft);
        mapHint.color = MutedTxt;
        y -= 48f;
        Label("PresetLabel", panel.transform, "2　ホーム構成", 22,
            new Vector2(0f, 1f), Vector2.one, new Vector2(16f, y - 32f),
            new Vector2(-16f, y), TextAnchor.MiddleLeft);
        y -= 38f;

        const int cols = 3;
        const float cellH = 52f;
        for (int i = 0; i < BuildController.StationPresets.Length; i++)
        {
            int index = i;
            int col = i % cols;
            int row = i / cols;
            float x0 = col / (float)cols;
            float x1 = (col + 1) / (float)cols;
            float rowTop = y - row * (cellH + 5f);
            var image = Btn("Preset" + i, panel.transform,
                BuildController.StationPresets[i].label, 18,
                new Vector2(x0, 1f), new Vector2(x1, 1f),
                new Vector2(col == 0 ? 16f : 4f, rowTop - cellH),
                new Vector2(col == cols - 1 ? -16f : -4f, rowTop),
                () => { BC.ApplyStationPreset(index); RefreshStationPanel(); });
            stationPresetBtns.Add(image);
        }
        y -= 2f * (cellH + 5f) + 8f;
        carsVal = ParamRow(panel.transform, "対応両数", ref y,
            () => ChangeCars(-1), () => ChangeCars(1));
        facesVal = ParamRow(panel.transform, "ホーム面数", ref y,
            () => ChangeFaces(-1), () => ChangeFaces(1));
        linesVal = ParamRow(panel.transform, "線路本数", ref y,
            () => ChangeLines(-1), () => ChangeLines(1));
        Btn("Yaw", panel.transform, "駅の向きを45°回転", 24,
            new Vector2(0f, 1f), Vector2.one, new Vector2(16f, y - 56f),
            new Vector2(-16f, y), RotatePreview);
        y -= 64f;
        costText = Label("Cost", panel.transform, "", 25,
            new Vector2(0f, 1f), Vector2.one, new Vector2(16f, y - 44f),
            new Vector2(-16f, y), TextAnchor.MiddleLeft);
        y -= 50f;
        stationConfirmImage = Btn("Confirm", panel.transform, "ここに建設", 28,
            new Vector2(0f, 1f), new Vector2(0.68f, 1f),
            new Vector2(16f, y - 64f), new Vector2(-5f, y),
            () => BC.ConfirmStation(), BtnActive);
        stationConfirmButton = stationConfirmImage.GetComponent<Button>();
        confirmBtnLabel = stationConfirmImage.GetComponentInChildren<Text>();
        Btn("Cancel", panel.transform, "やめる", 23,
            new Vector2(0.69f, 1f), Vector2.one,
            new Vector2(5f, y - 64f), new Vector2(-16f, y),
            () => BC.SetMode(BuildController.Mode.View));
    }

    Text ParamRow(Transform parent, string title, ref float y,
        UnityEngine.Events.UnityAction minus, UnityEngine.Events.UnityAction plus)
    {
        Label("Label" + title, parent, title, 24, new Vector2(0f, 1f),
            new Vector2(0.42f, 1f), new Vector2(16f, y - 56f),
            new Vector2(0f, y), TextAnchor.MiddleLeft);
        Btn("Minus" + title, parent, "−", 32, new Vector2(0.43f, 1f),
            new Vector2(0.59f, 1f), new Vector2(0f, y - 56f), new Vector2(-3f, y), minus);
        var value = Label("Value" + title, parent, "", 27,
            new Vector2(0.60f, 1f), new Vector2(0.82f, 1f),
            new Vector2(2f, y - 56f), new Vector2(-2f, y), TextAnchor.MiddleCenter);
        Btn("Plus" + title, parent, "＋", 31, new Vector2(0.83f, 1f), Vector2.one,
            new Vector2(3f, y - 56f), new Vector2(-16f, y), plus);
        y -= 63f;
        return value;
    }

    void ChangeCars(int delta)
    {
        BC.pCars = Mathf.Clamp(BC.pCars + delta, 2, 10);
        BC.ApplyPreviewParams();
        RefreshStationPanel();
    }

    void ChangeFaces(int delta)
    {
        BC.pFaces = Mathf.Clamp(BC.pFaces + delta, 1, 4);
        BC.pLines = Mathf.Clamp(BC.pLines, Mathf.Max(1, BC.pFaces - 1), 8);
        BC.ApplyPreviewParams();
        RefreshStationPanel();
    }

    void ChangeLines(int delta)
    {
        BC.pLines = Mathf.Clamp(BC.pLines + delta, Mathf.Max(1, BC.pFaces - 1), 8);
        BC.ApplyPreviewParams();
        RefreshStationPanel();
    }

    void RotatePreview()
    {
        BC.pYaw = Mathf.Repeat(BC.pYaw + 45f, 360f);
        BC.ApplyPreviewParams();
    }

    void RefreshStationPanel()
    {
        if (stationPanel == null || BC == null) return;
        carsVal.text = BC.pCars + "両";
        facesVal.text = BC.pFaces + "面";
        linesVal.text = BC.pLines + "線";
        for (int i = 0; i < stationPresetBtns.Count; i++)
        {
            var preset = BuildController.StationPresets[i];
            stationPresetBtns[i].color = preset.faces == BC.pFaces && preset.lines == BC.pLines
                ? BtnSelected : BtnBg;
        }

        double newCost = GameState.StationCost(BC.pCars, BC.pFaces, BC.pLines);
        bool affordable;
        if (BC.rebuildTarget != null)
        {
            stationTitle.text = "駅を建て替える";
            confirmBtnLabel.text = "建て替え確定";
            double delta = newCost - GameState.StationCost(
                BC.rebuildTarget.cars, BC.rebuildTarget.faces, BC.rebuildTarget.lines);
            affordable = delta <= 0 || GameState.money >= delta;
            costText.text = delta > 0 ? "追加費用　" + (delta / 1e8).ToString("F1") + "億円"
                : delta < 0 ? "払戻　" + (-delta * 0.5 / 1e8).ToString("F1") + "億円"
                : "費用なし";
        }
        else
        {
            stationTitle.text = "駅を建てる";
            confirmBtnLabel.text = BC.previewStation == null ? "位置を選んでください" : "ここに建設";
            affordable = GameState.money >= newCost;
            costText.text = "建設費　" + (newCost / 1e8).ToString("F1") + "億円";
        }
        costText.color = affordable ? TxtCol : new Color(1f, 0.46f, 0.46f);
        stationConfirmButton.interactable =
            (BC.previewStation != null || BC.rebuildTarget != null) && affordable;
        stationConfirmImage.color = stationConfirmButton.interactable ? BtnActive : BtnBg;
    }

    // ---- 系統 / 配車 ----

    void BuildTrainPanel()
    {
        var panel = Panel("TrainPanel", safeRoot, Vector2.zero, Vector2.zero,
            Vector2.zero, Vector2.zero, PanelBg);
        trainPanel = panel.gameObject;
        trainPanelRt = panel.rectTransform;
        Label("Title", panel.transform, "系統と列車", 31, new Vector2(0f, 1f),
            new Vector2(0.72f, 1f), new Vector2(16f, -60f),
            new Vector2(0f, -10f), TextAnchor.MiddleLeft);
        Btn("Close", panel.transform, "閉じる", 21, new Vector2(0.74f, 1f), Vector2.one,
            new Vector2(0f, -58f), new Vector2(-12f, -10f),
            () => BC.SetMode(BuildController.Mode.View));
        Btn("ServiceTab", panel.transform, "系統をつくる", 23,
            new Vector2(0f, 1f), new Vector2(0.5f, 1f),
            new Vector2(12f, -120f), new Vector2(-4f, -66f), () => SetTrainTab(false));
        Btn("DispatchTab", panel.transform, "列車を配置", 23,
            new Vector2(0.5f, 1f), Vector2.one,
            new Vector2(4f, -120f), new Vector2(-12f, -66f), () => SetTrainTab(true));
        trainContent = ScrollColumn("TrainScroll", panel.transform,
            new Vector2(8f, 10f), new Vector2(-8f, -128f), out trainScroll);
    }

    void SetTrainTab(bool dispatch)
    {
        if (dispatch) BC.GoDispatchTab();
        else BC.GoManageTab();
    }

    public void RefreshTrainPanel()
    {
        if (trainContent == null || BC == null) return;
        bool resetScroll = lastTrainSub != BC.trainSub;
        lastTrainSub = BC.trainSub;
        for (int i = trainContent.childCount - 1; i >= 0; i--)
            Destroy(trainContent.GetChild(i).gameObject);
        stationSearchInput = null;
        stationSearchRows = null;
        platformRow = null;
        routeText = null;
        saveLineImage = null;
        dispatchImage = null;

        var tabService = trainPanel.transform.Find("ServiceTab").GetComponent<Image>();
        var tabDispatch = trainPanel.transform.Find("DispatchTab").GetComponent<Image>();
        bool dispatch = BC.trainSub == BuildController.TrainSub.Dispatch;
        tabService.color = dispatch ? BtnBg : BtnActive;
        tabDispatch.color = dispatch ? BtnActive : BtnBg;

        if (dispatch) BuildDispatchFlow();
        else if (BC.trainSub == BuildController.TrainSub.CreateLine) BuildCreateLineFlow();
        else BuildManageFlow();

        Canvas.ForceUpdateCanvases();
        if (resetScroll && trainScroll != null) trainScroll.verticalNormalizedPosition = 1f;
    }

    void BuildManageFlow()
    {
        FlowLabel("Intro", trainContent,
            "停車駅と番線を順に登録して、列車が走る運行系統を作ります。", 21, 66f,
            TextAnchor.MiddleLeft, MutedTxt);
        FlowButton("NewLine", trainContent, "＋ 新しい系統を作る", 25,
            MinimumPrimaryButtonHeight + 6f, () => BC.BeginCreateLine(), BtnActive);
        FlowLabel("ListTitle", trainContent, "運行系統", 23, 42f);
        if (Services.lines.Count == 0)
        {
            FlowLabel("Empty", trainContent, "まだ系統がありません。上のボタンから作成してください。",
                20, 70f, TextAnchor.UpperLeft, MutedTxt);
            return;
        }
        foreach (var line in Services.lines)
        {
            var captured = line;
            var row = FlowRow("Line" + line.id, trainContent, 62f);
            var color = line.TypeColor;
            FlowButton("Summary", row,
                line.DisplayName + "　列車" + line.TrainCount + "本", 20, 62f,
                () => ShowLineSummary(captured), new Color(color.r, color.g, color.b, 0.92f),
                -1f, 1f);
            FlowButton("Delete", row, "廃止", 20, 62f,
                () => ConfirmLineDelete(captured), Danger, 92f, 0f);
        }
    }

    void ShowLineSummary(ServiceLine line)
    {
        var names = new List<string>();
        foreach (var st in line.route) names.Add(st.stationName);
        Toast(line.DisplayName + "：" + string.Join(" → ", names.ToArray()));
    }

    void ConfirmLineDelete(ServiceLine line)
    {
        string extra = line.TrainCount > 0
            ? "\nこの系統を走る列車" + line.TrainCount + "本も撤去されます。"
            : "";
        ShowConfirm("系統を廃止しますか？",
            line.DisplayName + "を廃止します。" + extra + "\nこの操作は取り消せません。",
            () => BC.DeleteLine(line));
    }

    void BuildCreateLineFlow()
    {
        FlowLabel("Step1", trainContent, "1　列車種別を選ぶ", 23, 38f);
        var typeRow = FlowRow("Types", trainContent, 58f, 5f);
        for (int i = 0; i < ServiceType.Names.Length; i++)
        {
            int type = i;
            var image = FlowButton("Type" + i, typeRow, ServiceType.Names[i], 20, 58f,
                () => BC.SetNewLineType(type),
                i == BC.newLineType ? ServiceType.Colors[i] : BtnBg);
            image.color = i == BC.newLineType ? ServiceType.Colors[i] : BtnBg;
        }

        FlowLabel("Step2", trainContent, "2　停車駅を順番に追加する", 23, 38f);
        FlowLabel("MapHelp", trainContent,
            "地図の駅をタップするか、駅名で検索してください。駅ごとに使う番線を選びます。",
            19, 62f, TextAnchor.MiddleLeft, MutedTxt);
        stationSearchInput = FlowInput("StationSearch", trainContent, "駅名で検索", 52f);
        stationSearchInput.onValueChanged.AddListener(OnStationSearchChanged);

        stationSearchRows = Rect("SearchResults", trainContent, Vector2.zero, Vector2.one,
            Vector2.zero, Vector2.zero);
        var searchLayout = stationSearchRows.gameObject.AddComponent<VerticalLayoutGroup>();
        searchLayout.spacing = 5f;
        searchLayout.childControlHeight = true;
        searchLayout.childControlWidth = true;
        searchLayout.childForceExpandHeight = false;
        searchLayout.childForceExpandWidth = true;
        var searchElement = stationSearchRows.gameObject.AddComponent<LayoutElement>();
        searchElement.preferredHeight = 0f;

        if (BC.pendingStation != null)
        {
            FlowLabel("PlatformPrompt", trainContent,
                BC.pendingStation.stationName + "：使用する番線", 22, 38f);
            platformRow = FlowRow("PlatformRow", trainContent, 58f, 5f);
            int count = BC.pendingStation.PlatformCount;
            for (int i = 0; i < count; i++)
            {
                int platform = i + 1;
                FlowButton("Platform" + platform, platformRow, platform + "番", 21, 58f,
                    () => BC.AddRouteStop(platform), BtnBlue);
            }
        }

        FlowLabel("Step3", trainContent, "3　経路を確認して保存", 23, 38f);
        float routeHeight = Mathf.Clamp(72f + BC.routeSel.Count * 12f, 72f, 170f);
        routeText = FlowLabel("Route", trainContent, "", 20, routeHeight,
            TextAnchor.UpperLeft, TxtCol);
        var actions = FlowRow("Actions", trainContent, 62f);
        saveLineImage = FlowButton("SaveLine", actions, "系統を保存", 24, 62f,
            () => BC.SaveNewLine(), BtnActive);
        FlowButton("CancelLine", actions, "やめる", 22, 62f,
            () => BC.CancelCreateLine(), BtnBg, 126f, 0f);
        UpdateRouteLabel();
    }

    InputField FlowInput(string name, Transform parent, string placeholder, float height)
    {
        var image = Panel(name, parent, Vector2.zero, Vector2.one,
            Vector2.zero, Vector2.zero, new Color(0.96f, 0.97f, 0.99f, 1f));
        var le = image.gameObject.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
        var input = image.gameObject.AddComponent<InputField>();
        var text = Label("Text", image.transform, "", 23, Vector2.zero, Vector2.one,
            new Vector2(12f, 3f), new Vector2(-12f, -3f), TextAnchor.MiddleLeft);
        text.color = new Color(0.08f, 0.10f, 0.14f);
        text.supportRichText = false;
        var hint = Label("Placeholder", image.transform, placeholder, 23,
            Vector2.zero, Vector2.one, new Vector2(12f, 3f),
            new Vector2(-12f, -3f), TextAnchor.MiddleLeft);
        hint.color = new Color(0.43f, 0.47f, 0.53f);
        input.textComponent = text;
        input.placeholder = hint;
        input.targetGraphic = image;
        input.characterLimit = 24;
        input.lineType = InputField.LineType.SingleLine;
        return input;
    }

    void OnStationSearchChanged(string query)
    {
        if (stationSearchRows == null) return;
        for (int i = stationSearchRows.childCount - 1; i >= 0; i--)
            Destroy(stationSearchRows.GetChild(i).gameObject);
        var element = stationSearchRows.GetComponent<LayoutElement>();
        if (string.IsNullOrWhiteSpace(query))
        {
            element.preferredHeight = 0f;
            return;
        }

        int matches = 0;
        foreach (var station in TrackNetwork.stations)
        {
            if (station.id == 0 || station.preview) continue;
            if (station.stationName.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var captured = station;
            FlowButton("Station" + station.id, stationSearchRows, station.stationName,
                20, 50f, () => BC.TapRouteStation(captured), BtnBlue);
            matches++;
            if (matches >= 6) break;
        }
        if (matches == 0)
        {
            FlowLabel("NoResult", stationSearchRows, "一致する駅がありません", 19, 48f,
                TextAnchor.MiddleLeft, MutedTxt);
            matches = 1;
        }
        element.preferredHeight = matches * 55f;
        Canvas.ForceUpdateCanvases();
    }

    void BuildDispatchFlow()
    {
        FlowLabel("Intro", trainContent,
            "編成と運行系統を選び、1本の列車として配置します。複数系統を順につなげられます。",
            20, 66f, TextAnchor.MiddleLeft, MutedTxt);
        FlowLabel("Step1", trainContent, "1　購入する編成", 23, 38f);
        foreach (var formation in TrainCatalog.Formations)
        {
            var captured = formation;
            bool selected = BC.selFormation == formation;
            FlowButton("Formation" + formation.Label, trainContent,
                (selected ? "選択中　" : "") + formation.Label + "　" +
                (formation.CostYen / 1e8).ToString("F0") + "億円", 20, 52f,
                () => SelectFormation(captured), selected ? BtnSelected : BtnBg);
        }
        string formationInfo = BC.selFormation == null
            ? "編成を選んでください"
            : "定員" + BC.selFormation.Capacity + "人／最高" +
              BC.selFormation.type.maxSpeedKmh + "km/h／" +
              BC.selFormation.cars + "両対応ホームが必要";
        FlowLabel("FormationInfo", trainContent, formationInfo, 19, 48f,
            TextAnchor.MiddleLeft, MutedTxt);

        FlowLabel("Step2", trainContent, "2　走らせる系統を追加", 23, 38f);
        if (Services.lines.Count == 0)
        {
            FlowLabel("NoLines", trainContent,
                "系統がありません。「系統をつくる」タブで先に作成してください。",
                20, 66f, TextAnchor.MiddleLeft, MutedTxt);
        }
        else
        {
            foreach (var line in Services.lines)
            {
                var captured = line;
                var color = line.TypeColor;
                FlowButton("AddLine" + line.id, trainContent,
                    "＋ " + line.DisplayName, 20, 52f, () => BC.AddToItinerary(captured),
                    new Color(color.r, color.g, color.b, 0.92f));
            }
        }

        FlowLabel("Step3", trainContent, "3　この列車の運用順", 23, 38f);
        if (BC.selLines.Count == 0)
        {
            FlowLabel("EmptyItinerary", trainContent,
                "上の系統をタップして追加してください。", 19, 52f,
                TextAnchor.MiddleLeft, MutedTxt);
        }
        for (int i = 0; i < BC.selLines.Count; i++)
        {
            int index = i;
            var line = BC.selLines[i];
            var row = FlowRow("Itinerary" + i, trainContent, 54f, 5f);
            FlowLabel("Name", row, (i + 1) + "　" + line.DisplayName, 19, 54f);
            FlowButton("Up", row, "↑", 22, 54f,
                () => BC.MoveItinerary(index, -1), BtnBg, 54f, 0f);
            FlowButton("Down", row, "↓", 22, 54f,
                () => BC.MoveItinerary(index, 1), BtnBg, 54f, 0f);
            FlowButton("Remove", row, "×", 22, 54f,
                () => BC.RemoveFromItinerary(index), Danger, 54f, 0f);
        }
        dispatchImage = FlowButton("Dispatch", trainContent, "この運用で列車を購入・配置", 24,
            64f, () => BC.DispatchTrain(), BtnActive);
        SetButtonEnabled(dispatchImage, BC.selFormation != null && BC.selLines.Count > 0);
    }

    void SelectFormation(TrainCatalog.Formation formation)
    {
        BC.selFormation = formation;
        RefreshTrainPanel();
    }

    public void UpdateRouteLabel()
    {
        if (routeText == null || BC == null) return;
        if (BC.routeSel.Count == 0)
            routeText.text = "停車駅はまだありません";
        else
        {
            var names = new List<string>();
            for (int i = 0; i < BC.routeSel.Count; i++)
            {
                var station = BC.routeSel[i];
                int platform = station.PlatformNumberOf(BC.routeTrackSel[i]);
                names.Add((i + 1) + ". " + station.stationName + "（" + platform + "番）");
            }
            routeText.text = string.Join("\n", names.ToArray());
        }
        SetButtonEnabled(saveLineImage, BC.routeSel.Count >= 2);
    }

    public void ShowPlatformPicker(Station station)
    {
        if (trainPanel != null && trainPanel.activeSelf) RefreshTrainPanel();
    }

    public void HidePlatformPicker()
    {
        if (trainPanel != null && trainPanel.activeSelf &&
            BC != null && BC.trainSub == BuildController.TrainSub.CreateLine)
            RefreshTrainPanel();
    }

    public void ClearStationSearch()
    {
        if (stationSearchInput != null) stationSearchInput.SetTextWithoutNotify("");
        if (stationSearchRows == null) return;
        for (int i = stationSearchRows.childCount - 1; i >= 0; i--)
            Destroy(stationSearchRows.GetChild(i).gameObject);
        var element = stationSearchRows.GetComponent<LayoutElement>();
        if (element != null) element.preferredHeight = 0f;
    }

    // ---- 駅情報 ----

    void BuildInfoPanel()
    {
        var panel = Panel("InfoPanel", safeRoot, Vector2.zero, Vector2.zero,
            Vector2.zero, Vector2.zero, PanelBg);
        infoPanel = panel.gameObject;
        infoPanelRt = panel.rectTransform;
        Label("Title", panel.transform, "駅の情報", 30, new Vector2(0f, 1f),
            new Vector2(0.72f, 1f), new Vector2(16f, -60f),
            new Vector2(0f, -10f), TextAnchor.MiddleLeft);
        Btn("Close", panel.transform, "閉じる", 21, new Vector2(0.74f, 1f), Vector2.one,
            new Vector2(0f, -58f), new Vector2(-12f, -10f), HideStationInfo);
        infoText = Label("Info", panel.transform, "", 23, new Vector2(0f, 1f), Vector2.one,
            new Vector2(16f, -226f), new Vector2(-16f, -70f), TextAnchor.UpperLeft);
        Btn("Focus", panel.transform, "この駅を中心に見る", 22,
            new Vector2(0.04f, 0f), new Vector2(0.96f, 0f),
            new Vector2(0f, 252f), new Vector2(0f, 304f), FocusInfoStation, BtnBlue);
        Btn("Edges", panel.transform, "番線ごとの乗降設定", 22,
            new Vector2(0.04f, 0f), new Vector2(0.96f, 0f),
            new Vector2(0f, 192f), new Vector2(0f, 244f), OnPlatformEdgesTap);
        Btn("Rename", panel.transform, "駅名を変更", 22,
            new Vector2(0.04f, 0f), new Vector2(0.96f, 0f),
            new Vector2(0f, 132f), new Vector2(0f, 184f), OnRenameTap);
        Btn("Rebuild", panel.transform, "建て替え", 23,
            new Vector2(0.04f, 0f), new Vector2(0.49f, 0f),
            new Vector2(0f, 72f), new Vector2(0f, 124f), OnRebuildTap, BtnSelected);
        Btn("Remove", panel.transform, "撤去", 23,
            new Vector2(0.51f, 0f), new Vector2(0.96f, 0f),
            new Vector2(0f, 72f), new Vector2(0f, 124f), OnRemoveTap, Danger);
        Btn("Dismiss", panel.transform, "閉じる", 21,
            new Vector2(0.30f, 0f), new Vector2(0.70f, 0f),
            new Vector2(0f, 14f), new Vector2(0f, 62f), HideStationInfo);
        infoPanel.SetActive(false);
    }

    void RefreshInfoText()
    {
        if (infoStation == null || infoText == null) return;
        infoText.text = infoStation.stationName + "\n" +
            infoStation.cars + "両対応　" + infoStation.faces + "面" +
            infoStation.lines + "線\n" +
            "待ち客　" + infoStation.TotalWaiting + " / " + infoStation.WaitingCap + "人\n" +
            "発展レベル　" + infoStation.DevLevel + "　　接続駅　" +
            TrackNetwork.Reachable(infoStation).Count + "駅";
    }

    void FocusInfoStation()
    {
        if (infoStation != null && rig != null)
            rig.FocusOn(infoStation.transform.position, 165f);
    }

    public void ShowStationInfo(Station station)
    {
        infoStation = station;
        infoPanel.SetActive(true);
        RefreshInfoText();
        ApplyResponsiveLayout();
    }

    public void HideStationInfo()
    {
        infoStation = null;
        if (infoPanel != null) infoPanel.SetActive(false);
        ApplyResponsiveLayout();
    }

    void OnRebuildTap()
    {
        if (infoStation != null) BC.BeginRebuild(infoStation);
    }

    void OnRemoveTap()
    {
        if (infoStation == null) return;
        var station = infoStation;
        ShowConfirm("駅を撤去しますか？",
            station.stationName + "と、接続線路・通過列車・関連系統を削除します。\n" +
            "建設費などの一部は払い戻されます。この操作は取り消せません。",
            () =>
            {
                HideStationInfo();
                BC.RemoveStation(station);
            });
    }

    // ---- カメラ / 初心者ガイド ----

    void BuildCameraTools()
    {
        var panel = Panel("CameraTools", safeRoot, Vector2.zero, Vector2.zero,
            Vector2.zero, Vector2.zero, PanelBg);
        cameraTools = panel.gameObject;
        cameraToolsRt = panel.rectTransform;
        Btn("ZoomIn", panel.transform, "＋", 32, new Vector2(0.08f, 0.76f),
            new Vector2(0.92f, 0.98f), Vector2.zero, Vector2.zero,
            () => { if (rig != null) rig.ZoomBy(0.72f); });
        Btn("ZoomOut", panel.transform, "−", 32, new Vector2(0.08f, 0.52f),
            new Vector2(0.92f, 0.74f), Vector2.zero, Vector2.zero,
            () => { if (rig != null) rig.ZoomBy(1.38f); });
        Btn("Rotate", panel.transform, "回転", 20, new Vector2(0.08f, 0.28f),
            new Vector2(0.92f, 0.50f), Vector2.zero, Vector2.zero,
            () => { if (rig != null) rig.RotateStep(); });
        Btn("Home", panel.transform, "全体", 20, new Vector2(0.08f, 0.04f),
            new Vector2(0.92f, 0.26f), Vector2.zero, Vector2.zero,
            () => { if (rig != null) rig.FrameNetwork(); });
    }

    void BuildOnboarding()
    {
        var panel = Panel("NextAction", safeRoot, new Vector2(0.06f, 0f),
            new Vector2(0.94f, 0f), Vector2.zero, Vector2.zero, PanelBg);
        onboardingPanel = panel.gameObject;
        onboardingRt = panel.rectTransform;
        onboardingTitle = Label("Title", panel.transform, "", 27,
            new Vector2(0f, 0.60f), Vector2.one, new Vector2(16f, 0f),
            new Vector2(-68f, -10f), TextAnchor.MiddleLeft);   // 右上の×ボタンぶん空ける
        // 案内を閉じられるようにする。タップ領域は非退行ルールの54以上を確保する
        Btn("Close", panel.transform, "×", 26,
            new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-62f, -60f), new Vector2(-8f, -6f), DismissOnboarding);
        onboardingBody = Label("Body", panel.transform, "", 20,
            new Vector2(0f, 0.28f), new Vector2(1f, 0.61f), new Vector2(16f, 0f),
            new Vector2(-16f, 0f), TextAnchor.MiddleLeft);
        var action = Btn("Action", panel.transform, "", 23,
            new Vector2(0.04f, 0.05f), new Vector2(0.96f, 0.29f),
            Vector2.zero, Vector2.zero, RunOnboardingAction, BtnActive);
        onboardingButtonLabel = action.GetComponentInChildren<Text>();
        onboardingPanel.SetActive(false);
    }

    void RefreshOnboarding()
    {
        if (onboardingPanel == null || BC == null) return;
        int stage = -1;
        if (BC.mode == BuildController.Mode.View && (rig == null || rig.cabTrain == null))
        {
            if (TrackNetwork.stations.Count == 0) stage = 0;
            else if (TrackNetwork.stations.Count == 1) stage = 1;
            else if (TrackNetwork.segments.Count == 0) stage = 2;
            else if (Services.lines.Count == 0) stage = 3;
            else if (TrackNetwork.trains.Count == 0) stage = 4;
        }
        // ×で閉じた段階は再表示しない。進捗が次の段階へ進めば、新しい案内は改めて出す
        if (stage >= 0 && dismissedOnboarding.Contains(stage)) stage = -1;
        onboardingStage = stage;
        onboardingPanel.SetActive(stage >= 0);
        PositionToast(IsPortrait ? PortraitToolbarHeight : LandscapeToolbarHeight);
        if (stage < 0) return;
        string[] titles =
        {
            "最初の駅を建てましょう", "次の駅を建てましょう", "駅を線路でつなぎましょう",
            "運行系統を作りましょう", "列車を配置しましょう",
        };
        string[] bodies =
        {
            "駅を選び、地図上の建てたい場所をタップします。",
            "列車を走らせるには、まず2つ以上の駅が必要です。",
            "線路の種類を選び、始点と終点の駅を順にタップします。",
            "停車駅と番線を登録すると、列車を配置できるようになります。",
            "編成と系統を選ぶと、運行が始まります。",
        };
        string[] actions = { "駅を建てる", "2つ目の駅を建てる", "線路を敷く", "系統を作る", "列車を配置" };
        onboardingTitle.text = titles[stage];
        onboardingBody.text = bodies[stage];
        onboardingButtonLabel.text = actions[stage];
    }

    // ×で閉じた段階を覚えておく(このセッション中のみ)。次の段階へ進めばまた出る
    readonly HashSet<int> dismissedOnboarding = new HashSet<int>();

    void DismissOnboarding()
    {
        if (onboardingStage >= 0) dismissedOnboarding.Add(onboardingStage);
        onboardingStage = -1;
        onboardingPanel.SetActive(false);
        PositionToast(IsPortrait ? PortraitToolbarHeight : LandscapeToolbarHeight);
    }

    void RunOnboardingAction()
    {
        if (onboardingStage == 0 || onboardingStage == 1)
            BC.SetMode(BuildController.Mode.Station);
        else if (onboardingStage == 2)
            BC.SetMode(BuildController.Mode.Track);
        else if (onboardingStage == 3)
        {
            BC.SetMode(BuildController.Mode.Train);
            BC.BeginCreateLine();
        }
        else if (onboardingStage == 4)
        {
            BC.SetMode(BuildController.Mode.Train);
            BC.GoDispatchTab();
        }
    }

    // ---- モーダル ----

    void BuildRenameModal()
    {
        var overlay = Panel("RenameModal", transform, Vector2.zero, Vector2.one,
            Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.68f));
        renameModal = overlay.gameObject;
        var box = Panel("Box", overlay.transform, new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, PanelBg);
        box.rectTransform.sizeDelta = new Vector2(650f, 320f);
        Label("Title", box.transform, "駅名を変更", 30, new Vector2(0f, 1f), Vector2.one,
            new Vector2(24f, -68f), new Vector2(-24f, -16f), TextAnchor.MiddleLeft);
        var inputImage = Panel("Input", box.transform, new Vector2(0f, 1f), Vector2.one,
            new Vector2(24f, -162f), new Vector2(-24f, -84f),
            new Color(0.96f, 0.97f, 0.99f, 1f));
        renameInput = inputImage.gameObject.AddComponent<InputField>();
        var text = Label("Text", inputImage.transform, "", 29, Vector2.zero, Vector2.one,
            new Vector2(14f, 4f), new Vector2(-14f, -4f), TextAnchor.MiddleLeft);
        text.color = new Color(0.08f, 0.10f, 0.14f);
        text.supportRichText = false;
        var placeholder = Label("Placeholder", inputImage.transform, "駅名を入力", 29,
            Vector2.zero, Vector2.one, new Vector2(14f, 4f),
            new Vector2(-14f, -4f), TextAnchor.MiddleLeft);
        placeholder.color = new Color(0.43f, 0.47f, 0.53f);
        renameInput.textComponent = text;
        renameInput.placeholder = placeholder;
        renameInput.targetGraphic = inputImage;
        renameInput.characterLimit = 12;
        renameInput.lineType = InputField.LineType.SingleLine;
        Btn("OK", box.transform, "変更する", 26, new Vector2(0f, 0f),
            new Vector2(0.49f, 0f), new Vector2(24f, 24f),
            new Vector2(-4f, 94f), OnRenameOk, BtnActive);
        Btn("Cancel", box.transform, "キャンセル", 24, new Vector2(0.51f, 0f),
            new Vector2(1f, 0f), new Vector2(4f, 24f),
            new Vector2(-24f, 94f), () => renameModal.SetActive(false));
        renameModal.SetActive(false);
    }

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

    void ApplyRename(string value)
    {
        if (infoStation == null) return;
        value = (value ?? "").Trim();
        if (value.Length == 0)
        {
            Toast("駅名が空のため変更しませんでした");
            return;
        }
        if (value.Length > 12) value = value.Substring(0, 12);
        infoStation.stationName = value;
        infoStation.gameObject.name = value;
        infoStation.UpdateLabel();
        RefreshInfoText();
        SaveLoad.Save();
        Toast("駅名を「" + value + "」に変更しました");
    }

    void BuildEdgeModal()
    {
        // セーフエリア内で全画面を覆い、Box本体はApplyResponsiveLayoutで
        // 利用可能高に収める。横向きスマホでもタイトルと閉じるボタンを隠さない。
        var overlay = Panel("EdgeModal", safeRoot, Vector2.zero, Vector2.one,
            Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.68f));
        edgePanel = overlay.gameObject;
        var box = Panel("Box", overlay.transform, new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, PanelBg);
        edgeBoxRt = box.rectTransform;
        edgeBoxRt.sizeDelta = new Vector2(700f, 920f);
        Label("Title", box.transform, "番線ごとの乗降設定", 29,
            new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -62f),
            new Vector2(-24f, -14f), TextAnchor.MiddleLeft);
        var hint = Label("Hint", box.transform,
            "ボタンをタップして、乗降可・乗車専用・降車専用・使用停止を切り替えます。",
            18, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -112f),
            new Vector2(-24f, -66f), TextAnchor.MiddleLeft);
        hint.color = MutedTxt;
        edgeRows = ScrollColumn("EdgeScroll", box.transform,
            new Vector2(18f, 84f), new Vector2(-18f, -124f), out _);
        Btn("Close", box.transform, "閉じる", 24, new Vector2(0.28f, 0f),
            new Vector2(0.72f, 0f), new Vector2(0f, 16f),
            new Vector2(0f, 70f), () => edgePanel.SetActive(false));
        edgePanel.SetActive(false);
    }

    void OnPlatformEdgesTap()
    {
        if (infoStation == null) return;
        edgePanel.SetActive(true);
        RefreshEdgeModal();
    }

    static string EdgeModeLabel(StationLayout.PlatformEdgeMode mode)
    {
        switch (mode)
        {
            case StationLayout.PlatformEdgeMode.Normal: return "乗降可";
            case StationLayout.PlatformEdgeMode.BoardOnly: return "乗車専用";
            case StationLayout.PlatformEdgeMode.AlightOnly: return "降車専用";
            case StationLayout.PlatformEdgeMode.Disabled: return "使用停止";
            default: return "?";
        }
    }

    void RefreshEdgeModal()
    {
        for (int i = edgeRows.childCount - 1; i >= 0; i--)
            Destroy(edgeRows.GetChild(i).gameObject);
        if (infoStation == null) return;
        int index = 0;
        foreach (var edge in infoStation.PlatformEdges)
        {
            int trackIndex = edge.trackIndex;
            int side = edge.side;
            var row = FlowRow("Edge" + index++, edgeRows, 58f);
            int platform = infoStation.PlatformNumberOf(edge.trackIndex);
            FlowLabel("Name", row, platform + "番線／" + (edge.platformIndex + 1) + "番ホーム",
                19, 58f);
            FlowButton("Mode", row, EdgeModeLabel(edge.mode), 19, 58f,
                () => CycleEdgeMode(trackIndex, side), BtnBlue, 170f, 0f);
        }
        Canvas.ForceUpdateCanvases();
    }

    void CycleEdgeMode(int trackIndex, int side)
    {
        if (infoStation == null) return;
        var current = StationLayout.PlatformEdgeMode.Normal;
        foreach (var edge in infoStation.PlatformEdges)
            if (edge.trackIndex == trackIndex && edge.side == side)
            {
                current = edge.mode;
                break;
            }
        var next = (StationLayout.PlatformEdgeMode)(((int)current + 1) % 4);
        infoStation.SetPlatformEdgeMode(trackIndex, side, next);
        SaveLoad.Save();
        RefreshEdgeModal();
    }

    void BuildSettingsModal()
    {
        var overlay = Panel("SettingsModal", transform, Vector2.zero, Vector2.one,
            Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.68f));
        settingsPanel = overlay.gameObject;
        var box = Panel("Box", overlay.transform, new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, PanelBg);
        box.rectTransform.sizeDelta = new Vector2(650f, 430f);
        Label("Title", box.transform, "設定", 31, new Vector2(0f, 1f), Vector2.one,
            new Vector2(24f, -68f), new Vector2(-24f, -16f), TextAnchor.MiddleLeft);
        var help = Label("Help", box.transform,
            "操作\n地図移動：ドラッグ　拡大縮小：ピンチ／ホイール／±\n画面回転：右側の「回転」　全駅表示：「全体」",
            21, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -202f),
            new Vector2(-24f, -82f), TextAnchor.UpperLeft);
        help.color = MutedTxt;
        Btn("Reset", box.transform, "すべてのセーブデータを初期化", 23,
            new Vector2(0.06f, 0f), new Vector2(0.94f, 0f),
            new Vector2(0f, 114f), new Vector2(0f, 176f), ConfirmReset, Danger);
        Btn("Close", box.transform, "閉じる", 24, new Vector2(0.28f, 0f),
            new Vector2(0.72f, 0f), new Vector2(0f, 28f),
            new Vector2(0f, 88f), () => settingsPanel.SetActive(false));
        settingsPanel.SetActive(false);
    }

    void ConfirmReset()
    {
        settingsPanel.SetActive(false);
        ShowConfirm("最初からやり直しますか？",
            "駅・線路・系統・列車・資金を含む、すべてのセーブデータを削除します。\n" +
            "この操作は取り消せません。",
            SaveLoad.ResetAll);
    }

    void BuildConfirmModal()
    {
        var overlay = Panel("ConfirmModal", transform, Vector2.zero, Vector2.one,
            Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.72f));
        confirmModal = overlay.gameObject;
        var box = Panel("Box", overlay.transform, new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, PanelBg);
        box.rectTransform.sizeDelta = new Vector2(680f, 420f);
        confirmTitle = Label("Title", box.transform, "", 30, new Vector2(0f, 1f),
            Vector2.one, new Vector2(26f, -72f), new Vector2(-26f, -16f),
            TextAnchor.MiddleLeft);
        confirmBody = Label("Body", box.transform, "", 22, new Vector2(0f, 1f),
            Vector2.one, new Vector2(26f, -238f), new Vector2(-26f, -88f),
            TextAnchor.UpperLeft);
        confirmBody.color = MutedTxt;
        Btn("Cancel", box.transform, "キャンセル", 24, new Vector2(0f, 0f),
            new Vector2(0.49f, 0f), new Vector2(26f, 28f),
            new Vector2(-5f, 98f), CloseConfirm);
        Btn("Confirm", box.transform, "実行する", 25, new Vector2(0.51f, 0f),
            Vector2.one, new Vector2(5f, 28f), new Vector2(-26f, 98f),
            RunConfirm, Danger);
        confirmModal.SetActive(false);
    }

    // 線路モードで既に繋がっている2駅を選んだときの撤去確認(BuildControllerから呼ぶ)
    public void ConfirmRemoveSegment(TrackSegment seg)
    {
        if (seg == null || seg.a == null || seg.b == null) return;
        ShowConfirm("この線路を撤去しますか？",
            seg.a.stationName + "〜" + seg.b.stationName + "の線路を撤去します。\n" +
            "この線路が無いと走れなくなる系統・列車も、あわせて払い戻して撤去されます。",
            () => BC.RemoveSegment(seg));
    }

    void ShowConfirm(string title, string body, Action action)
    {
        confirmTitle.text = title;
        confirmBody.text = body;
        pendingConfirm = action;
        confirmModal.SetActive(true);
    }

    void CloseConfirm()
    {
        pendingConfirm = null;
        confirmModal.SetActive(false);
    }

    void RunConfirm()
    {
        var action = pendingConfirm;
        pendingConfirm = null;
        confirmModal.SetActive(false);
        if (action != null) action();
    }

    // ---- トースト / 戻る ----

    void BuildToast()
    {
        var panel = Panel("Toast", safeRoot, new Vector2(0.05f, 0f),
            new Vector2(0.95f, 0f), Vector2.zero, Vector2.zero,
            new Color(0.02f, 0.03f, 0.05f, 0.92f));
        // トーストは通知専用。起動直後は次アクション案内と重なるため、表示中でも
        // 下にある建設ボタンや地図操作へ必ず入力を通す。
        panel.raycastTarget = false;
        var canvasGroup = panel.gameObject.AddComponent<CanvasGroup>();
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        toastBg = panel.gameObject;
        toastRt = panel.rectTransform;
        toastText = Label("Text", panel.transform, "", 24, Vector2.zero, Vector2.one,
            new Vector2(14f, 6f), new Vector2(-14f, -6f), TextAnchor.MiddleCenter);
        toastText.raycastTarget = false;
        toastBg.SetActive(false);
    }

    public static void Toast(string message)
    {
        if (I == null)
        {
            Debug.Log("Toast: " + message);
            return;
        }
        I.toastText.text = message;
        I.toastBg.SetActive(true);
        I.toastUntil = Time.unscaledTime + 4.8f;
    }

    void HandleBack()
    {
        if (confirmModal.activeSelf) { CloseConfirm(); return; }
        if (edgePanel.activeSelf) { edgePanel.SetActive(false); return; }
        if (renameModal.activeSelf) { renameModal.SetActive(false); return; }
        if (settingsPanel.activeSelf) { settingsPanel.SetActive(false); return; }
        if (infoPanel.activeSelf) { HideStationInfo(); return; }
        if (rig != null && rig.cabTrain != null)
        {
            BC.SetMode(BuildController.Mode.View);
            return;
        }
        if (BC.mode == BuildController.Mode.Train &&
            BC.trainSub == BuildController.TrainSub.CreateLine)
        {
            BC.CancelCreateLine();
            return;
        }
        if (BC.mode != BuildController.Mode.View) BC.SetMode(BuildController.Mode.View);
    }

    // ---- モード / レスポンシブ配置 ----

    public void OnModeChanged()
    {
        if (BC == null) return;
        if (rig != null) rig.ExitCab();
        if (cabBtn != null) cabBtn.color = BtnBg;
        foreach (var kv in modeBtns)
            kv.Value.color = kv.Key == BC.mode ? BtnActive : BtnBg;
        trackPanel.SetActive(BC.mode == BuildController.Mode.Track);
        stationPanel.SetActive(BC.mode == BuildController.Mode.Station);
        trainPanel.SetActive(BC.mode == BuildController.Mode.Train);
        if (BC.mode == BuildController.Mode.Track)
        {
            RefreshTrackBedButtons();
            RefreshTrackSelection();
        }
        if (BC.mode == BuildController.Mode.Station) RefreshStationPanel();
        if (BC.mode == BuildController.Mode.Train) RefreshTrainPanel();
        if (BC.mode != BuildController.Mode.View) HideStationInfo();
        RefreshOnboarding();
        ApplyResponsiveLayout();
    }

    void ApplyResponsiveLayout()
    {
        if (safeRoot == null || Screen.width <= 0 || Screen.height <= 0) return;
        var safe = Screen.safeArea;
        safeRoot.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
        safeRoot.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
        safeRoot.offsetMin = Vector2.zero;
        safeRoot.offsetMax = Vector2.zero;
        Canvas.ForceUpdateCanvases();

        if (edgeBoxRt != null && safeRoot.rect.width > 0f && safeRoot.rect.height > 0f)
        {
            const float modalMargin = 32f;
            edgeBoxRt.sizeDelta = new Vector2(
                Mathf.Min(700f, Mathf.Max(0f, safeRoot.rect.width - modalMargin)),
                Mathf.Min(920f, Mathf.Max(0f, safeRoot.rect.height - modalMargin)));
        }

        bool portrait = IsPortrait;
        float top = portrait ? PortraitTopHeight : LandscapeTopHeight;
        float bottom = portrait ? PortraitToolbarHeight : LandscapeToolbarHeight;
        SetBar(topBarRt, true, top);
        SetBar(toolbarRt, false, bottom);
        SetSheet(trackPanelRt, portrait, 330f, 410f, false, top, bottom);
        SetSheet(stationPanelRt, portrait, 760f, 430f, false, top, bottom);
        SetSheet(trainPanelRt, portrait, 1110f, 490f, true, top, bottom);
        SetSheet(infoPanelRt, portrait, 570f, 520f, true, top, bottom);

        toastRt.anchorMin = new Vector2(0.05f, 0f);
        toastRt.anchorMax = new Vector2(0.95f, 0f);
        toastRt.pivot = new Vector2(0.5f, 0f);
        toastRt.sizeDelta = new Vector2(0f, 82f);

        onboardingRt.anchorMin = new Vector2(0.06f, 0f);
        onboardingRt.anchorMax = new Vector2(0.94f, 0f);
        onboardingRt.pivot = new Vector2(0.5f, 0f);
        onboardingRt.anchoredPosition = new Vector2(0f, bottom + 12f);
        onboardingRt.sizeDelta = new Vector2(0f, 220f);
        PositionToast(bottom);

        bool placeLeft = !portrait && (BC != null && BC.mode == BuildController.Mode.Train ||
            infoPanel != null && infoPanel.activeSelf);
        cameraToolsRt.anchorMin = cameraToolsRt.anchorMax =
            new Vector2(placeLeft ? 0f : 1f, 0.5f);
        cameraToolsRt.pivot = new Vector2(placeLeft ? 0f : 1f, 0.5f);
        cameraToolsRt.sizeDelta = new Vector2(88f, 350f);
        cameraToolsRt.anchoredPosition = new Vector2(placeLeft ? 10f : -10f,
            (bottom - top) * 0.18f);

        lastScreenSize = new Vector2Int(Screen.width, Screen.height);
        lastSafeArea = safe;
    }

    void PositionToast(float bottom)
    {
        if (toastRt == null) return;
        float y = bottom + 10f;
        if (onboardingPanel != null && onboardingPanel.activeSelf && onboardingRt != null)
            y = onboardingRt.anchoredPosition.y + onboardingRt.rect.height + 10f;
        toastRt.anchoredPosition = new Vector2(0f, y);
    }

    static void SetBar(RectTransform rt, bool top, float height)
    {
        rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
        rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
        rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
        rt.anchoredPosition = new Vector2(0f, top ? -6f : 6f);
        rt.sizeDelta = new Vector2(-12f, height - 6f);
    }

    static void SetSheet(RectTransform rt, bool portrait, float portraitHeight,
        float landscapeWidth, bool right, float top, float bottom)
    {
        if (portrait)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, bottom + 10f);
            rt.sizeDelta = new Vector2(-20f, portraitHeight);
        }
        else
        {
            float x = right ? 1f : 0f;
            rt.anchorMin = new Vector2(x, 0f);
            rt.anchorMax = new Vector2(x, 1f);
            rt.pivot = new Vector2(x, 0.5f);
            rt.anchoredPosition = new Vector2(right ? -10f : 10f, (bottom - top) * 0.5f);
            rt.sizeDelta = new Vector2(landscapeWidth, -(top + bottom + 20f));
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) HandleBack();
        if (lastScreenSize.x != Screen.width || lastScreenSize.y != Screen.height ||
            lastSafeArea != Screen.safeArea)
            ApplyResponsiveLayout();

        moneyText.text = "資金　" + GameState.MoneyLabel;
        clockText.text = rig != null && rig.cabTrain != null
            ? GameState.ClockLabel + "　" + rig.cabTrain.SpeedKmh.ToString("F0") + "km/h"
            : GameState.ClockLabel;
        carriedText.text = "輸送　" + GameState.carried + "人";
        if (stationPanel.activeSelf) RefreshStationPanel();
        if (toastBg.activeSelf && Time.unscaledTime > toastUntil) toastBg.SetActive(false);

        if (Time.unscaledTime >= nextSlowRefresh)
        {
            nextSlowRefresh = Time.unscaledTime + 0.25f;
            RefreshSpeedButtons();
            RefreshTrackSelection();
            RefreshInfoText();
            RefreshOnboarding();
        }
    }
}
