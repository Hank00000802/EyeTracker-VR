using UnityEngine;
using UnityEngine.UI;

public class ShowImageOnButtonPress : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Image targetImage;
    [SerializeField] private GameObject targetObject;

    [Header("Behavior")]
    [SerializeField] private bool hideOnStart = true;

    private void Start()
    {
        if (hideOnStart)
            SetImageVisible(false);
    }

    public void ShowImage()
    {
        SetImageVisible(true);
    }

    public void HideImage()
    {
        SetImageVisible(false);
    }

    public void ToggleImage()
    {
        GameObject visualTarget = GetVisualTarget();
        if (visualTarget == null)
            return;

        SetImageVisible(!visualTarget.activeSelf);
    }

    private void SetImageVisible(bool visible)
    {
        GameObject visualTarget = GetVisualTarget();
        if (visualTarget == null)
        {
            Debug.LogWarning("[ShowImageOnButtonPress] 尚未指定 targetImage 或 targetObject。", this);
            return;
        }

        visualTarget.SetActive(visible);

        Image image = targetImage != null ? targetImage : visualTarget.GetComponent<Image>();
        if (image != null)
            image.enabled = visible;
    }

    private GameObject GetVisualTarget()
    {
        if (targetObject != null)
            return targetObject;

        if (targetImage != null)
            return targetImage.gameObject;

        return null;
    }
}
