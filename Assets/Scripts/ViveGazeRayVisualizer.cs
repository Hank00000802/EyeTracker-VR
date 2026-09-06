using UnityEngine;
using VIVE.OpenXR;
using VIVE.OpenXR.EyeTracker;

[RequireComponent(typeof(LineRenderer))]
public class ViveGazeRayVisualizer : MonoBehaviour
{
    [Header("XR References")]
    [Tooltip("指定 XR Origin/Camera Offset，用來把 gaze direction 轉成世界座標")]
    [SerializeField] private Transform trackingSpace;

    [Tooltip("指定 XR Origin/Camera Offset/Main Camera，射線固定從此處發出")]
    [SerializeField] private Transform centerOrigin;

    [Header("Raycast")]
    [SerializeField, Min(0.1f)]
    private float maxDistance = 10f;

    [SerializeField]
    private LayerMask hitLayers = ~0;

    [Header("Visual")]
    [Tooltip("沿 Main Camera forward 固定前移的顯示起點距離（與視線方向無關，避免左右眼資料帶動起點橫移）")]
    [SerializeField, Min(0f)]
    private float visualStartOffset = 0.1f;

    [SerializeField, Min(0.001f)]
    private float lineWidth = 0.005f;

    [SerializeField]
    private Transform hitMarker;

    [Header("Visual Smoothing")]
    [Tooltip("只平滑畫面上的射線；不應套用到研究用 raw data")]
    [SerializeField]
    private bool smoothVisualization = true;

    [SerializeField, Range(1f, 40f)]
    private float smoothingSpeed = 18f;

    [Tooltip("單眼 isValid 短暫掉線時，仍沿用該眼最後方向多久（秒），避免左右跳動")]
    [SerializeField, Min(0f)]
    private float eyeValidityHoldSeconds = 0.08f;

    [Header("Debug")]
    [SerializeField]
    private bool logHitChanges = true;

    /// <summary>目前是否有可用的合併視線方向（至少一眼有效，或 hold 期間）。</summary>
    public bool CombinedValid { get; private set; }

    /// <summary>永遠為 Main Camera（centerOrigin）世界座標，不用左右眼 position。</summary>
    public Vector3 CombinedOrigin { get; private set; }

    /// <summary>未經視覺平滑的合併方向，可供 CSV／研究紀錄使用。</summary>
    public Vector3 CombinedDirection { get; private set; }

    public GameObject CurrentHitObject { get; private set; }
    public Vector3 CurrentHitPoint { get; private set; }

    private LineRenderer lineRenderer;
    private GameObject previousHitObject;

    private Vector3 displayedDirection = Vector3.forward;
    private bool hasDisplayedDirection;

    private Vector3 lastLeftDirection = Vector3.forward;
    private Vector3 lastRightDirection = Vector3.forward;
    private float lastLeftValidTime = float.NegativeInfinity;
    private float lastRightValidTime = float.NegativeInfinity;

    private bool visualizationEnabled;

    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();

        lineRenderer.positionCount = 2;
        lineRenderer.useWorldSpace = true;
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.enabled = false;

