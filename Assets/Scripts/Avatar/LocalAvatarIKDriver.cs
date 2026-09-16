using UnityEngine;

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

    private Quaternion headRotationOffset;
    private float nextAvatarDebugTime;
    private Vector3 bodyXzVelocity;
    private float bodyYVelocity;
    private float bodyYOffsetFromHead;
    private bool bodyYFollowArmed;

    [SerializeField] private float bodyTurnThreshold = 35f;
    [SerializeField] private float bodyTurnSpeed = 90f;
    [Tooltip("Ignore XZ error below this (meters) to avoid HMD micro-jitter.")]
    [SerializeField] private float bodyPositionThreshold = 0.012f;
    [Tooltip("SmoothDamp time for body XZ follow.")]
    [SerializeField] private float bodyFollowSmoothTime = 0.04f;
    [Tooltip("Max XZ catch-up speed (m/s).")]
    [SerializeField] private float bodyMoveSpeed = 8f;
    [Tooltip("Horizontal only. X = left/right (local X). Y = forward/back (local Z). Inspector Y is NOT height.")]
    [SerializeField] private Vector2 bodyHorizontalOffset;
    [Tooltip("World up in meters. Positive raises the avatar (viewpoint feels lower). Negative lowers the avatar (viewpoint closer to the head).")]
    [SerializeField] private float bodyVerticalOffset;
    [Tooltip("Start following HMD Y after this vertical error (meters).")]
    [SerializeField] private float bodyYFollowDeadzone = 0.03f;
    [Tooltip("Slow filtered Y follow. Large seated height changes still use VRHeightAdjustController.")]
    [SerializeField] private float bodyYFollowSmoothTime = 0.18f;

    [Header("Hand Rotation Calibration")]
    [SerializeField] private Vector3 leftHandRotationOffset;
    [SerializeField] private Vector3 rightHandRotationOffset;

    public Vector3 LeftHandRotationOffset => leftHandRotationOffset;
    public Vector3 RightHandRotationOffset => rightHandRotationOffset;

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

    private Vector3 GetDesiredBodyPosition()
    {
        float bodyYaw = bodyRoot.eulerAngles.y;
        Vector3 localOffset = new Vector3(
            bodyHorizontalOffset.x,
            0f,
            bodyHorizontalOffset.y);
        Vector3 worldOffset = Quaternion.Euler(0f, bodyYaw, 0f) * localOffset;

        return new Vector3(
            headSource.position.x + worldOffset.x,
            bodyRoot.position.y,
            headSource.position.z + worldOffset.z
        );
    }

    private void UpdateBodyPosition()
    {
        if (bodyRoot == null || headSource == null)
            return;

        Vector3 desired = GetDesiredBodyPosition();
        Vector3 current = bodyRoot.position;

        Vector3 currentHorizontal = new Vector3(current.x, 0f, current.z);
        Vector3 desiredHorizontal = new Vector3(desired.x, 0f, desired.z);
        float xzError = Vector3.Distance(currentHorizontal, desiredHorizontal);

        Vector3 next = current;

        if (xzError > bodyPositionThreshold)
        {
            Vector3 xzTarget = new Vector3(desired.x, current.y, desired.z);
            next = Vector3.SmoothDamp(
                current,
                xzTarget,
                ref bodyXzVelocity,
                bodyFollowSmoothTime,
                bodyMoveSpeed
            );
            next.y = current.y;
            bodyXzVelocity.y = 0f;
        }
        else
        {
            bodyXzVelocity = Vector3.Lerp(bodyXzVelocity, Vector3.zero, Time.deltaTime * 8f);
        }

        float yError = 0f;
        if (!bodyYFollowArmed)
        {
            float headY = headSource.position.y;
            if (headY > 0.5f && headY < 2.4f)
            {
                bodyYOffsetFromHead = current.y - headY;
                bodyYFollowArmed = true;
            }
        }
        else
        {
            float desiredY =
                headSource.position.y + bodyYOffsetFromHead + bodyVerticalOffset;
            yError = desiredY - next.y;
            if (Mathf.Abs(yError) > bodyYFollowDeadzone)
            {
                next.y = Mathf.SmoothDamp(
                    next.y,
                    desiredY,
                    ref bodyYVelocity,
                    bodyYFollowSmoothTime
                );
            }
            else
            {
                bodyYVelocity = 0f;
            }
        }

        bodyRoot.position = next;

        LogAvatarDebug(desired, xzError, yError);
    }

    private void LogAvatarDebug(Vector3 desired, float xzError, float yError)
    {
        if (Time.unscaledTime < nextAvatarDebugTime)
            return;

        nextAvatarDebugTime = Time.unscaledTime + 0.5f;

        Vector3 headHmd = headSource.position;
        Vector3 body = bodyRoot.position;
        Vector3 avatarHead = avatarHeadBone != null
            ? avatarHeadBone.position
            : (headTarget != null ? headTarget.position : body);

        Vector3 hmdToAvatarHead = avatarHead - headHmd;
        Vector3 hmdToBody = body - headHmd;

        string headBoneSource = avatarHeadBone != null
            ? avatarHeadBone.name
            : (headTarget != null ? "HeadTargetFallback" : "bodyRootFallback");

        Debug.Log(
            $"[AVATAR DEBUG] headSource={headHmd} bodyRoot={body} " +
            $"avatarHead({headBoneSource})={avatarHead} " +
            $"hmdToAvatarHead={hmdToAvatarHead} dist={hmdToAvatarHead.magnitude:F3} " +
            $"hmdToBody={hmdToBody} dist={hmdToBody.magnitude:F3} " +
            $"bodyXzTarget=({desired.x:F3},{desired.z:F3}) xzError={xzError:F3} yError={yError:F3} " +
            $"threshold={bodyPositionThreshold:F3} moveSpeed={bodyMoveSpeed:F2} " +
            $"xzSmooth={bodyFollowSmoothTime:F3} yArmed={bodyYFollowArmed} yDeadzone={bodyYFollowDeadzone:F3} " +
            $"horizOffset={bodyHorizontalOffset} vertOffset={bodyVerticalOffset:F3} bodyYaw={bodyRoot.eulerAngles.y:F1}");
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
        UpdateBodyPosition();
        UpdateBodyRotation();

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
