using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// uGUIを全てコードで組む。モードごとのパネル表示切替とHUD更新
public class UIController : MonoBehaviour
{
    public static UIController I;
    public CameraRig rig;

    Text moneyText, clockText, carriedText, toastText, costText, routeText, infoText, fmInfoText;
    Text carsVal, facesVal, linesVal;
    GameObject stationPanel, trainPanel, infoPanel, toastBg;
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
                new Vector2(0.45f + 0.13f * i, 0.06f), new Vector2(0.57f + 0.13f * i, 0.48f),
                Vector2.zero, Vector2.zero, () => SetSpeed(sp));
            speedBtns.Add(new KeyValuePair<float, Image>(sp, b));
        }
        Btn("Rot", bar.transform, "視点⟳", 24, new Vector2(0.85f, 0.06f), new Vector2(0.99f, 0.48f),
            Vector2.zero, Vector2.zero, () => { if (rig != null) rig.RotateStep(); });
        SetSpeed(GameState.timeScale);
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
            var img = Btn("M" + names[i], bar.transform, names[i], 32,
                new Vector2(i * 0.25f + 0.006f, 0.08f), new Vector2((i + 1) * 0.25f - 0.006f, 0.92f),
                Vector2.zero, Vector2.zero, () => BC.SetMode(m));
            modeBtns[m] = img;
        }
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
        Label("Title", p.transform, "駅を建設", 30, new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, y - 44), new Vector2(-14, y), TextAnchor.MiddleLeft);
        y -= 56;
        carsVal = ParamRow(p.transform, "対応両数", ref y, () => ChangeCars(-1), () => ChangeCars(1));
        facesVal = ParamRow(p.transform, "ホーム(面)", ref y, () => ChangeFaces(-1), () => ChangeFaces(1));
        linesVal = ParamRow(p.transform, "番線(線)", ref y, () => ChangeLines(-1), () => ChangeLines(1));
        Btn("Yaw", p.transform, "向きを45°回転", 26, new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, y - 56), new Vector2(-14, y), RotatePreview);
        y -= 68;
        costText = Label("Cost", p.transform, "", 26, new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, y - 44), new Vector2(-14, y), TextAnchor.MiddleLeft);
        y -= 52;
        Btn("Confirm", p.transform, "ここに建設", 30, new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, y - 66), new Vector2(-14, y), () => BC.ConfirmStation(), BtnActive);
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
        rt.sizeDelta = new Vector2(350, 850);
        trainPanel = p.gameObject;
        float y = -14;
        Label("Title", p.transform, "列車を購入", 30, new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, y - 44), new Vector2(-14, y), TextAnchor.MiddleLeft);
        y -= 54;
        foreach (var fm in TrainCatalog.Formations)
        {
            var f = fm;
            var img = Btn("F" + f.Label, p.transform, f.Label + "  " + (f.CostYen / 1e8).ToString("F0") + "億円", 25,
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, y - 58), new Vector2(-14, y), () => SelectFormation(f));
            fmBtns.Add(new KeyValuePair<TrainCatalog.Formation, Image>(f, img));
            y -= 66;
        }
        fmInfoText = Label("FmInfo", p.transform, "編成を選択してください", 22, new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, y - 60), new Vector2(-14, y), TextAnchor.UpperLeft);
        y -= 70;
        routeText = Label("Route", p.transform, "", 22, new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, y - 90), new Vector2(-14, y), TextAnchor.UpperLeft);
        y -= 100;
        Btn("Launch", p.transform, "発車!", 30, new Vector2(0, 1), new Vector2(0.55f, 1), new Vector2(14, y - 66), new Vector2(0, y), () => BC.LaunchTrain(), BtnActive);
        Btn("ClearR", p.transform, "経路クリア", 24, new Vector2(0.58f, 1), new Vector2(1, 1), new Vector2(0, y - 66), new Vector2(-14, y), () => BC.ClearRoute());
    }

    void SelectFormation(TrainCatalog.Formation f)
    {
        BC.selFormation = f;
        foreach (var kv in fmBtns) kv.Value.color = kv.Key == f ? BtnActive : BtnBg;
        fmInfoText.text = f.Label + ": 定員" + f.Capacity + "人 / 最高" + f.type.maxSpeedKmh + "km/h\n停車駅は" + f.cars + "両以上対応が必要";
        UpdateRouteLabel();
    }

    public void UpdateRouteLabel()
    {
        if (routeText == null) return;
        if (BC.routeSel.Count == 0)
        {
            routeText.text = "駅を順にタップして経路を作成";
            return;
        }
        var names = new List<string>();
        foreach (var s in BC.routeSel) names.Add(s.stationName);
        routeText.text = "経路: " + string.Join(" → ", names.ToArray());
    }

    void BuildInfoPanel()
    {
        var p = Panel("InfoPanel", transform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, Vector2.zero, PanelBg);
        var rt = p.rectTransform;
        rt.pivot = new Vector2(0.5f, 1);
        rt.anchoredPosition = new Vector2(0, -120);
        rt.sizeDelta = new Vector2(480, 250);
        infoPanel = p.gameObject;
        infoText = Label("Info", p.transform, "", 25, Vector2.zero, Vector2.one, new Vector2(16, 60), new Vector2(-16, -12), TextAnchor.UpperLeft);
        Btn("Close", p.transform, "閉じる", 24, new Vector2(0.3f, 0), new Vector2(0.7f, 0), new Vector2(0, 8), new Vector2(0, 54), HideStationInfo);
        infoPanel.SetActive(false);
    }

    void BuildToast()
    {
        var p = Panel("Toast", transform, new Vector2(0.05f, 0), new Vector2(0.95f, 0), new Vector2(0, 104), new Vector2(0, 168), new Color(0, 0, 0, 0.65f));
        toastBg = p.gameObject;
        toastText = Label("T", p.transform, "", 25, Vector2.zero, Vector2.one, new Vector2(12, 4), new Vector2(-12, -4), TextAnchor.MiddleCenter);
        toastBg.SetActive(false);
    }

    public static void Toast(string msg)
    {
        if (I == null) { Debug.Log("Toast: " + msg); return; }
        I.toastText.text = msg;
        I.toastBg.SetActive(true);
        I.toastUntil = Time.unscaledTime + 3f;
    }

    public void ShowStationInfo(Station st)
    {
        infoPanel.SetActive(true);
        infoText.text = st.stationName + "  (" + st.cars + "両対応・" + st.faces + "面" + st.lines + "線)\n"
            + "待ち客: " + st.TotalWaiting + "人 / 上限" + st.WaitingCap + "\n"
            + "発展レベル: " + st.DevLevel + "\n"
            + "接続駅: " + TrackNetwork.Reachable(st).Count + "駅";
    }

    public void HideStationInfo() => infoPanel.SetActive(false);

    public void OnModeChanged()
    {
        var mode = BC.mode;
        foreach (var kv in modeBtns) kv.Value.color = kv.Key == mode ? BtnActive : BtnBg;
        stationPanel.SetActive(mode == BuildController.Mode.Station);
        trainPanel.SetActive(mode == BuildController.Mode.Train);
        if (mode != BuildController.Mode.View) HideStationInfo();
        UpdateRouteLabel();
    }

    void Update()
    {
        moneyText.text = "資金 " + GameState.MoneyLabel;
        clockText.text = GameState.ClockLabel;
        carriedText.text = "輸送人員 " + GameState.carried + "人";
        if (stationPanel.activeSelf)
        {
            carsVal.text = BC.pCars + "両";
            facesVal.text = BC.pFaces + "面";
            linesVal.text = BC.pLines + "線";
            costText.text = "建設費 " + (GameState.StationCost(BC.pCars, BC.pFaces, BC.pLines) / 1e8).ToString("F1") + "億円";
        }
        if (toastBg.activeSelf && Time.unscaledTime > toastUntil) toastBg.SetActive(false);
    }
}
