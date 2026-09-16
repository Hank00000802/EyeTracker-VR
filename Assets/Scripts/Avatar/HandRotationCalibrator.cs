using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Temporary local-only hand rotation calibration UI.
/// Remove or disable after copying final XYZ into LocalAvatarIKDriver.
/// </summary>
public class HandRotationCalibrator : MonoBehaviour
{
    [SerializeField] private LocalAvatarIKDriver ikDriver;
    [SerializeField] private bool showCalibrationUi;

    bool m_rightHand;
    int m_axis;
    GameObject m_calibRoot;
    TMP_Text m_toggleLabel;
    TMP_Text m_offsetText;
    Button m_leftButton;
    Button m_rightButton;
    readonly Button[] m_axisButtons = new Button[3];

    private void Start()
    {
        if (ikDriver == null)
        {
            LocalAvatarIKDriver[] drivers =
                FindObjectsByType<LocalAvatarIKDriver>(FindObjectsSortMode.None);
            for (int i = 0; i < drivers.Length; i++)
            {
                if (drivers[i] != null && drivers[i].isActiveAndEnabled)
                {
                    ikDriver = drivers[i];
                    break;
                }
            }
        }

        if (ikDriver == null)
            Debug.LogWarning("[HandCalib] LocalAvatarIKDriver not found.");

        BuildToggleButton();
        BuildUi();
        SetCalibrationUiVisible(showCalibrationUi);
        RefreshUi();
    }

    void BuildToggleButton()
    {
        Button toggle = CreateButton(
            transform,
            showCalibrationUi ? "Hide Hand Calib" : "Hand Calib",
            new Vector2(0f, -200f),
            ToggleCalibrationUi,
            new Vector2(180f, 40f));
        toggle.gameObject.name = "HandCalibToggle";
        m_toggleLabel = toggle.GetComponentInChildren<TMP_Text>();
    }

    void ToggleCalibrationUi()
    {
        SetCalibrationUiVisible(m_calibRoot == null || !m_calibRoot.activeSelf);
    }

    void SetCalibrationUiVisible(bool visible)
    {
        showCalibrationUi = visible;
        if (m_calibRoot != null)
            m_calibRoot.SetActive(visible);

        if (m_toggleLabel != null)
            m_toggleLabel.text = visible ? "Hide Hand Calib" : "Hand Calib";
    }

    void BuildUi()
    {
        var root = new GameObject("HandRotationCalibUI", typeof(RectTransform));
        root.transform.SetParent(transform, false);
        m_calibRoot = root;

        var rootRt = root.GetComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0.5f, 0.5f);
        rootRt.anchorMax = new Vector2(0.5f, 0.5f);
        rootRt.pivot = new Vector2(0.5f, 0.5f);
        rootRt.anchoredPosition = new Vector2(0f, -320f);
        rootRt.sizeDelta = new Vector2(520f, 220f);

        m_offsetText = CreateLabel(root.transform, "OffsetText", new Vector2(0f, 86f), new Vector2(500f, 40f));

        m_leftButton = CreateButton(root.transform, "L", new Vector2(-70f, 42f), () =>
        {
            m_rightHand = false;
            RefreshUi();
        });
        m_rightButton = CreateButton(root.transform, "R", new Vector2(70f, 42f), () =>
        {
            m_rightHand = true;
            RefreshUi();
        });

        string[] axes = { "X", "Y", "Z" };
        float[] xs = { -120f, 0f, 120f };
        for (int i = 0; i < 3; i++)
        {
            int axis = i;
            m_axisButtons[i] = CreateButton(
                root.transform,
                axes[i],
                new Vector2(xs[i], 0f),
                () =>
                {
                    m_axis = axis;
                    RefreshUi();
                });
        }

        CreateButton(root.transform, "+10", new Vector2(-150f, -42f), () => Nudge(10f));
        CreateButton(root.transform, "-10", new Vector2(-50f, -42f), () => Nudge(-10f));
        CreateButton(root.transform, "+1", new Vector2(50f, -42f), () => Nudge(1f));
        CreateButton(root.transform, "-1", new Vector2(150f, -42f), () => Nudge(-1f));
        CreateButton(root.transform, "Reset", new Vector2(0f, -84f), ResetSelected);
    }

    void Nudge(float degrees)
    {
        if (ikDriver == null)
            return;

        Vector3 delta = Vector3.zero;
        delta[m_axis] = degrees;
        ikDriver.AddHandRotationOffset(m_rightHand, delta);
        RefreshUi();
    }

    void ResetSelected()
    {
        if (ikDriver == null)
            return;

        ikDriver.ResetHandRotationOffset(m_rightHand);
        RefreshUi();
    }

    void RefreshUi()
    {
        if (ikDriver == null || m_offsetText == null)
            return;

        Vector3 left = ikDriver.LeftHandRotationOffset;
        Vector3 right = ikDriver.RightHandRotationOffset;
        m_offsetText.text =
            $"L  X:{left.x:0}  Y:{left.y:0}  Z:{left.z:0}\n" +
            $"R  X:{right.x:0}  Y:{right.y:0}  Z:{right.z:0}";

        SetSelected(m_leftButton, !m_rightHand);
        SetSelected(m_rightButton, m_rightHand);
        for (int i = 0; i < m_axisButtons.Length; i++)
            SetSelected(m_axisButtons[i], m_axis == i);
    }

    static void SetSelected(Button button, bool selected)
    {
        if (button == null)
            return;

        var image = button.GetComponent<Image>();
        if (image != null)
            image.color = selected
                ? new Color(0.2f, 0.45f, 0.75f, 0.95f)
                : new Color(0.18f, 0.18f, 0.18f, 0.9f);
    }

    static TMP_Text CreateLabel(Transform parent, string name, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        var text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = 18;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;
        return text;
    }

    static Button CreateButton(
        Transform parent,
        string label,
        Vector2 pos,
        UnityEngine.Events.UnityAction onClick,
        Vector2 size = default)
    {
        var go = new GameObject(label, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = size == default ? new Vector2(88f, 36f) : size;

        var image = go.AddComponent<Image>();
        image.color = new Color(0.18f, 0.18f, 0.18f, 0.9f);
        image.raycastTarget = true;

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        var text = textGo.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 18;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;

        return button;
    }
}
