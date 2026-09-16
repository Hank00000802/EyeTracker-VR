using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using TMPro;

public enum ExperimentPhase : byte
{
    Waiting = 0,

    DailyDiscussion = 1,

    ProfessionalIntroduction = 2,

    PracticeADraw = 3,

    PracticeBDraw = 4,

    PracticeDiscussion = 5,

    PracticeNarration = 6,

    StoryADraw = 7,

    StoryBDraw = 8,

    StoryDiscussion = 9,

    StoryNarration = 10,

    Finished = 11
}

public enum ExperimentPart : byte
{
    Practice = 0,

    Part1 = 1,

    Part2 = 2
}

public class ExperimentFlowManager : NetworkBehaviour
{
    [Header("Shared Task UI")]
    [SerializeField] private GameObject taskScreenRoot;

    [SerializeField] private TMP_Text taskTitleText;
    [SerializeField] private TMP_Text promptText;
    [SerializeField] private TMP_Text instructionText;
    [SerializeField] private TMP_Text timerText;

    [SerializeField] private GameObject storyInteractionPanel;

    [Header("Part Selection (reuse existing TaskScreen buttons)")]
    [SerializeField] private Button practiceButton;
    [SerializeField] private Button part1Button;
    [SerializeField] private Button part2Button;

    [Header("Story Board")]
    [SerializeField] private StoryCardBoardManager storyCardBoardManager;
    [SerializeField] private StoryCardTaskManager storyCardTaskManager;

    [Header("Official Story Selection")]
    [SerializeField] private GameObject selectionStoryPanel;
    [SerializeField] private StorySelectionPanelView storySelectionPanelView;

    [Header("Task Durations")]
    [SerializeField] private float dailyDiscussionDuration = 120f;
    [SerializeField] private float professionalIntroductionDuration = 120f;
    [SerializeField] private float storyDiscussionDuration = 120f;

    [Header("Timer Warning")]
    [SerializeField, Min(0f)] private float lastTimerVisibleSeconds = 20f;
    [SerializeField] private string lastTimerMessage = "{0} seconds remaining. Please wrap it up.";

    [Header("Debug / Researcher Control")]
    [SerializeField] private GameObject nextPhaseButton;

    [Header("Eye Recording Control")]
    [SerializeField] private StudySessionManager studySessionManager;
    [SerializeField] private GameObject startRecordingButton;
    [SerializeField] private GameObject stopRecordingButton;

