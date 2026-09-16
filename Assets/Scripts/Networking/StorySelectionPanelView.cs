using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Host-only story picker. Uses the existing child buttons and layout as authored.
/// Does not create buttons, change labels, or add layout components.
/// </summary>
public class StorySelectionPanelView : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;

    private static readonly Regex TrailingNumber =
        new Regex(@"(\d+)\s*$", RegexOptions.Compiled);

    private Action<int> onStorySelected;
    private bool listenersBound;

    public GameObject PanelRoot => panelRoot;

    public void BindPanel(GameObject panel)
    {
        panelRoot = panel;
        listenersBound = false;
    }

    public void ShowStories(IReadOnlyList<string> storyNames, Action<int> onSelected)
    {
        Show(onSelected);
    }

    public void Show(Action<int> onSelected)
    {
        onStorySelected = onSelected;
        BindExistingButtons();

        if (panelRoot != null)
            panelRoot.SetActive(true);
    }

    public void Hide()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    private void BindExistingButtons()
    {
        if (panelRoot == null || listenersBound)
            return;

        List<Button> buttons = CollectExistingButtons();
        bool boundByName = false;

        for (int i = 0; i < buttons.Count; i++)
        {
            Button button = buttons[i];
            if (button == null)
                continue;

            if (!TryGetStoryIndexFromName(button.name, out int storyIndex))
                continue;

            BindClick(button, storyIndex);
            boundByName = true;
        }

        if (!boundByName)
        {
            for (int i = 0; i < buttons.Count; i++)
                BindClick(buttons[i], i);
        }

        listenersBound = true;
    }

    private List<Button> CollectExistingButtons()
    {
        var result = new List<Button>();
        if (panelRoot == null)
            return result;

        Button[] children = panelRoot.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Button button = children[i];
            if (button == null)
                continue;

            string name = button.name;
            if (name.IndexOf("Close", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Cancel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Back", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            result.Add(button);
        }

        return result;
    }

    private void BindClick(Button button, int storyIndex)
    {
        if (button == null)
            return;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => onStorySelected?.Invoke(storyIndex));
    }

    private static bool TryGetStoryIndexFromName(string objectName, out int storyIndex)
    {
        storyIndex = -1;
        if (string.IsNullOrEmpty(objectName))
            return false;

        Match match = TrailingNumber.Match(objectName);
        if (!match.Success)
            return false;

        if (!int.TryParse(match.Groups[1].Value, out int number) || number < 1)
            return false;

        storyIndex = number - 1;
        return true;
    }
}
