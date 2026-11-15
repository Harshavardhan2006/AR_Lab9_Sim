using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

[RequireComponent(typeof(ARRaycastManager))]
[RequireComponent(typeof(ARPlaneManager))]
public class ARInteractionController : MonoBehaviour
{
    [Header("Objects the user can place")]
    [SerializeField] private GameObject[] prefabs;

    [Header("UI (optional)")]
    [SerializeField] private TMP_Text statusText;

    [Header("Gesture settings")]
    [SerializeField] private float tapMaxMovePixels = 25f;
    [SerializeField] private float minScale = 0.05f;
    [SerializeField] private float maxScale = 0.6f;

    private ARRaycastManager raycastManager;
    private ARPlaneManager planeManager;
    private ARPointCloudManager pointCloudManager;
    private Camera cam;

    private readonly List<ARRaycastHit> arHits = new List<ARRaycastHit>();
    private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    private readonly List<PlacedObject> placed = new List<PlacedObject>();

    private PlacedObject selected;
    private PlacedObject touchedObject;
    private int prefabIndex;
    private bool planesVisible = true;

    private Vector2 startPos;
    private bool startedOverUI, moved, dragging, wasSelected, ignoreSingle;

    private bool mouseDragging;

    void Awake()
    {
        raycastManager = GetComponent<ARRaycastManager>();
        planeManager = GetComponent<ARPlaneManager>();
        pointCloudManager = GetComponent<ARPointCloudManager>();
        cam = Camera.main;
    }

    void OnEnable()
    {
        EnhancedTouchSupport.Enable();
#if UNITY_EDITOR
        TouchSimulation.Enable();   // mouse acts like a finger in the Editor
#endif
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        TouchSimulation.Disable();
#endif
        EnhancedTouchSupport.Disable();
    }

