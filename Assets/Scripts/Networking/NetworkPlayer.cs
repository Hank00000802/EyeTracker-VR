using UnityEngine;
using Unity.Netcode;

public class NetworkVRPlayer : NetworkBehaviour
{
    [Header("Pose Proxies")]
    [SerializeField] private GameObject headVisual;
    [SerializeField] private GameObject leftHandVisual;
    [SerializeField] private GameObject rightHandVisual;
    [SerializeField] private GameObject bodyVisual;

    [Header("Remote Avatar")]
    [SerializeField] private GameObject remoteAvatarVisual;
    [SerializeField] private Transform remoteHeadTarget;
    [SerializeField] private Transform remoteLeftHandTarget;
    [SerializeField] private Transform remoteRightHandTarget;

    [Header("Role Outerwear")]
    [SerializeField] private Material hostOuterwearMaterial;
    [SerializeField] private Material clientOuterwearMaterial;

    [Header("Network")]
    [SerializeField] private float sendRate = 20f;
    [SerializeField] private float interpolationSpeed = 20f;

    private Transform localBodyRoot;
    private Transform localHeadTarget;
    private Transform localLeftHandTarget;
    private Transform localRightHandTarget;
    private bool ownerSourcesResolved;

    private float nextSendTime;
    private float nextOwnerDebugTime;
    private bool remoteSnapped;

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

    private readonly NetworkVariable<Vector3> bodyPosition =
        new NetworkVariable<Vector3>(
            Vector3.zero,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );

