using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Makes the UpperBodyClip waist line follow the avatar's animated Hips bone.
///
/// The shader compares the pixel's WORLD height with _WaistHeight. The original shader used
/// object-space height with a fixed 0.8 m, which only works for a standing pose: once the
/// Animator seats the avatar, the whole body is below 0.8 m and gets clipped away.
/// The per-renderer value is set through a MaterialPropertyBlock, so no material asset is modified.
/// </summary>
[DefaultExecutionOrder(1000)]
public class AvatarWaistClipDriver : MonoBehaviour
{
    private const string WaistProperty = "_WaistHeight";
    private static readonly int WaistId = Shader.PropertyToID(WaistProperty);

    [Tooltip("Waist clip height above the Hips bone (m). Captured from the authored bind pose when 'Auto Capture' is on.")]
    [SerializeField] private float waistAboveHips;
    [SerializeField] private bool autoCapture = true;
    [Tooltip("Extra shift of the clip line in meters (positive = hide more of the lower body).")]
    [SerializeField] private float extraOffset;

    private Transform hips;
    private readonly List<Renderer> renderers = new List<Renderer>();
    private MaterialPropertyBlock block;
    private float nextLogTime;

    private void Awake()
    {
        Animator animator = GetComponentInChildren<Animator>();
        if (animator != null)
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);

        if (hips == null)
            hips = FindDeepChild(transform, "Hips");

        float baseWaist = 0.8f;
        bool haveBase = false;

        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null && mats[i].HasProperty(WaistId))
                {
                    renderers.Add(r);
                    if (!haveBase)
                    {
                        baseWaist = mats[i].GetFloat(WaistId);
                        haveBase = true;
                    }
                    break;
                }
            }
        }

        // Awake runs before the Animator's first evaluation, so the bones are still in the
        // pose that was authored in the Editor. The authored waist line was baseWaist above the root.
        if (autoCapture && hips != null)
            waistAboveHips = (transform.position.y + baseWaist) - hips.position.y;

        block = new MaterialPropertyBlock();

        Debug.Log("[WAIST CLIP] renderers=" + renderers.Count +
                  " hips=" + (hips != null ? hips.name : "NULL") +
                  " baseWaist=" + baseWaist.ToString("F3") +
                  " waistAboveHips=" + waistAboveHips.ToString("F3"));
    }

    private void LateUpdate()
    {
        if (hips == null || renderers.Count == 0)
            return;

        float waistWorldY = hips.position.y + waistAboveHips + extraOffset;

        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer r = renderers[i];
            if (r == null)
                continue;

            r.GetPropertyBlock(block);
            block.SetFloat(WaistId, waistWorldY);
            r.SetPropertyBlock(block);
        }

        if (Time.unscaledTime >= nextLogTime)
        {
            nextLogTime = Time.unscaledTime + 2f;
            Debug.Log("[WAIST CLIP] waistWorldY=" + waistWorldY.ToString("F3") +
                      " hipsY=" + hips.position.y.ToString("F3"));
        }
    }

    private static Transform FindDeepChild(Transform parent, string childName)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == childName)
                return child;

            Transform nested = FindDeepChild(child, childName);
            if (nested != null)
                return nested;
        }

        return null;
    }
}
