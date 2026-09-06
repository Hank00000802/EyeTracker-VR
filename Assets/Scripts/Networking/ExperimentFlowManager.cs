using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using TMPro;

public enum ExperimentPhase : byte
{
    Waiting = 0,

    DailyDiscussion = 1,

    ProfessionalIntroduction = 2,

    StoryADraw = 3,

    StoryBDraw = 4,

    StoryDiscussion = 5,

    StoryNarration = 6,

    Finished = 7
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

    [Header("Task Durations")]
    [SerializeField] private float dailyDiscussionDuration = 120f;
    [SerializeField] private float professionalIntroductionDuration = 120f;

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

    private bool hasAutoAdvanced;

    public ExperimentPhase CurrentPhase => currentPhase.Value;

    public ExperimentPart CurrentPart => currentPart.Value;

    /// <summary>
    /// Combined label for CSV / debug, e.g. DailyDiscussion_Part1.
    /// </summary>
    public string PhaseAndPartLabel =>
        $"{currentPhase.Value}_{currentPart.Value}";

    public bool IsParticipantA =>
        NetworkManager != null &&
        NetworkManager.IsHost;

    public bool IsParticipantB =>
        NetworkManager != null &&
        NetworkManager.IsClient &&
        !NetworkManager.IsHost;

    public override void OnNetworkSpawn()
    {
        currentPhase.OnValueChanged += OnPhaseChanged;
        currentPart.OnValueChanged += OnPartChanged;

        WirePartButtons();

        ApplyPhase(currentPhase.Value);

        // 開發階段只有 Host 看得到 Next Phase
        if (nextPhaseButton != null)
            nextPhaseButton.SetActive(IsServer);

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
        SetPart(ExperimentPart.Practice);
    }

    public void HostSelectPart1()
    {
        SetPart(ExperimentPart.Part1);
    }

    public void HostSelectPart2()
    {
        SetPart(ExperimentPart.Part2);
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

        if (part == ExperimentPart.Practice &&
            !IsStoryPhase(currentPhase.Value))
        {
            Debug.LogWarning(
                "[ExperimentFlow] Practice is only available during Story phases."
            );
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

        // Practice is story-only; leave Practice when entering pre-story phases.
        if (!IsStoryPhase(phase) &&
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

        hasAutoAdvanced = false;

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

            _ => 0f
        };
    }

    private void Update()
    {
        if (!IsSpawned)
            return;

        UpdateTimerDisplay();
    }

    private void UpdateTimerDisplay()
    {
        float duration = GetPhaseDuration(currentPhase.Value);

        if (duration <= 0f || phaseEndServerTime.Value < 0d)
        {
            if (timerText != null && timerText.gameObject.activeSelf)
                timerText.gameObject.SetActive(false);

            return;
        }

        if (timerText != null && !timerText.gameObject.activeSelf)
            timerText.gameObject.SetActive(true);

        double remaining =
            phaseEndServerTime.Value -
            NetworkManager.ServerTime.Time;

        if (remaining > 0d)
        {
            int totalSeconds = Mathf.CeilToInt((float)remaining);
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;

            if (timerText != null)
                timerText.text = $"{minutes:00}:{seconds:00}";

            return;
        }

        if (timerText != null)
            timerText.text = "00:00";

        if (IsServer && !hasAutoAdvanced)
        {
            hasAutoAdvanced = true;
            NextPhase();
        }
    }

    private static bool IsStoryPhase(ExperimentPhase phase)
    {
        return phase == ExperimentPhase.StoryADraw ||
               phase == ExperimentPhase.StoryBDraw ||
               phase == ExperimentPhase.StoryDiscussion ||
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
        ApplyContentForPhaseAndPart(phase, currentPart.Value);

        if (storyCardBoardManager != null)
        {
            storyCardBoardManager.ApplyFrontSpritesForPart(currentPart.Value);
            storyCardBoardManager.ApplyButtonColorsForPhase(phase);
        }
    }

    private void UpdatePartButtonVisibility(ExperimentPhase phase)
    {
        bool isStoryPhase = IsStoryPhase(phase);
        bool showPartButtons =
            phase == ExperimentPhase.DailyDiscussion ||
            phase == ExperimentPhase.ProfessionalIntroduction ||
            isStoryPhase;

        // Practice only during Story phases.
        if (practiceButton != null)
        {
            practiceButton.gameObject.SetActive(showPartButtons && isStoryPhase);
            practiceButton.interactable = IsServer && isStoryPhase;
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

            case ExperimentPhase.StoryADraw:

                SetTaskText(
                    StoryTitle(part),
                    $"{PartTag(part)} Participant A: Draw your cards.",
                    "Participant B, please wait."
                );

                break;

            case ExperimentPhase.StoryBDraw:

                SetTaskText(
                    StoryTitle(part),
                    $"{PartTag(part)} Participant B: Draw your cards.",
                    "Participant A, please wait."
                );

                break;

            case ExperimentPhase.StoryDiscussion:

                SetTaskText(
                    StoryTitle(part),
                    $"{PartTag(part)} Discuss the four cards together.",
                    "Use all four cards to plan your story."
                );

                break;

            case ExperimentPhase.StoryNarration:

                SetTaskText(
                    StoryTitle(part),
                    $"{PartTag(part)} Tell the story in the order 1 → 2 → 3 → 4.",
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