        visualizationEnabled = false;
        ApplyVisualizationState();
    }

    /// <summary>
    /// UI 按鈕用：切換視線射線與 Hit Marker 顯示（不影響 CSV 錄製）。
    /// </summary>
    public void ToggleEyeTrackingVisualization()
    {
        visualizationEnabled = !visualizationEnabled;
        ApplyVisualizationState();
    }

    private void ApplyVisualizationState()
    {
        enabled = visualizationEnabled;

        if (visualizationEnabled)
            return;

        if (lineRenderer != null)
            lineRenderer.enabled = false;

        if (hitMarker != null)
            hitMarker.gameObject.SetActive(false);

        CombinedValid = false;
        CurrentHitObject = null;
        hasDisplayedDirection = false;
    }

    private void Update()
    {
        if (!visualizationEnabled)
        {
            HideGazeVisuals();
            return;
        }

        // 起點只綁 Main Camera，與左右眼 gaze pose／direction 完全隔離。
        if (centerOrigin == null)
        {
            HideGazeVisuals();
            return;
        }

        // Physics／研究用原點：永遠是 Camera 中心，不用任何眼動位置。
        Vector3 stableOrigin = centerOrigin.position;
        CombinedOrigin = stableOrigin;

        if (!TryGetCombinedDirection(out Vector3 rawDirection))
        {
            HideGazeVisuals();
            return;
        }

        CombinedValid = true;
        // 研究用 raw direction：不套用視覺平滑。
        CombinedDirection = rawDirection;

        // 眼動資料只影響方向；僅視覺射線／hit marker 使用平滑方向。
        Vector3 visualDirection = GetDisplayedDirection(rawDirection);

        Vector3 rayEnd = stableOrigin + visualDirection * maxDistance;

        bool hasHit = Physics.Raycast(
            stableOrigin,
            visualDirection,
            out RaycastHit hit,
            maxDistance,
            hitLayers,
            QueryTriggerInteraction.Ignore);

        if (hasHit)
        {
            CurrentHitObject = hit.collider.gameObject;
            CurrentHitPoint = hit.point;
            rayEnd = hit.point;

            if (hitMarker != null)
            {
                hitMarker.position = hit.point;
                SetMarkerVisible(true);
            }
        }
        else
        {
            CurrentHitObject = null;
            CurrentHitPoint = rayEnd;
            SetMarkerVisible(false);
        }

        // 顯示起點 = Camera 中心 + Camera.forward * offset。
        // 刻意不用 gaze direction 做前移，否則方向左右抖時起點也會橫移。
        Vector3 visualStart =
            stableOrigin + centerOrigin.forward * visualStartOffset;

        lineRenderer.enabled = true;
        lineRenderer.SetPosition(0, visualStart);
        lineRenderer.SetPosition(1, rayEnd);

        LogHitChangeIfNeeded(CurrentHitObject);
    }

    private void HideGazeVisuals()
    {
        CombinedValid = false;
        CurrentHitObject = null;
        hasDisplayedDirection = false;

        lineRenderer.enabled = false;
        SetMarkerVisible(false);
        LogHitChangeIfNeeded(null);
    }

    private bool TryGetCombinedDirection(out Vector3 combinedDirection)
    {
        combinedDirection = Vector3.forward;

        bool success =
            XR_HTC_eye_tracker.Interop.GetEyeGazeData(
                out XrSingleEyeGazeDataHTC[] gazes);

        if (!success || gazes == null || gazes.Length < 2)
            return false;

        int leftIndex =
            (int)XrEyePositionHTC.XR_EYE_POSITION_LEFT_HTC;

        int rightIndex =
            (int)XrEyePositionHTC.XR_EYE_POSITION_RIGHT_HTC;

        bool leftValidNow = TryReadEyeDirection(
            gazes[leftIndex],
            out Vector3 leftDirection);

        bool rightValidNow = TryReadEyeDirection(
            gazes[rightIndex],
            out Vector3 rightDirection);

        float now = Time.unscaledTime;

        if (leftValidNow)
        {
            lastLeftDirection = leftDirection;
            lastLeftValidTime = now;
        }

        if (rightValidNow)
        {
            lastRightDirection = rightDirection;
            lastRightValidTime = now;
        }

        // 短暫 invalid 時沿用最後方向，避免雙眼平均 ↔ 單眼之間左右跳。
        bool leftUsable =
            leftValidNow ||
            (now - lastLeftValidTime) <= eyeValidityHoldSeconds;

        bool rightUsable =
            rightValidNow ||
            (now - lastRightValidTime) <= eyeValidityHoldSeconds;

        if (leftUsable && rightUsable)
        {
            combinedDirection =
                (lastLeftDirection + lastRightDirection).normalized;

            return combinedDirection.sqrMagnitude > 0.001f;
        }

        // 僅單眼可用：仍只用該眼 direction，起點維持 Main Camera。
        if (leftUsable)
        {
            combinedDirection = lastLeftDirection;
            return true;
        }

        if (rightUsable)
        {
            combinedDirection = lastRightDirection;
            return true;
        }

        return false;
    }

    private bool TryReadEyeDirection(
        XrSingleEyeGazeDataHTC gaze,
        out Vector3 worldDirection)
    {
        worldDirection = Vector3.forward;

        if (!gaze.isValid)
            return false;

        Quaternion localRotation =
            gaze.gazePose.orientation.ToUnityQuaternion();

        Vector3 localDirection =
            (localRotation * Vector3.forward).normalized;

        // VIVE gaze orientation → tracking space local direction → Unity world。
        // 不要再乘上 Main Camera rotation，以免方向被套用兩次。
        worldDirection = trackingSpace != null
            ? trackingSpace.TransformDirection(localDirection).normalized
            : localDirection;

        return worldDirection.sqrMagnitude > 0.001f;
    }

    private Vector3 GetDisplayedDirection(Vector3 rawDirection)
    {
        if (!smoothVisualization)
        {
            displayedDirection = rawDirection;
            hasDisplayedDirection = true;
            return displayedDirection;
        }

        if (!hasDisplayedDirection)
        {
            displayedDirection = rawDirection;
            hasDisplayedDirection = true;
            return displayedDirection;
        }

        float interpolation =
            1f - Mathf.Exp(
                -smoothingSpeed * Time.unscaledDeltaTime);

        displayedDirection = Vector3.Slerp(
            displayedDirection,
            rawDirection,
            interpolation).normalized;

        return displayedDirection;
    }

    private void SetMarkerVisible(bool visible)
    {
        if (hitMarker != null &&
            hitMarker.gameObject.activeSelf != visible)
        {
            hitMarker.gameObject.SetActive(visible);
        }
    }

    private void LogHitChangeIfNeeded(GameObject hitObject)
    {
        if (!logHitChanges || hitObject == previousHitObject)
            return;

        string objectName =
            hitObject != null ? hitObject.name : "None";

        Debug.Log($"[GazeRay] Hit object: {objectName}");
        previousHitObject = hitObject;
    }
}
