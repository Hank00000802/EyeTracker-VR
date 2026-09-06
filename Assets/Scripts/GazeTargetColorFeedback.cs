using UnityEngine;

/// <summary>
/// 依 ViveGazeRayVisualizer.CurrentHitObject 對三個注視目標做變色回饋。
/// 透過 MaterialPropertyBlock 改色，不產生 Material Instance。
/// </summary>
public class GazeTargetColorFeedback : MonoBehaviour
{
    // 於 Awake() 以 Shader.PropertyToID 賦值，避免在欄位初始化呼叫 UnityEngine API。
    private int baseColorId;
    private int colorId;

    [Header("Gaze Source")]
    [SerializeField] private ViveGazeRayVisualizer gazeVisualizer;

    [Header("Target Renderers")]
    [SerializeField] private Renderer leftTargetRenderer;
    [SerializeField] private Renderer centerTargetRenderer;
    [SerializeField] private Renderer rightTargetRenderer;

    [Header("Highlight Colors")]
    [SerializeField] private Color leftHighlightColor = Color.red;
    [SerializeField] private Color centerHighlightColor = Color.yellow;
    [SerializeField] private Color rightHighlightColor = Color.green;

    // 只宣告，於 Awake() 初始化；欄位處 new 會觸發 CreateImpl 錯誤。
    private MaterialPropertyBlock propertyBlock;

    private Color leftDefaultColor = Color.white;
    private Color centerDefaultColor = Color.white;
    private Color rightDefaultColor = Color.white;

    private int leftColorPropertyId;
    private int centerColorPropertyId;
    private int rightColorPropertyId;

    private bool leftReady;
    private bool centerReady;
    private bool rightReady;

    private Renderer currentHighlighted;

    private void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();

        baseColorId = Shader.PropertyToID("_BaseColor");
        colorId = Shader.PropertyToID("_Color");

        CacheDefaultColors();
        RestoreAllColors();
    }

    private void CacheDefaultColors()
    {
        leftReady = TryCacheDefault(leftTargetRenderer, out leftDefaultColor, out leftColorPropertyId);
        centerReady = TryCacheDefault(centerTargetRenderer, out centerDefaultColor, out centerColorPropertyId);
        rightReady = TryCacheDefault(rightTargetRenderer, out rightDefaultColor, out rightColorPropertyId);
    }

    private void RestoreAllColors()
    {
        RestoreAllDefaults();
    }

    private void Update()
    {
        if (gazeVisualizer == null)
        {
            ClearHighlight();
            return;
        }

        GameObject hit = gazeVisualizer.CurrentHitObject;

        if (IsHit(leftTargetRenderer, hit))
        {
            ApplyHighlight(leftTargetRenderer, leftHighlightColor, leftColorPropertyId, leftReady);
            return;
        }

        if (IsHit(centerTargetRenderer, hit))
        {
            ApplyHighlight(centerTargetRenderer, centerHighlightColor, centerColorPropertyId, centerReady);
            return;
        }

        if (IsHit(rightTargetRenderer, hit))
        {
            ApplyHighlight(rightTargetRenderer, rightHighlightColor, rightColorPropertyId, rightReady);
            return;
        }

        ClearHighlight();
    }

    private void OnDisable()
    {
        RestoreAllDefaults();
        currentHighlighted = null;
    }

    private void ApplyHighlight(
        Renderer target,
        Color highlightColor,
        int colorPropertyId,
        bool ready)
    {
        if (!ready || target == null)
        {
            ClearHighlight();
            return;
        }

        if (currentHighlighted != null && currentHighlighted != target)
            RestoreRenderer(currentHighlighted);

        SetRendererColor(target, colorPropertyId, highlightColor);
        currentHighlighted = target;
    }

    private void ClearHighlight()
    {
        if (currentHighlighted == null)
            return;

        RestoreRenderer(currentHighlighted);
        currentHighlighted = null;
    }

    private void RestoreAllDefaults()
    {
        if (leftReady)
            SetRendererColor(leftTargetRenderer, leftColorPropertyId, leftDefaultColor);

        if (centerReady)
            SetRendererColor(centerTargetRenderer, centerColorPropertyId, centerDefaultColor);

        if (rightReady)
            SetRendererColor(rightTargetRenderer, rightColorPropertyId, rightDefaultColor);
    }

    private void RestoreRenderer(Renderer target)
    {
        if (target == null)
            return;

        if (target == leftTargetRenderer && leftReady)
        {
            SetRendererColor(target, leftColorPropertyId, leftDefaultColor);
            return;
        }

        if (target == centerTargetRenderer && centerReady)
        {
            SetRendererColor(target, centerColorPropertyId, centerDefaultColor);
            return;
        }

        if (target == rightTargetRenderer && rightReady)
            SetRendererColor(target, rightColorPropertyId, rightDefaultColor);
    }

    private void SetRendererColor(Renderer target, int colorPropertyId, Color color)
    {
        if (target == null)
            return;

        target.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(colorPropertyId, color);
        target.SetPropertyBlock(propertyBlock);
    }

    private static bool IsHit(Renderer target, GameObject hitObject)
    {
        return target != null &&
               hitObject != null &&
               hitObject == target.gameObject;
    }

    private bool TryCacheDefault(
        Renderer target,
        out Color defaultColor,
        out int colorPropertyId)
    {
        defaultColor = Color.white;
        colorPropertyId = 0;

        if (target == null)
            return false;

        Material shared = target.sharedMaterial;
        if (shared == null)
            return false;

        if (shared.HasProperty(baseColorId))
        {
            colorPropertyId = baseColorId;
            defaultColor = shared.GetColor(baseColorId);
            return true;
        }

        if (shared.HasProperty(colorId))
        {
            colorPropertyId = colorId;
            defaultColor = shared.GetColor(colorId);
            return true;
        }

        return false;
    }
}
