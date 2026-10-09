using UnityEngine;

/// <summary>
/// Keeps the avatar's animated Head bone locked to the HMD camera, using the
/// camera-to-head relation authored in the Unity Editor (captured in edit mode).
/// The XR Origin is never moved by this script.
/// </summary>
[DefaultExecutionOrder(-200)]
public class LocalAvatarIKDriver : MonoBehaviour
{
    [Header("XR Sources")]
    public Transform leftHandSource;
    public Transform rightHandSource;
    public Transform headSource;

    [Header("IK Targets")]
    public Transform leftHandTarget;
    public Transform rightHandTarget;
    public Transform headTarget;
    public Transform bodyRoot;

    [Tooltip("Actual avatar Head bone. Auto-resolved from Animator if unset.")]
    [SerializeField] private Transform avatarHeadBone;

    [Header("Authored Pose (captured automatically in the Editor)")]
    [Tooltip("Head bone minus camera, expressed in bodyRoot yaw space, as placed in the Editor.")]
    [SerializeField] private Vector3 authoredHeadMinusCameraLocal;
    [SerializeField] private bool authoredHeadPoseValid;

    [Header("Body")]
    [SerializeField] private float bodyTurnThreshold = 35f;
    [SerializeField] private float bodyTurnSpeed = 90f;
    [Tooltip("Optional extra world-up shift in meters on top of the Editor-authored pose. Keep 0 to match the Editor exactly.")]
    [SerializeField] private float bodyVerticalOffset;

    [Header("Hand Rotation Calibration")]
    [SerializeField] private Vector3 leftHandRotationOffset;
    [SerializeField] private Vector3 rightHandRotationOffset;

    public Vector3 LeftHandRotationOffset => leftHandRotationOffset;
    public Vector3 RightHandRotationOffset => rightHandRotationOffset;

    private Quaternion headRotationOffset;
    private float nextAvatarDebugTime;

    public void AddHandRotationOffset(bool rightHand, Vector3 eulerDelta)
    {
        if (rightHand)
            rightHandRotationOffset += eulerDelta;
        else
            leftHandRotationOffset += eulerDelta;
    }

    public void ResetHandRotationOffset(bool rightHand)
    {
        if (rightHand)
            rightHandRotationOffset = Vector3.zero;
        else
            leftHandRotationOffset = Vector3.zero;
    }

    /// <summary>
    /// Stores the current camera/Head-bone arrangement. Called by the Editor script before
    /// entering Play mode, when saving the scene and when building (never at runtime,
    /// because the Animator changes the avatar pose and XROrigin rewrites Camera Offset).
    /// </summary>
    public void CaptureAuthoredPose()
    {
        if (headSource == null || bodyRoot == null)
            return;

        ResolveAvatarHeadBone();
        if (avatarHeadBone == null)
            return;

        Quaternion yaw = Quaternion.Euler(0f, bodyRoot.eulerAngles.y, 0f);
        authoredHeadMinusCameraLocal =
            Quaternion.Inverse(yaw) * (avatarHeadBone.position - headSource.position);
        authoredHeadPoseValid = true;
    }

    private void Awake()
    {
        if (headSource == null || bodyRoot == null)
            return;

        ResolveAvatarHeadBone();

        // Must be added in Awake (before the Animator's first evaluation) so it can read the authored pose.
        if (bodyRoot.GetComponent<AvatarWaistClipDriver>() == null)
            bodyRoot.gameObject.AddComponent<AvatarWaistClipDriver>();

        if (!authoredHeadPoseValid)
        {
            Debug.LogWarning(
                "[AVATAR ALIGN] Authored camera-to-head pose is missing. " +
                "Leave Play mode and press Play again (or save the scene) so the Editor can store it.");
        }
    }

    private void Start()
    {
        if (headSource != null && headTarget != null)
        {
            headRotationOffset =
                Quaternion.Inverse(headSource.rotation) * headTarget.rotation;
        }

        ResolveAvatarHeadBone();
    }

    private void ResolveAvatarHeadBone()
    {
        if (avatarHeadBone != null)
            return;

        if (bodyRoot == null)
            return;

        Animator animator = bodyRoot.GetComponentInChildren<Animator>();
        if (animator != null)
            avatarHeadBone = animator.GetBoneTransform(HumanBodyBones.Head);

        if (avatarHeadBone == null)
            avatarHeadBone = FindDeepChild(bodyRoot, "Head");
    }

    private static Transform FindDeepChild(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == name)
                return child;

            Transform nested = FindDeepChild(child, name);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private void UpdateBodyPosition()
    {
        if (!authoredHeadPoseValid || bodyRoot == null || headSource == null)
            return;

        ResolveAvatarHeadBone();
        if (avatarHeadBone == null)
            return;

        Quaternion yaw = Quaternion.Euler(0f, bodyRoot.eulerAngles.y, 0f);
        Vector3 desiredHead =
            headSource.position + yaw * authoredHeadMinusCameraLocal
            + Vector3.up * bodyVerticalOffset;

        // The Head bone is animated, so move the whole body by whatever is still missing.
        bodyRoot.position += desiredHead - avatarHeadBone.position;

        LogAvatarDebug(desiredHead);
    }

    private void LogAvatarDebug(Vector3 desiredHead)
    {
        if (Time.unscaledTime < nextAvatarDebugTime)
            return;

        nextAvatarDebugTime = Time.unscaledTime + 1f;

        Vector3 hmd = headSource.position;
        Vector3 head = avatarHeadBone.position;

        Debug.Log(
            "[AVATAR DEBUG] hmd=" + hmd.ToString("F3") +
            " avatarHead=" + head.ToString("F3") +
            " hmdMinusAvatarHead=" + (hmd - head).ToString("F3") +
            " authoredHeadMinusCamera=" + authoredHeadMinusCameraLocal.ToString("F3") +
            " headError=" + (desiredHead - head).magnitude.ToString("F4") +
            " bodyRoot=" + bodyRoot.position.ToString("F3") +
            " bodyYaw=" + bodyRoot.eulerAngles.y.ToString("F1"));
    }

    private void UpdateBodyRotation()
    {
        if (bodyRoot == null || headSource == null)
            return;

        float headYaw = headSource.eulerAngles.y;
        float bodyYaw = bodyRoot.eulerAngles.y;

        float yawDifference = Mathf.DeltaAngle(bodyYaw, headYaw);

        if (Mathf.Abs(yawDifference) > bodyTurnThreshold)
        {
            float newYaw = Mathf.MoveTowardsAngle(
                bodyYaw,
                headYaw,
                bodyTurnSpeed * Time.deltaTime
            );

            bodyRoot.rotation = Quaternion.Euler(0f, newYaw, 0f);
        }
    }

    private void Update()
    {
        UpdateBodyRotation();
        UpdateBodyPosition();

        if (headSource != null && headTarget != null)
        {
            headTarget.rotation =
                headSource.rotation * headRotationOffset;
        }

        if (leftHandSource != null && leftHandTarget != null)
        {
            leftHandTarget.position = leftHandSource.position;
            leftHandTarget.rotation =
                leftHandSource.rotation *
                Quaternion.Euler(leftHandRotationOffset);
        }

        if (rightHandSource != null && rightHandTarget != null)
        {
            rightHandTarget.position = rightHandSource.position;
            rightHandTarget.rotation =
                rightHandSource.rotation *
                Quaternion.Euler(rightHandRotationOffset);
        }
    }
}