    private readonly NetworkVariable<ExperimentPhase> currentPhase =
        new NetworkVariable<ExperimentPhase>(
            ExperimentPhase.Waiting,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private readonly NetworkVariable<ExperimentPart> currentPart =
        new NetworkVariable<ExperimentPart>(
            ExperimentPart.Part1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private readonly NetworkVariable<double> phaseEndServerTime =
        new NetworkVariable<double>(
            -1d,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private readonly NetworkVariable<int> selectedStoryIndex =
        new NetworkVariable<int>(
            -1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private readonly NetworkVariable<bool> isSelectingStory =
        new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private bool timerExpired;

    public ExperimentPhase CurrentPhase => currentPhase.Value;

    public ExperimentPart CurrentPart => currentPart.Value;

    public bool IsSelectingStory => isSelectingStory.Value;

    public int SelectedStoryIndex => selectedStoryIndex.Value;

    /// <summary>
    /// Combined label for CSV / debug, e.g. DailyDiscussion_Part1.
    /// </summary>
    public string PhaseAndPartLabel
    {
        get
        {
            if (IsOfficialStoryPhase(currentPhase.Value) &&
                selectedStoryIndex.Value >= 0)
            {
                return
                    $"{currentPhase.Value}_{currentPart.Value}_Story{selectedStoryIndex.Value + 1}";
            }

            return $"{currentPhase.Value}_{currentPart.Value}";
        }
    }

    public bool IsParticipantA =>
        NetworkManager != null &&
        NetworkManager.IsHost;

    public bool IsParticipantB =>
        NetworkManager != null &&
        NetworkManager.IsClient &&
        !NetworkManager.IsHost;

    private void Awake()
    {
        if (selectionStoryPanel == null && taskScreenRoot != null)
        {
            Transform found = FindChildByName(
                taskScreenRoot.transform,
                "SelectionStoryPanel");
            if (found != null)
                selectionStoryPanel = found.gameObject;
        }

        if (selectionStoryPanel == null)
        {
            GameObject named = GameObject.Find("SelectionStoryPanel");
            if (named != null)
                selectionStoryPanel = named;
        }

        if (selectionStoryPanel != null)
            selectionStoryPanel.SetActive(false);
    }

    public override void OnNetworkSpawn()
    {
        currentPhase.OnValueChanged += OnPhaseChanged;
        currentPart.OnValueChanged += OnPartChanged;
        selectedStoryIndex.OnValueChanged += OnSelectedStoryChanged;
        isSelectingStory.OnValueChanged += OnSelectingStoryChanged;

        WirePartButtons();
        ResolveStorySelectionPanel();

        if (storyCardTaskManager == null)
            storyCardTaskManager = FindFirstObjectByType<StoryCardTaskManager>();

        ApplyPhase(currentPhase.Value);
        ApplySelectedStorySprites();
        ApplyStorySelectionVisuals();
        ConfigureNextPhaseButtonHover();
        UpdateNextPhaseButtonVisibility();

        if (startRecordingButton != null)
            startRecordingButton.SetActive(IsServer);

        if (stopRecordingButton != null)
            stopRecordingButton.SetActive(IsServer);

        Debug.Log(
            $"[ExperimentFlow] Spawned | " +
            $"Phase={currentPhase.Value} | Part={currentPart.Value} | " +
            $"A={IsParticipantA} | " +
            $"B={IsParticipantB}"
        );
    }

    public override void OnNetworkDespawn()
    {
        currentPhase.OnValueChanged -= OnPhaseChanged;
        currentPart.OnValueChanged -= OnPartChanged;
        selectedStoryIndex.OnValueChanged -= OnSelectedStoryChanged;
        isSelectingStory.OnValueChanged -= OnSelectingStoryChanged;

        UnwirePartButtons();
    }

    private void WirePartButtons()
    {
        if (practiceButton != null)
            practiceButton.onClick.AddListener(HostSelectPractice);

        if (part1Button != null)
            part1Button.onClick.AddListener(HostSelectPart1);

        if (part2Button != null)
            part2Button.onClick.AddListener(HostSelectPart2);
    }

    private void UnwirePartButtons()
    {
        if (practiceButton != null)
            practiceButton.onClick.RemoveListener(HostSelectPractice);

        if (part1Button != null)
            part1Button.onClick.RemoveListener(HostSelectPart1);

        if (part2Button != null)
            part2Button.onClick.RemoveListener(HostSelectPart2);
    }

    private void OnPhaseChanged(
        ExperimentPhase previous,
        ExperimentPhase current)
    {
        Debug.Log(
            $"[FLOW] Phase transition: {previous} -> {current}");

        Debug.Log(
            $"[ExperimentFlow] Phase changed: " +
            $"{previous} -> {current}"
        );

        ApplyPhase(current);
    }

    private void OnPartChanged(
        ExperimentPart previous,
        ExperimentPart current)
    {
        Debug.Log(
            $"[FLOW] Part transition: {previous} -> {current}");

        ApplyPhase(currentPhase.Value);
    }

    private void OnSelectedStoryChanged(int previous, int current)
    {
        ApplySelectedStorySprites();
    }

    private void OnSelectingStoryChanged(bool previous, bool current)
    {
        ApplyStorySelectionVisuals();
    }

    private void ApplySelectedStorySprites()
    {
        if (storyCardBoardManager == null)
            return;

        storyCardBoardManager.ApplySpritesForCurrentSelection(
            currentPhase.Value,
            currentPart.Value,
            selectedStoryIndex.Value);
    }

    private void ApplyStorySelectionVisuals()
    {
        bool selecting = isSelectingStory.Value &&
                         IsOfficialStoryPhase(currentPhase.Value);

        if (storyCardBoardManager != null)
            storyCardBoardManager.SetBoardHidden(selecting);

        ResolveStorySelectionPanel();

        if (!selecting || !IsServer)
        {
            if (storySelectionPanelView != null)
                storySelectionPanelView.Hide();
            else if (selectionStoryPanel != null)
                selectionStoryPanel.SetActive(false);

            return;
        }

        var names = new System.Collections.Generic.List<string>();
        int count = storyCardBoardManager != null
            ? storyCardBoardManager.OfficialStoryCount
            : 0;
        for (int i = 0; i < count; i++)
            names.Add(storyCardBoardManager.GetOfficialStoryName(i));

        if (storySelectionPanelView != null)
            storySelectionPanelView.ShowStories(names, HostSelectOfficialStory);
        else if (selectionStoryPanel != null)
            selectionStoryPanel.SetActive(true);
    }

    private void ResolveStorySelectionPanel()
    {
        if (selectionStoryPanel == null && taskScreenRoot != null)
        {
            Transform found = FindChildByName(
                taskScreenRoot.transform,
                "SelectionStoryPanel");
            if (found != null)
                selectionStoryPanel = found.gameObject;
        }

        if (selectionStoryPanel == null)
        {
            GameObject named = GameObject.Find("SelectionStoryPanel");
            if (named != null)
                selectionStoryPanel = named;
        }

        if (selectionStoryPanel == null && taskScreenRoot != null)
            selectionStoryPanel = CreateFallbackSelectionPanel();

        if (selectionStoryPanel == null)
            return;

        if (storySelectionPanelView == null)
            storySelectionPanelView =
                selectionStoryPanel.GetComponent<StorySelectionPanelView>();

        if (storySelectionPanelView == null)
            storySelectionPanelView =
                selectionStoryPanel.AddComponent<StorySelectionPanelView>();

        storySelectionPanelView.BindPanel(selectionStoryPanel);

        if (!IsServer || !isSelectingStory.Value)
            selectionStoryPanel.SetActive(false);
    }

    private GameObject CreateFallbackSelectionPanel()
    {
        GameObject panel = new GameObject(
            "SelectionStoryPanel",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        panel.transform.SetParent(taskScreenRoot.transform, false);

        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(40f, 80f);
        rect.offsetMax = new Vector2(-40f, -80f);

        Image image = panel.GetComponent<Image>();
        image.color = new Color(0.08f, 0.08f, 0.08f, 0.92f);
        image.raycastTarget = true;

        panel.SetActive(false);
        return panel;
    }

    private static Transform FindChildByName(Transform root, string name)
    {
        if (root == null)
            return null;

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] != null && children[i].name == name)
                return children[i];
        }

        return null;
    }

    public void StartExperiment()
    {
        if (!IsServer)
            return;

        SetPhase(ExperimentPhase.DailyDiscussion);
    }

    public void NextPhase()
    {
        if (!IsServer)
            return;

        ExperimentPhase next =
            currentPhase.Value switch
            {
                ExperimentPhase.Waiting =>
                    ExperimentPhase.DailyDiscussion,

                ExperimentPhase.DailyDiscussion =>
                    ExperimentPhase.ProfessionalIntroduction,

                ExperimentPhase.ProfessionalIntroduction =>
                    ExperimentPhase.PracticeADraw,

                ExperimentPhase.PracticeADraw =>
                    ExperimentPhase.PracticeBDraw,

                ExperimentPhase.PracticeBDraw =>
                    ExperimentPhase.PracticeDiscussion,

                ExperimentPhase.PracticeDiscussion =>
                    ExperimentPhase.PracticeNarration,

                ExperimentPhase.PracticeNarration =>
                    ExperimentPhase.StoryADraw,

                ExperimentPhase.StoryADraw =>
                    ExperimentPhase.StoryBDraw,

                ExperimentPhase.StoryBDraw =>
                    ExperimentPhase.StoryDiscussion,

                ExperimentPhase.StoryDiscussion =>
                    ExperimentPhase.StoryNarration,

                ExperimentPhase.StoryNarration =>
                    ExperimentPhase.Finished,

                _ =>
                    ExperimentPhase.Finished
            };

        SetPhase(next);
    }

    public void HostSelectPractice()
    {
        if (IsServer)
            isSelectingStory.Value = false;

        SetPhase(ExperimentPhase.PracticeADraw);
    }

    public void HostSelectPart1()
    {
        HostSelectOfficialPart(ExperimentPart.Part1);
    }

    public void HostSelectPart2()
    {
        HostSelectOfficialPart(ExperimentPart.Part2);
    }

    private void HostSelectOfficialPart(ExperimentPart part)
    {
        if (!IsServer)
        {
            Debug.LogWarning(
                "[ExperimentFlow] Only Server/Host can change part."
            );
            return;
        }

        if (currentPart.Value != part)
            currentPart.Value = part;

        if (!IsOfficialStoryPhase(currentPhase.Value))
            return;

        bool wasSelecting = isSelectingStory.Value;
        isSelectingStory.Value = true;
        if (wasSelecting)
            ApplyStorySelectionVisuals();
    }

    public void HostSelectOfficialStory(int storyIndex)
    {
        if (!IsServer)
            return;

        if (storyCardBoardManager == null ||
            storyIndex < 0 ||
            storyIndex >= storyCardBoardManager.OfficialStoryCount)
        {
            Debug.LogWarning(
                $"[ExperimentFlow] Invalid official story index: {storyIndex}");
            return;
        }

        int previous = selectedStoryIndex.Value;
        selectedStoryIndex.Value = storyIndex;
        if (previous == storyIndex)
            ApplySelectedStorySprites();

        isSelectingStory.Value = false;
    }

    public void SetPart(ExperimentPart part)
    {
        if (!IsServer)
        {
            Debug.LogWarning(
                "[ExperimentFlow] Only Server/Host can change part."
            );
            return;
        }

        if (part == ExperimentPart.Practice)
        {
            SetPhase(ExperimentPhase.PracticeADraw);
            return;
        }

        if (currentPart.Value == part)
            return;

        currentPart.Value = part;
    }

    public void HostStartRecording()
    {
        if (!IsServer)
            return;

        BroadcastStartRecordingRpc();
    }

    public void HostStopRecording()
    {
        if (!IsServer)
            return;

        BroadcastStopRecordingRpc();
    }

    [Rpc(SendTo.Everyone)]
    private void BroadcastStartRecordingRpc()
    {
        if (studySessionManager != null)
            studySessionManager.StartStudyRecordingFromNetwork();
    }

    [Rpc(SendTo.Everyone)]
    private void BroadcastStopRecordingRpc()
    {
        if (studySessionManager != null)
            studySessionManager.StopStudyRecordingFromNetwork();
    }

    public void SetPhase(ExperimentPhase phase)
    {
        if (!IsServer)
        {
            Debug.LogWarning(
                "[ExperimentFlow] Only Server/Host can change phase."
            );

            return;
        }

        if (IsOfficialStoryPhase(phase) &&
            currentPart.Value == ExperimentPart.Practice)
        {
            currentPart.Value = ExperimentPart.Part1;
        }

        float duration = GetPhaseDuration(phase);

        if (duration > 0f)
        {
            phaseEndServerTime.Value =
                NetworkManager.ServerTime.Time + duration;
        }
        else
        {
            phaseEndServerTime.Value = -1d;
        }

        timerExpired = false;

        if (!IsOfficialStoryPhase(phase) && isSelectingStory.Value)
            isSelectingStory.Value = false;

        ExperimentPhase previousPhase = currentPhase.Value;
        currentPhase.Value = phase;

        Debug.Log(
            $"[FLOW] Phase transition: {previousPhase} -> {phase}");
    }

    private float GetPhaseDuration(ExperimentPhase phase)
    {
        return phase switch
        {
            ExperimentPhase.DailyDiscussion =>
                dailyDiscussionDuration,

            ExperimentPhase.ProfessionalIntroduction =>
                professionalIntroductionDuration,

            ExperimentPhase.PracticeDiscussion =>
                storyDiscussionDuration,

            ExperimentPhase.StoryDiscussion =>
                storyDiscussionDuration,

            _ => 0f
        };
    }

    private void Update()
    {
        if (!IsSpawned)
            return;

        UpdateTimerDisplay();
        UpdateNextPhaseButtonVisibility();
    }

    private void UpdateTimerDisplay()
    {
        if (IsNarrationPhase(currentPhase.Value))
        {
            UpdateNarrationCountUpDisplay();
            return;
        }

        float duration = GetPhaseDuration(currentPhase.Value);

        if (duration <= 0f || phaseEndServerTime.Value < 0d)
        {
            HideTimerText();
            return;
        }

        double remaining =
            phaseEndServerTime.Value -
            NetworkManager.ServerTime.Time;

        if (remaining > 0d)
        {
            timerExpired = false;
            ShowLastSecondsWarning(remaining);
            return;
        }

        timerExpired = true;
        HideTimerText();
    }

    private void UpdateNarrationCountUpDisplay()
    {
        if (storyCardTaskManager == null ||
            !storyCardTaskManager.TryGetNarrationRemainingSeconds(out double remaining))
        {
            HideTimerText();
            return;
        }

        ShowLastSecondsWarning(remaining);
    }

    private void ShowLastSecondsWarning(double remaining)
    {
        float visibleWindow = Mathf.Max(0f, lastTimerVisibleSeconds);
        if (remaining <= 0d || remaining > visibleWindow)
        {
            HideTimerText();
            return;
        }

        int seconds = Mathf.Max(1, Mathf.CeilToInt((float)remaining));
        const string fallback = "{0} seconds remaining. Please wrap it up.";
        string template = string.IsNullOrWhiteSpace(lastTimerMessage)
            ? fallback
            : lastTimerMessage;
        string message = template.Contains("{0}")
            ? template.Replace("{0}", seconds.ToString())
            : template;

        if (timerText == null)
            return;

        timerText.richText = false;
        if (!timerText.gameObject.activeSelf)
            timerText.gameObject.SetActive(true);

        timerText.text = message;
    }

    private void HideTimerText()
    {
        if (timerText != null && timerText.gameObject.activeSelf)
            timerText.gameObject.SetActive(false);
    }

    private void UpdateNextPhaseButtonVisibility()
    {
        if (nextPhaseButton == null)
            return;

        bool shouldShow = false;

        if (IsServer)
        {
            ExperimentPhase phase = currentPhase.Value;
            if (IsDrawAPhase(phase) || IsDrawBPhase(phase))
            {
                shouldShow = false;
            }
            else if (IsNarrationPhase(phase))
            {
                shouldShow =
                    storyCardTaskManager != null &&
                    storyCardTaskManager.IsOnLastNarrationCard &&
                    storyCardTaskManager.IsNarrationUnlockElapsed();
            }
            else
            {
                bool timedPhase =
                    GetPhaseDuration(phase) > 0f &&
                    phaseEndServerTime.Value >= 0d;

                shouldShow = !timedPhase || timerExpired;
            }
        }

        if (nextPhaseButton.activeSelf != shouldShow)
            nextPhaseButton.SetActive(shouldShow);
    }

    private void ConfigureNextPhaseButtonHover()
    {
        if (nextPhaseButton == null)
            return;

        Button button = nextPhaseButton.GetComponent<Button>();
        if (button == null)
            return;

        Navigation navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;

        ColorBlock colors = button.colors;
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;
    }

    public static bool IsPracticePhase(ExperimentPhase phase)
    {
        return phase == ExperimentPhase.PracticeADraw ||
               phase == ExperimentPhase.PracticeBDraw ||
               phase == ExperimentPhase.PracticeDiscussion ||
               phase == ExperimentPhase.PracticeNarration;
    }

    public static bool IsOfficialStoryPhase(ExperimentPhase phase)
    {
        return phase == ExperimentPhase.StoryADraw ||
               phase == ExperimentPhase.StoryBDraw ||
               phase == ExperimentPhase.StoryDiscussion ||
               phase == ExperimentPhase.StoryNarration;
    }

    public static bool IsStoryPhase(ExperimentPhase phase)
    {
        return IsPracticePhase(phase) || IsOfficialStoryPhase(phase);
    }

    public static bool IsDrawAPhase(ExperimentPhase phase)
    {
        return phase == ExperimentPhase.PracticeADraw ||
               phase == ExperimentPhase.StoryADraw;
    }

    public static bool IsDrawBPhase(ExperimentPhase phase)
    {
        return phase == ExperimentPhase.PracticeBDraw ||
               phase == ExperimentPhase.StoryBDraw;
    }

    public static bool IsDiscussionPhase(ExperimentPhase phase)
    {
        return phase == ExperimentPhase.PracticeDiscussion ||
               phase == ExperimentPhase.StoryDiscussion;
    }

    public static bool IsNarrationPhase(ExperimentPhase phase)
    {
        return phase == ExperimentPhase.PracticeNarration ||
               phase == ExperimentPhase.StoryNarration;
    }

    private void ApplyPhase(ExperimentPhase phase)
    {
        Debug.Log($"[FLOW] Phase={phase} Part={currentPart.Value}");

        if (taskScreenRoot != null)
            taskScreenRoot.SetActive(true);

        bool isStoryPhase = IsStoryPhase(phase);

        if (promptText != null)
            promptText.gameObject.SetActive(!isStoryPhase);

        if (instructionText != null)
            instructionText.gameObject.SetActive(!isStoryPhase);

        if (storyInteractionPanel != null)
            storyInteractionPanel.SetActive(isStoryPhase);

        UpdatePartButtonVisibility(phase);
        UpdateNextPhaseButtonVisibility();
        ApplyContentForPhaseAndPart(phase, currentPart.Value);

        if (storyCardBoardManager != null)
        {
            storyCardBoardManager.ApplySpritesForCurrentSelection(
                phase,
                currentPart.Value,
                selectedStoryIndex.Value);
            storyCardBoardManager.ApplyButtonColorsForPhase(phase);
            storyCardBoardManager.SetBoardHidden(
                isSelectingStory.Value && IsOfficialStoryPhase(phase));
        }
    }

    private void UpdatePartButtonVisibility(ExperimentPhase phase)
    {
        bool showPartButtons = IsOfficialStoryPhase(phase);

        if (practiceButton != null)
        {
            practiceButton.gameObject.SetActive(false);
            practiceButton.interactable = false;
        }

        if (part1Button != null)
        {
            part1Button.gameObject.SetActive(showPartButtons);
            part1Button.interactable = IsServer && showPartButtons;
        }

        if (part2Button != null)
        {
            part2Button.gameObject.SetActive(showPartButtons);
            part2Button.interactable = IsServer && showPartButtons;
        }
    }

    /// <summary>
    /// Backend content lookup. Reuses the same Title/Prompt/Instruction UI.
    /// Placeholder Part2 / Practice text until final copy is provided.
    /// </summary>
    private void ApplyContentForPhaseAndPart(
        ExperimentPhase phase,
        ExperimentPart part)
    {
        switch (phase)
        {
            case ExperimentPhase.Waiting:

                SetTaskText(
                    "Waiting",
                    "Waiting for both participants.",
                    "Please wait for the experiment to begin."
                );

                break;

            case ExperimentPhase.DailyDiscussion:

                if (part == ExperimentPart.Part2)
                {
                    SetTaskText(
                        "Daily-life Discussion (Part 2)",
                        "Question 1: How would you feel if you couldn’t use your phone for one day? \n"+
                        "Question 2: What is your favorite place you have ever been to? \n"+
                        "Question 3: How do you relieve your stress? \n",
                        "Discuss the topic together."
                    );
                }
                else
                {
                    // Existing copy treated as Part1 (also used if Practice somehow selected).
                    SetTaskText(
                        "Daily-life Discussion (Part 1)",
                        "Question 1: What do you like to do in your free time? Why?\n" +
                        "Question 2: What is your favorite food? Describe it\n" +
                        "Question 3: Do you usually cook at home or buy food from outside?\n" +
                        "If you cook, what do you usually make?\n" +
                        "If you buy food from outside, what is your go-to choice?",
                        "Discuss the topic together."
                    );
                }

                break;

            case ExperimentPhase.ProfessionalIntroduction:

                if (part == ExperimentPart.Part2)
                {
                    SetTaskText(
                        "Professional Self-introduction (Part 2)",
                        "Question 1: Could you briefly introduce your major?\n" +
                        "Question 2: Is what you have learned in your major helpful to you? Why or why not?\n" +
                        "Question 3: What kind of job would you like to have after earning your degree? Why?",
                        "Introduce yourselves in turn and interact with your partner."
                    );
                }
                else
                {
                    SetTaskText(
                        "Professional Self-introduction (Part 1)",
                        "Question 1: What is your major, and what year are you in?\n" +
                        "Question 2: What is your favorite class in your department? Why?\n" +
                        "Question 3: What is your least favorite class in your department ? Why?",
                        "Introduce yourselves in turn and interact with your partner."
                    );
                }

                break;

            case ExperimentPhase.PracticeADraw:
            case ExperimentPhase.StoryADraw:

                SetTaskText(
                    StoryTitleForPhase(phase, part),
                    $"{PartTagForPhase(phase, part)} Participant A: Draw your cards.",
                    "Participant B, please wait."
                );

                break;

            case ExperimentPhase.PracticeBDraw:
            case ExperimentPhase.StoryBDraw:

                SetTaskText(
                    StoryTitleForPhase(phase, part),
                    $"{PartTagForPhase(phase, part)} Participant B: Draw your cards.",
                    "Participant A, please wait."
                );

                break;

            case ExperimentPhase.PracticeDiscussion:
            case ExperimentPhase.StoryDiscussion:

                SetTaskText(
                    StoryTitleForPhase(phase, part),
                    $"{PartTagForPhase(phase, part)} Discuss the four cards together.",
                    "Use all four cards to plan your story."
                );

                break;

            case ExperimentPhase.PracticeNarration:
            case ExperimentPhase.StoryNarration:

                SetTaskText(
                    StoryTitleForPhase(phase, part),
                    $"{PartTagForPhase(phase, part)} Tell the story in the order 1 → 2 → 3 → 4.",
                    "A → B → A → B"
                );

                break;

            case ExperimentPhase.Finished:

                Debug.Log("[FLOW UI] Showing Finished screen");

                SetTaskText(
                    "Finished",
                    "The task is complete.",
                    "Please wait for the researcher."
                );

                break;
        }
    }

    private static string StoryTitleForPhase(ExperimentPhase phase, ExperimentPart part)
    {
        if (IsPracticePhase(phase))
            return "Story-card Task (Practice)";

        return StoryTitle(part);
    }

    private static string PartTagForPhase(ExperimentPhase phase, ExperimentPart part)
    {
        if (IsPracticePhase(phase))
            return "[Practice]";

        return PartTag(part);
    }

    private static string StoryTitle(ExperimentPart part)
    {
        return part switch
        {
            ExperimentPart.Practice => "Story-card Task (Practice)",
            ExperimentPart.Part2 => "Story-card Task (Part 2)",
            _ => "Story-card Task (Part 1)"
        };
    }

    private static string PartTag(ExperimentPart part)
    {
        return part switch
        {
            ExperimentPart.Practice => "[Practice]",
            ExperimentPart.Part2 => "[Part2]",
            _ => "[Part1]"
        };
    }

    private void SetTaskText(
        string title,
        string prompt,
        string instruction)
    {
        if (taskTitleText != null)
            taskTitleText.text = title;

        if (promptText != null)
            promptText.text = prompt;

        if (instructionText != null)
            instructionText.text = instruction;
    }
}
