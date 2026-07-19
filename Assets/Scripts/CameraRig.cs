using UnityEngine;
using UnityEngine.EventSystems;

// 見下ろしカメラ。1本指ドラッグ=パン、ピンチ=ズーム、タップ=BuildControllerへ通知。
// マウス: 左ドラッグ=パン、ホイール=ズーム、クリック=タップ扱い
public class CameraRig : MonoBehaviour
{
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

    const float MinDist = 60f, MaxDist = 3200f, Limit = 1950f;

    public Camera Cam => cam;

    public void Setup()
    {
        cam = gameObject.GetComponent<Camera>();
        if (cam == null) cam = gameObject.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.68f, 0.81f, 0.93f);
        cam.farClipPlane = 9000f;
        cam.nearClipPlane = 1f;
        gameObject.tag = "MainCamera";
        Apply();
    }

    void Update()
    {
        if (Input.touchCount > 0) HandleTouch();
        else HandleMouse();
        Apply();
    }

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

    void Apply()
    {
        var rot = Quaternion.Euler(pitch, yaw, 0);
        transform.SetPositionAndRotation(target + rot * new Vector3(0, 0, -distance), rot);
    }
}
