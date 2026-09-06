using UnityEngine;
using Unity.Netcode;

public class NetworkVRPlayer : NetworkBehaviour
{
    [Header("Remote Visuals")]
    [SerializeField] private GameObject headVisual;
    [SerializeField] private GameObject leftHandVisual;
    [SerializeField] private GameObject rightHandVisual;

    [Header("Network")]
    [SerializeField] private float sendRate = 20f;
    [SerializeField] private float interpolationSpeed = 20f;

    private Transform localHead;
    private Transform localLeftController;
    private Transform localRightController;

    private float nextSendTime;

    private readonly NetworkVariable<Vector3> headPosition =
        new NetworkVariable<Vector3>(
            Vector3.zero,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );

    private readonly NetworkVariable<Quaternion> headRotation =
        new NetworkVariable<Quaternion>(
            Quaternion.identity,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );

    private readonly NetworkVariable<Vector3> leftHandPosition =
        new NetworkVariable<Vector3>(
            Vector3.zero,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );

    private readonly NetworkVariable<Quaternion> leftHandRotation =
        new NetworkVariable<Quaternion>(
            Quaternion.identity,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );

    private readonly NetworkVariable<Vector3> rightHandPosition =
        new NetworkVariable<Vector3>(
            Vector3.zero,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );

    private readonly NetworkVariable<Quaternion> rightHandRotation =
        new NetworkVariable<Quaternion>(
            Quaternion.identity,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            FindLocalXRObjects();

            // 自己不需要看到自己的 Debug Avatar
            SetVisuals(false);
        }
        else
        {
            // Remote Player 要顯示
            SetVisuals(true);
        }

        Debug.Log(
            $"[NETWORK VR PLAYER] Spawned | " +
            $"OwnerClientId={OwnerClientId} | " +
            $"IsOwner={IsOwner}"
        );
    }

    private void Update()
    {
        if (!IsSpawned)
            return;

        if (IsOwner)
        {
            UpdateOwnerPose();
        }
        else
        {
            UpdateRemoteVisuals();
        }
    }

    private void UpdateOwnerPose()
    {
        if (localHead == null ||
            localLeftController == null ||
            localRightController == null)
        {
            FindLocalXRObjects();
        }

        if (Time.unscaledTime < nextSendTime)
            return;

        nextSendTime =
            Time.unscaledTime + (1f / sendRate);

        if (localHead != null)
        {
            headPosition.Value =
                localHead.position;

            headRotation.Value =
                localHead.rotation;
        }

        if (localLeftController != null)
        {
            leftHandPosition.Value =
                localLeftController.position;

            leftHandRotation.Value =
                localLeftController.rotation;
        }

        if (localRightController != null)
        {
            rightHandPosition.Value =
                localRightController.position;

            rightHandRotation.Value =
                localRightController.rotation;
        }
    }

    private void UpdateRemoteVisuals()
    {
        float t =
            1f - Mathf.Exp(
                -interpolationSpeed * Time.deltaTime
            );

        if (headVisual != null)
        {
            headVisual.transform.position =
                Vector3.Lerp(
                    headVisual.transform.position,
                    headPosition.Value,
                    t
                );

            headVisual.transform.rotation =
                Quaternion.Slerp(
                    headVisual.transform.rotation,
                    headRotation.Value,
                    t
                );
        }

        if (leftHandVisual != null)
        {
            leftHandVisual.transform.position =
                Vector3.Lerp(
                    leftHandVisual.transform.position,
                    leftHandPosition.Value,
                    t
                );

            leftHandVisual.transform.rotation =
                Quaternion.Slerp(
                    leftHandVisual.transform.rotation,
                    leftHandRotation.Value,
                    t
                );
        }

        if (rightHandVisual != null)
        {
            rightHandVisual.transform.position =
                Vector3.Lerp(
                    rightHandVisual.transform.position,
                    rightHandPosition.Value,
                    t
                );

            rightHandVisual.transform.rotation =
                Quaternion.Slerp(
                    rightHandVisual.transform.rotation,
                    rightHandRotation.Value,
                    t
                );
        }
    }

    private void FindLocalXRObjects()
    {
        Camera mainCamera = Camera.main;

        if (mainCamera != null)
        {
            localHead =
                mainCamera.transform;
        }

        GameObject left =
            GameObject.Find("Left Controller");

        GameObject right =
            GameObject.Find("Right Controller");

        if (left != null)
        {
            localLeftController =
                left.transform;
        }

        if (right != null)
        {
            localRightController =
                right.transform;
        }

        Debug.Log(
            $"[NETWORK VR PLAYER] XR refs | " +
            $"Head={localHead != null} | " +
            $"Left={localLeftController != null} | " +
            $"Right={localRightController != null}"
        );
    }

    private void SetVisuals(bool visible)
    {
        if (headVisual != null)
            headVisual.SetActive(visible);

        if (leftHandVisual != null)
            leftHandVisual.SetActive(visible);

        if (rightHandVisual != null)
            rightHandVisual.SetActive(visible);
    }
}