    void Update()
    {

        if (cam == null) cam = Camera.main;

        int count = Touch.activeTouches.Count;
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) Debug.Log("click, touches=" + count);
        if (count == 1) HandleOneFinger(Touch.activeTouches[0]);
        else if (count >= 2) HandleTwoFingers(Touch.activeTouches[0], Touch.activeTouches[1]);
        else { dragging = false; ignoreSingle = false; }

#if UNITY_EDITOR
        HandleEditorMouse();
        HandleEditorExtras();
#endif
        if (!planesVisible)
        {
            planeManager.SetTrackablesActive(false);
            if (pointCloudManager != null) pointCloudManager.SetTrackablesActive(false);
        }
        UpdateStatus();
    }

    // ---------- ONE FINGER: tap, select, drag ----------
    private void HandleOneFinger(Touch t)
    {
        if (ignoreSingle) return;
        Vector2 pos = t.screenPosition;

        switch (t.phase)
        {
            case TouchPhase.Began:
                startPos = pos;
                moved = false;
                dragging = false;
                startedOverUI = IsOverUI(pos);
                Debug.Log("began " + pos + " overUI=" + startedOverUI);
                if (startedOverUI) return;

                touchedObject = FindPlacedObject(pos);
                wasSelected = touchedObject != null && touchedObject == selected;
                if (touchedObject != null)
                {
                    Select(touchedObject);
                    dragging = true;
                }
                break;

            case TouchPhase.Moved:
            case TouchPhase.Stationary:
                if (startedOverUI) return;
                if (Vector2.Distance(pos, startPos) > tapMaxMovePixels) moved = true;
                if (dragging && moved) MoveSelected(pos);
                break;

            case TouchPhase.Ended:
            case TouchPhase.Canceled:
                if (startedOverUI) return;
                if (!moved) OnTap(pos);
                dragging = false;
                break;
        }
    }

    private void OnTap(Vector2 pos)
    {
        Debug.Log("tap hit=" + raycastManager.Raycast(pos, arHits, TrackableType.PlaneWithinPolygon) + " planes=" + planeManager.trackables.count + " touchedObj=" + (touchedObject != null));
        if (touchedObject != null)
        {
            // Tapping an already selected object changes its colour
            if (wasSelected) touchedObject.CycleColor();
            return;
        }
        // Tapped empty space: place a new object if a plane is under the finger
        if (raycastManager.Raycast(pos, arHits, TrackableType.PlaneWithinPolygon))
            PlaceObject(arHits[0].pose.position);
        else
            Deselect();
    }

    // ---------- TWO FINGERS: pinch to scale, twist to rotate ----------
    private void HandleTwoFingers(Touch a, Touch b)
    {
        ignoreSingle = true;
        dragging = false;
        if (selected == null) return;

        Vector2 a1 = a.screenPosition, b1 = b.screenPosition;
        Vector2 a0 = a1 - a.delta, b0 = b1 - b.delta;

        float prevDist = Vector2.Distance(a0, b0);
        float dist = Vector2.Distance(a1, b1);
        if (prevDist > 10f) ScaleSelected(dist / prevDist);

        float prevAngle = Mathf.Atan2(b0.y - a0.y, b0.x - a0.x) * Mathf.Rad2Deg;
        float angle = Mathf.Atan2(b1.y - a1.y, b1.x - a1.x) * Mathf.Rad2Deg;
        float deltaAngle = Mathf.DeltaAngle(prevAngle, angle);
        selected.transform.Rotate(0f, -deltaAngle, 0f, Space.World);
    }

    // ---------- Helpers ----------
    private void PlaceObject(Vector3 point)
    {
        if (prefabs == null || prefabs.Length == 0) return;
        GameObject go = Instantiate(prefabs[prefabIndex], point, Quaternion.identity);
        if (!go.TryGetComponent(out PlacedObject po)) po = go.AddComponent<PlacedObject>();
        po.PlaceAt(point);
        placed.Add(po);
        Select(po);
    }

    private void MoveSelected(Vector2 screenPos)
    {
        if (selected != null && raycastManager.Raycast(screenPos, arHits, TrackableType.PlaneWithinPolygon))
            selected.PlaceAt(arHits[0].pose.position);
    }

    private void ScaleSelected(float factor)
    {
        if (selected == null) return;
        float s = Mathf.Clamp(selected.transform.localScale.x * factor, minScale, maxScale);
        selected.transform.localScale = Vector3.one * s;
        selected.Refresh();
    }

    private PlacedObject FindPlacedObject(Vector2 screenPos)
    {
        if (cam == null) return null;
        Ray ray = cam.ScreenPointToRay(screenPos);
        RaycastHit[] hits = Physics.RaycastAll(ray, 20f);
        PlacedObject best = null;
        float bestDist = float.MaxValue;
        foreach (RaycastHit h in hits)
        {
            PlacedObject po = h.collider.GetComponentInParent<PlacedObject>();
            if (po != null && h.distance < bestDist) { best = po; bestDist = h.distance; }
        }
        return best;
    }

    private bool IsOverUI(Vector2 screenPos)
    {
        if (EventSystem.current == null) return false;
        var data = new PointerEventData(EventSystem.current) { position = screenPos };
        uiHits.Clear();
        EventSystem.current.RaycastAll(data, uiHits);
        return uiHits.Count > 0;
    }

    private void Select(PlacedObject po)
    {
        if (selected != null && selected != po) selected.SetSelected(false);
        selected = po;
        selected.SetSelected(true);
    }

    private void Deselect()
    {
        if (selected != null) selected.SetSelected(false);
        selected = null;
    }

    private void UpdateStatus()
    {
        if (statusText == null) return;
        string shape = (prefabs != null && prefabs.Length > 0) ? prefabs[prefabIndex].name : "none";
        statusText.text =
            "Planes: " + planeManager.trackables.count + "   Objects: " + placed.Count + "\n" +
            "Shape: " + shape + "   Selected: " + (selected != null ? "Yes" : "None");
    }

    // ---------- Called by the UI buttons ----------
    public void SelectPrefab(int index)
    {
        if (prefabs != null && index >= 0 && index < prefabs.Length) prefabIndex = index;
    }

    public void DeleteSelected()
    {
        if (selected == null) return;
        placed.Remove(selected);
        Destroy(selected.gameObject);
        selected = null;
    }

    public void ClearAll()
    {
        foreach (PlacedObject po in placed) if (po != null) Destroy(po.gameObject);
        placed.Clear();
        selected = null;
    }

    public void TogglePlanes()
    {
        planesVisible = !planesVisible;
        planeManager.SetTrackablesActive(planesVisible);
        if (pointCloudManager != null) pointCloudManager.SetTrackablesActive(planesVisible);
    }

#if UNITY_EDITOR
    // Editor-only replacements for two-finger gestures

    private void HandleEditorMouse()
{
    if (Mouse.current == null || Touch.activeTouches.Count > 0) return;
    var left = Mouse.current.leftButton;
    Vector2 pos = Mouse.current.position.ReadValue();

    if (left.wasPressedThisFrame)
    {
        startPos = pos;
        moved = false;
        mouseDragging = false;
        startedOverUI = IsOverUI(pos);
        if (startedOverUI) return;
        touchedObject = FindPlacedObject(pos);
        wasSelected = touchedObject != null && touchedObject == selected;
        if (touchedObject != null)
        {
            Select(touchedObject);
            mouseDragging = true;
        }
    }
    else if (left.isPressed)
    {
        if (startedOverUI) return;
        if (Vector2.Distance(pos, startPos) > tapMaxMovePixels) moved = true;
        if (mouseDragging && moved) MoveSelected(pos);
    }
    else if (left.wasReleasedThisFrame)
    {
        if (!startedOverUI && !moved) OnTap(pos);
        mouseDragging = false;
    }
}

    private void HandleEditorExtras()
    {
        if (selected == null) return;

        if (Mouse.current != null)
        {
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f) ScaleSelected(1f + Mathf.Sign(scroll) * 0.08f);
        }
        if (Keyboard.current != null)
        {
            float dir = 0f;
            if (Keyboard.current.leftArrowKey.isPressed) dir += 1f;
            if (Keyboard.current.rightArrowKey.isPressed) dir -= 1f;
            if (dir != 0f) selected.transform.Rotate(0f, dir * 90f * Time.deltaTime, 0f, Space.World);
        }
    }
#endif
}
