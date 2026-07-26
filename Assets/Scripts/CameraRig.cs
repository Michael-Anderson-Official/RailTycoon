using UnityEngine;
using UnityEngine.EventSystems;

// 見下ろしカメラ。1本指ドラッグ=パン、ピンチ=ズーム、タップ=BuildControllerへ通知。
// マウス: 左ドラッグ=パン、ホイール=ズーム、クリック=タップ扱い
public class CameraRig : MonoBehaviour
{
    // 車窓モードかどうかをTrain側(ドアモニター)から見るため
    public static CameraRig I;
    void Awake() => I = this;

    public Vector3 target = Vector3.zero;
    public float distance = 600f;
    public float pitch = 52f;
    public float yaw = 0f;

    Camera cam;
    Vector2 downPos;
    float downTime;
    bool dragging;
    bool touchUi;
    float lastPinch = -1;

    // 縦画面で約4km四方を横幅に収めるには、狭い水平FOVのため3.2km以上離れる。
    // far clipも同じ用途に合わせ、全体表示後に端の駅が欠けない範囲を確保する。
    const float MinDist = 60f, MaxDist = 12000f, Limit = 1950f;
    public const float NetworkFrameFill = 0.72f;

    public Camera Cam => cam;

    // 前面展望モード(nullで通常視点)
    public Train cabTrain;
    Quaternion cabRot = Quaternion.identity;

    // 地図視点は18kmまで映すのでニアクリップが1m。運転台とモニターは目線から
    // 0.5〜1.1mの距離にあるため、そのままだと**まるごと切り取られて見えない**。
    // 車窓の間だけ手前まで映すようにする(2026-07-27にユーザーが実機で発見)
    const float MapNearClip = 1f;
    const float CabNearClip = 0.05f;

    public void EnterCab(Train t)
    {
        if (cabTrain != null && cabTrain != t) cabTrain.SetWindscreenVisible(true);
        cabTrain = t;
        t.CabPose(out var p, out var f);
        cabRot = Quaternion.LookRotation(f, Vector3.up);
        // 窓ガラスだけ外して「窓越し」にする(枠・ピラーは残す)
        t.SetWindscreenVisible(false);
        if (cam != null) cam.nearClipPlane = CabNearClip;
    }

    public void ExitCab()
    {
        if (cabTrain != null) cabTrain.SetWindscreenVisible(true);
        cabTrain = null;
        if (cam != null) cam.nearClipPlane = MapNearClip;
    }

