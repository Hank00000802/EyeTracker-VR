using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

/// <summary>
/// Local-only VR height adjustment: left X toggles mode; left stick Y adjusts a dedicated offset transform.
/// </summary>
public class VRHeightAdjustController : MonoBehaviour
{
    private const string DefaultToggleBinding =
        "<XRController>{LeftHand}/{PrimaryButton}";

    private const string DefaultHeightStickBinding =
        "<XRController>{LeftHand}/{Primary2DAxis}";

    [Header("References")]
    [SerializeField] private XROrigin xrOrigin;
    [SerializeField] private DynamicMoveProvider moveProvider;
    [SerializeField] private InputActionReference toggleHeightAdjustAction;
    [Tooltip("Optional. If unset, a dedicated Height Stick action bound to Left Primary2DAxis is created.")]
    [SerializeField] private InputActionReference heightStickAction;
    [Tooltip("Optional child under Camera Floor Offset. Created at runtime if unset.")]
    [SerializeField] private Transform heightOffsetRoot;

    [Header("Height Adjust")]
    [SerializeField] private float heightAdjustSpeed = 0.5f;
    [SerializeField] private float minHeightOffset = -0.5f;
    [SerializeField] private float maxHeightOffset = 0.8f;

    private bool heightAdjustModeActive;
    private bool moveProviderWasEnabled = true;
    private float currentHeightOffset;
    private InputAction toggleAction;
    private InputAction stickAction;
    private InputAction runtimeToggleAction;
    private InputAction runtimeStickAction;
    private float nextDiagTime;

    private void Awake()
    {
        if (xrOrigin == null)
            xrOrigin = GetComponent<XROrigin>();

        if (moveProvider == null)
            moveProvider = GetComponent<DynamicMoveProvider>();

        EnsureHeightOffsetRoot();
        ResolveInputActions();
    }

    private void OnEnable()
    {
        toggleAction?.Enable();
        stickAction?.Enable();
    }

    private void OnDisable()
    {
        if (heightAdjustModeActive)
            ExitHeightAdjustMode();

        toggleAction?.Disable();
        stickAction?.Disable();
    }

    private void Start()
    {
        ApplyHeightOffset();
    }

    private void OnDestroy()
    {
        if (runtimeToggleAction != null)
            runtimeToggleAction.Dispose();

        if (runtimeStickAction != null)
            runtimeStickAction.Dispose();
    }

    private void Update()
    {
        if (toggleAction != null && toggleAction.WasPressedThisFrame())
            ToggleHeightAdjustMode();

        if (!heightAdjustModeActive)
            return;

        Vector2 stick = stickAction != null
            ? stickAction.ReadValue<Vector2>()
            : Vector2.zero;

        float stickY = stick.y;
        float currentY = heightOffsetRoot != null
            ? heightOffsetRoot.localPosition.y
            : 0f;

        if (Mathf.Abs(stickY) >= 0.1f)
        {
            currentHeightOffset = Mathf.Clamp(
                currentHeightOffset + stickY * heightAdjustSpeed * Time.deltaTime,
                minHeightOffset,
                maxHeightOffset);

            ApplyHeightOffset();
        }

        float targetY = currentHeightOffset;

        if (Time.unscaledTime >= nextDiagTime)
        {
            nextDiagTime = Time.unscaledTime + 0.25f;
            Debug.Log(
                $"[HEIGHT TEST] stick={stick} stickY={stickY:F3} " +
                $"currentY={currentY:F3} targetY={targetY:F3}");
        }
    }

    private void EnsureHeightOffsetRoot()
    {
        if (heightOffsetRoot != null)
            return;

        Transform floorOffset = xrOrigin != null &&
                                xrOrigin.CameraFloorOffsetObject != null
            ? xrOrigin.CameraFloorOffsetObject.transform
            : transform;

        // Additive offset under Camera Floor Offset so XROrigin Floor mode
        // can keep CameraFloorOffsetObject.y = 0 without undoing height adjust.
        Transform existing = floorOffset.Find("HeightAdjustOffset");
        if (existing != null)
        {
            heightOffsetRoot = existing;
            return;
        }

        var go = new GameObject("HeightAdjustOffset");
        heightOffsetRoot = go.transform;
        heightOffsetRoot.SetParent(floorOffset, false);
        heightOffsetRoot.localPosition = Vector3.zero;
        heightOffsetRoot.localRotation = Quaternion.identity;
        heightOffsetRoot.localScale = Vector3.one;

        // Reparent tracked children so viewpoint/controllers rise together.
        for (int i = floorOffset.childCount - 1; i >= 0; i--)
        {
            Transform child = floorOffset.GetChild(i);
            if (child == heightOffsetRoot)
                continue;

            child.SetParent(heightOffsetRoot, true);
        }
    }

    private void ResolveInputActions()
    {
        if (toggleHeightAdjustAction != null)
        {
            toggleAction = toggleHeightAdjustAction.action;
        }
        else
        {
            runtimeToggleAction = new InputAction(
                name: "Height Adjust Toggle",
                type: InputActionType.Button,
                binding: DefaultToggleBinding);

            toggleAction = runtimeToggleAction;
        }

        // Dedicated stick action — independent of XRI Left Locomotion / Move,
        // so disabling DynamicMoveProvider cannot starve height input.
        if (heightStickAction != null)
        {
            stickAction = heightStickAction.action;
        }
        else
        {
            runtimeStickAction = new InputAction(
                name: "Height Adjust Stick",
                type: InputActionType.Value,
                binding: DefaultHeightStickBinding,
                expectedControlType: "Vector2");

            stickAction = runtimeStickAction;
        }
    }

    private void ToggleHeightAdjustMode()
    {
        if (heightAdjustModeActive)
            ExitHeightAdjustMode();
        else
            EnterHeightAdjustMode();
    }

    private void EnterHeightAdjustMode()
    {
        heightAdjustModeActive = true;

        if (moveProvider != null)
        {
            moveProviderWasEnabled = moveProvider.enabled;
            moveProvider.enabled = false;
        }

        stickAction?.Enable();
        nextDiagTime = 0f;

        Debug.Log("[HEIGHT] Adjust mode ON");
    }

    private void ExitHeightAdjustMode()
    {
        heightAdjustModeActive = false;

        if (moveProvider != null)
            moveProvider.enabled = moveProviderWasEnabled;

        Debug.Log("[HEIGHT] Adjust mode OFF");
    }

    private void ApplyHeightOffset()
    {
        if (heightOffsetRoot == null)
            return;

        Vector3 pos = heightOffsetRoot.localPosition;
        pos.y = currentHeightOffset;
        heightOffsetRoot.localPosition = pos;
    }
}
