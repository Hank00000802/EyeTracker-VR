using UnityEngine;
using UnityEngine.UI;

public class ResearcherPanelToggle : MonoBehaviour
{
    [SerializeField] private Button toggleButton;
    [SerializeField] private GameObject panel;
    [SerializeField] private GameObject studyPanel;

    bool m_visible = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureOnNetworkCanvas()
    {
        if (FindFirstObjectByType<ResearcherPanelToggle>() != null)
            return;

        GameObject canvas = GameObject.Find("NetworkCanvas");
        if (canvas != null)
            canvas.AddComponent<ResearcherPanelToggle>();
    }

    void Awake()
    {
        BindReferences();
        if (toggleButton == null)
        {
            Debug.LogWarning("[ResearcherPanelToggle] ReasercherButton not found under NetworkCanvas.");
            return;
        }

        WarnIfButtonInsidePanels();
        m_visible = IsAnyPanelVisible();
        toggleButton.onClick.RemoveListener(TogglePanels);
        toggleButton.onClick.AddListener(TogglePanels);
    }

    void OnDestroy()
    {
        if (toggleButton != null)
            toggleButton.onClick.RemoveListener(TogglePanels);
    }

    public void TogglePanels()
    {
        SetPanelsVisible(!m_visible);
    }

    public void SetPanelsVisible(bool visible)
    {
        m_visible = visible;
        if (panel != null)
            panel.SetActive(visible);
        if (studyPanel != null)
            studyPanel.SetActive(visible);
    }

    void BindReferences()
    {
        Transform canvas = transform;
        if (canvas.name != "NetworkCanvas")
        {
            GameObject found = GameObject.Find("NetworkCanvas");
            if (found != null)
                canvas = found.transform;
        }

        if (toggleButton == null)
            toggleButton = GetComponent<Button>();

        if (toggleButton == null)
        {
            Transform buttonTf = FindChildByNames(canvas, "ReasercherButton", "ResearcherButton");
            if (buttonTf == null)
            {
                GameObject named = GameObject.Find("ReasercherButton");
                if (named == null)
                    named = GameObject.Find("ResearcherButton");
                if (named != null)
                    buttonTf = named.transform;
            }

            if (buttonTf != null)
            {
                toggleButton = buttonTf.GetComponent<Button>();
                if (toggleButton == null)
                    toggleButton = buttonTf.gameObject.AddComponent<Button>();
            }
        }

        if (panel == null)
        {
            Transform panelTf = FindChildByNames(canvas, "Panel");
            if (panelTf != null)
                panel = panelTf.gameObject;
        }

        if (studyPanel == null)
        {
            Transform studyTf = FindChildByNames(canvas, "StudyPanel");
            if (studyTf != null)
                studyPanel = studyTf.gameObject;
        }
    }

    bool IsAnyPanelVisible()
    {
        bool panelOn = panel != null && panel.activeSelf;
        bool studyOn = studyPanel != null && studyPanel.activeSelf;
        return panelOn || studyOn;
    }

    void WarnIfButtonInsidePanels()
    {
        if (toggleButton == null)
            return;

        Transform buttonTf = toggleButton.transform;
        if (IsDescendantOf(buttonTf, panel) || IsDescendantOf(buttonTf, studyPanel))
        {
            Debug.LogWarning(
                "[ResearcherPanelToggle] ReasercherButton is inside Panel/StudyPanel. " +
                "Move it under NetworkCanvas so it stays visible after hiding the panels.");
        }
    }

    static bool IsDescendantOf(Transform child, GameObject ancestor)
    {
        if (child == null || ancestor == null)
            return false;

        Transform current = child;
        while (current != null)
        {
            if (current.gameObject == ancestor)
                return true;
            current = current.parent;
        }

        return false;
    }

    static Transform FindChildByNames(Transform root, params string[] names)
    {
        if (root == null || names == null)
            return null;

        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            for (int n = 0; n < names.Length; n++)
            {
                if (all[i].name == names[n])
                    return all[i];
            }
        }

        return null;
    }
}