    public void Setup()
    {
        cam = gameObject.GetComponent<Camera>();
        if (cam == null) cam = gameObject.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.68f, 0.81f, 0.93f);
        cam.farClipPlane = 18000f;
        cam.nearClipPlane = MapNearClip;
        gameObject.tag = "MainCamera";
        Apply();
    }

    void Update()
    {
        if (cabTrain != null)
        {
            // 前面展望: 先頭車前端に追従(ポリラインの角で揺れないよう回転は補間)
            cabTrain.CabPose(out var p, out var f);
            var look = Quaternion.LookRotation((f + Vector3.down * 0.06f).normalized, Vector3.up);
            cabRot = Quaternion.Slerp(cabRot, look, 1f - Mathf.Exp(-6f * Time.deltaTime));
            transform.SetPositionAndRotation(p, cabRot);
            return;
        }
        if (Input.touchCount > 0) { HandleTouch(); lastTouchTime = Time.unscaledTime; }
        else HandleMouse();
        Apply();
    }

    float lastTouchTime = -10f;

    void HandleTouch()
    {
        if (Input.touchCount == 1)
        {
            var t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Began)
            {
                downPos = t.position;
                downTime = Time.unscaledTime;
                dragging = false;
                lastPinch = -1;
                touchUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(t.fingerId);
            }
            else if (t.phase == TouchPhase.Moved && !touchUi)
            {
                if (dragging || (t.position - downPos).magnitude > 24f)
                {
                    dragging = true;
                    Pan(t.deltaPosition);
                }
            }
            else if (t.phase == TouchPhase.Ended)
            {
                if (!dragging && !touchUi && Time.unscaledTime - downTime < 0.4f)
                    Tap(t.position);
            }
        }
        else if (Input.touchCount == 2)
        {
            dragging = true;
            var a = Input.GetTouch(0);
            var b = Input.GetTouch(1);
            float pinch = (a.position - b.position).magnitude;
            if (lastPinch > 0 && pinch > 1f)
                distance = Mathf.Clamp(distance * lastPinch / pinch, MinDist, MaxDist);
            lastPinch = pinch;
        }
        if (Input.touchCount != 2) lastPinch = -1;
    }

    void HandleMouse()
    {
        lastPinch = -1;
        // タッチ操作の直後は無視する。スマホのブラウザはtouchend後に遅れて
        // 合成マウスイベント(ゴーストクリック)を発火することがあり、これを本物の
        // クリックとして処理すると、実タッチで選択した直後に同じ座標へ「クリック」が
        // 飛んで同じ駅を二重タップした扱いになり(線路モードの「同じ駅を再タップで
        // 選択解除」等)、選択した瞬間に解除されたように見えてしまう
        if (Time.unscaledTime - lastTouchTime < 0.8f) return;
        if (Input.GetMouseButtonDown(0))
        {
            downPos = Input.mousePosition;
            downTime = Time.unscaledTime;
            dragging = false;
            touchUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }
        else if (Input.GetMouseButton(0) && !touchUi)
        {
            var cur = (Vector2)Input.mousePosition;
            if (dragging || (cur - downPos).magnitude > 8f)
            {
                if (dragging) Pan(cur - lastMouse);
                dragging = true;
            }
        }
        else if (Input.GetMouseButtonUp(0))
        {
            if (!dragging && !touchUi && Time.unscaledTime - downTime < 0.5f)
                Tap(Input.mousePosition);
        }
        lastMouse = Input.mousePosition;
        float wheel = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(wheel) > 0.001f)
            distance = Mathf.Clamp(distance * (1f - wheel * 0.4f), MinDist, MaxDist);
    }

    Vector2 lastMouse;

    void Pan(Vector2 deltaPx)
    {
        float k = distance * 0.0016f;
        var rot = Quaternion.Euler(0, yaw, 0);
        target -= rot * new Vector3(deltaPx.x * k, 0, deltaPx.y * k);
        target.x = Mathf.Clamp(target.x, -Limit, Limit);
        target.z = Mathf.Clamp(target.z, -Limit, Limit);
    }

    void Tap(Vector2 screenPos)
    {
        if (BuildController.Instance != null)
            BuildController.Instance.HandleTap(cam.ScreenPointToRay(screenPos));
    }

    public void RotateStep() => yaw = Mathf.Repeat(yaw + 45f, 360f);

    public void ZoomBy(float factor)
    {
        distance = Mathf.Clamp(distance * factor, MinDist, MaxDist);
    }

    public void FocusOn(Vector3 worldPosition, float preferredDistance = 220f)
    {
        target = new Vector3(worldPosition.x, 0f, worldPosition.z);
        distance = Mathf.Clamp(preferredDistance, MinDist, MaxDist);
    }

    // 指定した地上Boundsの全隅が、縦横FOVのNetworkFrameFill内へ入る距離を返す。
    // 各隅のcamera-space depthも含めるため、俯角やyawで手前側だけ切れることもない。
    public static float RequiredFrameDistance(Bounds bounds, float cameraPitch, float cameraYaw,
        float verticalFov, float aspect, float viewportFill = NetworkFrameFill)
    {
        aspect = Mathf.Max(0.1f, aspect);
        viewportFill = Mathf.Clamp(viewportFill, 0.1f, 0.95f);
        float tanV = Mathf.Tan(Mathf.Clamp(verticalFov, 10f, 150f) * 0.5f * Mathf.Deg2Rad);
        float tanH = tanV * aspect;
        var rotation = Quaternion.Euler(cameraPitch, cameraYaw, 0f);
        Vector3 right = rotation * Vector3.right;
        Vector3 up = rotation * Vector3.up;
        Vector3 forward = rotation * Vector3.forward;
        Vector3 ext = bounds.extents;
        float required = 0f;

        for (int ix = -1; ix <= 1; ix += 2)
            for (int iy = -1; iy <= 1; iy += 2)
                for (int iz = -1; iz <= 1; iz += 2)
                {
                    Vector3 rel = Vector3.Scale(ext, new Vector3(ix, iy, iz));
                    float depthFromTarget = Vector3.Dot(rel, forward);
                    float horizontal = Mathf.Abs(Vector3.Dot(rel, right));
                    float vertical = Mathf.Abs(Vector3.Dot(rel, up));
                    required = Mathf.Max(required,
                        horizontal / (tanH * viewportFill) - depthFromTarget,
                        vertical / (tanV * viewportFill) - depthFromTarget);
                }
        return Mathf.Max(0f, required);
    }

    // 建設済みの全駅が画面へ収まる位置へ戻す。駅が無い時は初期視点。
    public void FrameNetwork()
    {
        if (TrackNetwork.stations.Count == 0)
        {
            target = Vector3.zero;
            distance = 600f;
            return;
        }

        var first = TrackNetwork.stations[0];
        var bounds = new Bounds(first.transform.position, Vector3.zero);
        foreach (var st in TrackNetwork.stations)
        {
            Vector3 along = st.Axis * (st.HalfLen + StationLayout.ThroatLen);
            float halfWidth = st.layout.trackOffsets != null
                ? st.layout.totalWidth * 0.5f + 8f : 18f;
            Vector3 across = st.transform.right * halfWidth;
            bounds.Encapsulate(st.transform.position + along + across);
            bounds.Encapsulate(st.transform.position + along - across);
            bounds.Encapsulate(st.transform.position - along + across);
            bounds.Encapsulate(st.transform.position - along - across);
        }
        // 曲線線路のふくらみと画面端の視覚余白。
        bounds.Expand(new Vector3(60f, 0f, 60f));
        target = bounds.center;
        target.y = 0f;
        float aspect = cam != null && cam.aspect > 0.01f
            ? cam.aspect : Mathf.Max(0.1f, (float)Screen.width / Mathf.Max(1, Screen.height));
        float fov = cam != null ? cam.fieldOfView : 60f;
        distance = Mathf.Clamp(Mathf.Max(180f,
            RequiredFrameDistance(bounds, pitch, yaw, fov, aspect)), MinDist, MaxDist);
    }

    void Apply()
    {
        var rot = Quaternion.Euler(pitch, yaw, 0);
        transform.SetPositionAndRotation(target + rot * new Vector3(0, 0, -distance), rot);
    }
}