    private readonly NetworkVariable<float> bodyYaw =
        new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );

    public override void OnNetworkSpawn()
    {
        ResolvePrefabHierarchy();
        DisableRemoteLocalDrivers();
        HideProxyRenderers();
        EnsureRemotePoseDriver();
        ApplyRoleOuterwear();

        if (IsOwner)
        {
            ResolveOwnerSourcesOnce();
            SetRenderersEnabled(remoteAvatarVisual, false);
        }
        else
        {
            SetRenderersEnabled(remoteAvatarVisual, true);
        }

        Debug.Log(
            $"[NETWORK VR PLAYER] Spawned | " +
            $"OwnerClientId={OwnerClientId} | " +
            $"IsOwner={IsOwner} | " +
            $"bodyVisual={(bodyVisual != null)} | " +
            $"remoteAvatar={(remoteAvatarVisual != null)}"
        );
    }

    private void Update()
    {
        if (!IsSpawned || IsOwner)
            return;

        UpdateRemoteProxies();
    }

    private void LateUpdate()
    {
        if (!IsSpawned || !IsOwner)
            return;

        UpdateOwnerPose();
    }

    private void UpdateOwnerPose()
    {
        if (!ownerSourcesResolved)
            ResolveOwnerSourcesOnce();

        if (Time.unscaledTime < nextSendTime)
        {
            LogOwnerDebug();
            return;
        }

        nextSendTime = Time.unscaledTime + (1f / sendRate);

        if (localHeadTarget != null)
        {
            headPosition.Value = localHeadTarget.position;
            headRotation.Value = localHeadTarget.rotation;
        }

        if (localLeftHandTarget != null)
        {
            leftHandPosition.Value = localLeftHandTarget.position;
            leftHandRotation.Value = localLeftHandTarget.rotation;
        }

        if (localRightHandTarget != null)
        {
            rightHandPosition.Value = localRightHandTarget.position;
            rightHandRotation.Value = localRightHandTarget.rotation;
        }

        if (localBodyRoot != null)
        {
            bodyPosition.Value = localBodyRoot.position;
            bodyYaw.Value = localBodyRoot.eulerAngles.y;
        }

        LogOwnerDebug();
    }

    private void UpdateRemoteProxies()
    {
        float t = remoteSnapped
            ? 1f - Mathf.Exp(-interpolationSpeed * Time.deltaTime)
            : 1f;

        ApplyPose(headVisual, headPosition.Value, headRotation.Value, t);
        ApplyPose(leftHandVisual, leftHandPosition.Value, leftHandRotation.Value, t);
        ApplyPose(rightHandVisual, rightHandPosition.Value, rightHandRotation.Value, t);
        ApplyBodyPose(t);

        remoteSnapped = true;
    }

    private static void ApplyPose(
        GameObject visual,
        Vector3 targetPosition,
        Quaternion targetRotation,
        float t)
    {
        if (visual == null)
            return;

        Transform tf = visual.transform;
        tf.position = Vector3.Lerp(tf.position, targetPosition, t);
        tf.rotation = Quaternion.Slerp(tf.rotation, targetRotation, t);
    }

    private void ApplyBodyPose(float t)
    {
        if (bodyVisual == null)
            return;

        Transform tf = bodyVisual.transform;
        tf.position = Vector3.Lerp(tf.position, bodyPosition.Value, t);

        float yaw = Mathf.LerpAngle(tf.eulerAngles.y, bodyYaw.Value, t);
        tf.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    private void ResolvePrefabHierarchy()
    {
        if (headVisual == null)
            headVisual = FindDirectChild("Head");
        if (leftHandVisual == null)
            leftHandVisual = FindDirectChild("Left");
        if (rightHandVisual == null)
            rightHandVisual = FindDirectChild("Right");
        if (bodyVisual == null)
            bodyVisual = FindDirectChild("Body");
        if (remoteAvatarVisual == null)
            remoteAvatarVisual = FindDirectChild("RemoteAvatarVisual");

        Transform remoteRoot = remoteAvatarVisual != null
            ? remoteAvatarVisual.transform
            : null;

        if (remoteHeadTarget == null && remoteRoot != null)
            remoteHeadTarget = FindDeepChild(remoteRoot, "HeadTarget");
        if (remoteLeftHandTarget == null && remoteRoot != null)
            remoteLeftHandTarget = FindDeepChild(remoteRoot, "LeftHandTarget");
        if (remoteRightHandTarget == null && remoteRoot != null)
            remoteRightHandTarget = FindDeepChild(remoteRoot, "RightHandTarget");
    }

    private void ResolveOwnerSourcesOnce()
    {
        if (ownerSourcesResolved)
            return;

        LocalAvatarIKDriver localDriver = null;
        LocalAvatarIKDriver[] drivers =
            FindObjectsByType<LocalAvatarIKDriver>(FindObjectsSortMode.None);

        for (int i = 0; i < drivers.Length; i++)
        {
            LocalAvatarIKDriver driver = drivers[i];
            if (driver == null || !driver.isActiveAndEnabled)
                continue;
            if (driver.transform.IsChildOf(transform))
                continue;

            localDriver = driver;
            break;
        }

        if (localDriver == null)
        {
            GameObject sceneAvatar = GameObject.Find("AvatarPlayerVisual");
            if (sceneAvatar != null)
                localDriver = sceneAvatar.GetComponent<LocalAvatarIKDriver>();
        }

        if (localDriver != null)
        {
            localBodyRoot = localDriver.bodyRoot != null
                ? localDriver.bodyRoot
                : localDriver.transform;
            localHeadTarget = localDriver.headTarget;
            localLeftHandTarget = localDriver.leftHandTarget;
            localRightHandTarget = localDriver.rightHandTarget;
        }

        ownerSourcesResolved = true;

        Debug.Log(
            $"[NET AVATAR OWNER] resolved IsOwner={IsOwner} " +
            $"driver={(localDriver != null)} " +
            $"bodySource={(localBodyRoot != null)} " +
            $"headSource={(localHeadTarget != null)} " +
            $"leftSource={(localLeftHandTarget != null)} " +
            $"rightSource={(localRightHandTarget != null)}"
        );
    }

    private void EnsureRemotePoseDriver()
    {
        if (remoteAvatarVisual == null)
            return;

        RemoteAvatarPoseDriver driver =
            remoteAvatarVisual.GetComponent<RemoteAvatarPoseDriver>();
        if (driver == null)
            driver = remoteAvatarVisual.AddComponent<RemoteAvatarPoseDriver>();

        driver.Bind(
            bodyVisual != null ? bodyVisual.transform : null,
            headVisual != null ? headVisual.transform : null,
            leftHandVisual != null ? leftHandVisual.transform : null,
            rightHandVisual != null ? rightHandVisual.transform : null,
            remoteAvatarVisual.transform,
            remoteHeadTarget,
            remoteLeftHandTarget,
            remoteRightHandTarget
        );
        driver.enabled = true;
    }

    private void ApplyRoleOuterwear()
    {
        bool ownerIsHost =
            NetworkManager != null &&
            OwnerClientId == NetworkManager.ServerClientId;

        Material material = ownerIsHost
            ? hostOuterwearMaterial
            : clientOuterwearMaterial;

        if (material == null)
        {
            Debug.LogWarning(
                $"[NET AVATAR] Outerwear material missing. " +
                $"ownerIsHost={ownerIsHost} OwnerClientId={OwnerClientId}"
            );
            return;
        }

        Transform searchRoot = remoteAvatarVisual != null
            ? remoteAvatarVisual.transform
            : transform;

        int applied = 0;
        Renderer[] renderers = searchRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !IsOuterwearRenderer(renderer))
                continue;

            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                renderer.sharedMaterial = material;
            }
            else
            {
                materials[0] = material;
                renderer.sharedMaterials = materials;
            }

            applied++;
        }

        Debug.Log(
            $"[NET AVATAR] Outerwear applied | " +
            $"ownerIsHost={ownerIsHost} | material={material.name} | count={applied}"
        );
    }

    private static bool IsOuterwearRenderer(Renderer renderer)
    {
        string name = renderer.gameObject.name;
        if (string.IsNullOrEmpty(name))
            return false;

        return name.IndexOf("Outerwear", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Outwear", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private GameObject FindDirectChild(string childName)
    {
        Transform child = transform.Find(childName);
        return child != null ? child.gameObject : null;
    }

    private static Transform FindDeepChild(Transform parent, string childName)
    {
        if (parent.name == childName)
            return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindDeepChild(parent.GetChild(i), childName);
            if (found != null)
                return found;
        }

        return null;
    }

    private void LogOwnerDebug()
    {
        if (Time.unscaledTime < nextOwnerDebugTime)
            return;

        nextOwnerDebugTime = Time.unscaledTime + 0.5f;

        Vector3 bodySourcePos = localBodyRoot != null ? localBodyRoot.position : Vector3.zero;
        Vector3 leftSourcePos = localLeftHandTarget != null
            ? localLeftHandTarget.position
            : Vector3.zero;

        Debug.Log(
            $"[NET AVATAR OWNER] IsOwner={IsOwner} " +
            $"bodySource={(localBodyRoot != null)} " +
            $"headSource={(localHeadTarget != null)} " +
            $"leftSource={(localLeftHandTarget != null)} " +
            $"rightSource={(localRightHandTarget != null)} " +
            $"bodySourcePos={bodySourcePos} " +
            $"networkBodyPos={bodyPosition.Value} " +
            $"leftSourcePos={leftSourcePos} " +
            $"networkLeftPos={leftHandPosition.Value}"
        );
    }

    private void DisableRemoteLocalDrivers()
    {
        if (remoteAvatarVisual == null)
            return;

        LocalAvatarIKDriver[] drivers =
            remoteAvatarVisual.GetComponentsInChildren<LocalAvatarIKDriver>(true);

        for (int i = 0; i < drivers.Length; i++)
        {
            if (drivers[i] != null)
                drivers[i].enabled = false;
        }
    }

    private void HideProxyRenderers()
    {
        SetRenderersEnabled(headVisual, false);
        SetRenderersEnabled(leftHandVisual, false);
        SetRenderersEnabled(rightHandVisual, false);
        DisableColliders(headVisual);
        DisableColliders(leftHandVisual);
        DisableColliders(rightHandVisual);
    }

    private static void SetRenderersEnabled(GameObject root, bool enabled)
    {
        if (root == null)
            return;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = enabled;
        }
    }

    private static void DisableColliders(GameObject root)
    {
        if (root == null)
            return;

        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }
    }
}
