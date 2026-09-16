using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Remote-only: copies interpolated network Body/Head/Left/Right into the avatar IK targets.
/// Must not read this device's XR Origin or LocalAvatarIKDriver.
/// </summary>
public class RemoteAvatarPoseDriver : MonoBehaviour
{
    [SerializeField] private Transform networkBody;
    [SerializeField] private Transform networkHead;
    [SerializeField] private Transform networkLeft;
    [SerializeField] private Transform networkRight;

    [SerializeField] private Transform bodyRoot;
    [SerializeField] private Transform headTarget;
    [SerializeField] private Transform leftHandTarget;
    [SerializeField] private Transform rightHandTarget;

    private NetworkObject networkObject;
    private float nextDebugTime;

    public void Bind(
        Transform bodyProxy,
        Transform headProxy,
        Transform leftProxy,
        Transform rightProxy,
        Transform remoteBodyRoot,
        Transform remoteHeadTarget,
        Transform remoteLeftHandTarget,
        Transform remoteRightHandTarget)
    {
        networkBody = bodyProxy;
        networkHead = headProxy;
        networkLeft = leftProxy;
        networkRight = rightProxy;
        bodyRoot = remoteBodyRoot;
        headTarget = remoteHeadTarget;
        leftHandTarget = remoteLeftHandTarget;
        rightHandTarget = remoteRightHandTarget;
    }

    private void Awake()
    {
        networkObject = GetComponentInParent<NetworkObject>();
    }

    private void LateUpdate()
    {
        if (networkObject == null)
            networkObject = GetComponentInParent<NetworkObject>();

        if (networkObject == null || !networkObject.IsSpawned || networkObject.IsOwner)
            return;

        if (bodyRoot != null && networkBody != null)
        {
            bodyRoot.position = networkBody.position;
            bodyRoot.rotation = Quaternion.Euler(0f, networkBody.eulerAngles.y, 0f);
        }

        if (headTarget != null && networkHead != null)
            headTarget.rotation = networkHead.rotation;

        if (leftHandTarget != null && networkLeft != null)
        {
            leftHandTarget.position = networkLeft.position;
            leftHandTarget.rotation = networkLeft.rotation;
        }

        if (rightHandTarget != null && networkRight != null)
        {
            rightHandTarget.position = networkRight.position;
            rightHandTarget.rotation = networkRight.rotation;
        }

        if (Time.unscaledTime >= nextDebugTime)
        {
            nextDebugTime = Time.unscaledTime + 0.5f;
            Vector3 bodyPos = bodyRoot != null ? bodyRoot.position : Vector3.zero;
            Vector3 netBodyPos = networkBody != null ? networkBody.position : Vector3.zero;
            Debug.Log(
                $"[NET AVATAR REMOTE] IsOwner=false " +
                $"bodyProxy={(networkBody != null)} headProxy={(networkHead != null)} " +
                $"leftProxy={(networkLeft != null)} rightProxy={(networkRight != null)} " +
                $"bodyRoot={(bodyRoot != null)} headTarget={(headTarget != null)} " +
                $"leftTarget={(leftHandTarget != null)} rightTarget={(rightHandTarget != null)} " +
                $"networkBodyPos={netBodyPos} avatarBodyPos={bodyPos}");
        }
    }
}
