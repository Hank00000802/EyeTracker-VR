using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

public class StoryCardTaskManager : NetworkBehaviour
{
    [Header("Experiment")]
    [SerializeField] private ExperimentFlowManager experimentFlowManager;

    [Header("Story UI")]
    [SerializeField] private GameObject storyInteractionPanel;
    [SerializeField] private Button drawButton;
    [SerializeField] private TMP_Text drawStatusText;

    [Header("Narration")]
    [SerializeField] private Button nextCardButton;
    [SerializeField] private TMP_Text narrationStatusText;
    [SerializeField] private float narrationNextCardUnlockSeconds = 40f;
    [SerializeField] private float narrationAutoAdvanceSeconds = 180f;

    [Header("Board")]
    [SerializeField] private StoryCardBoardManager storyCardBoardManager;
    [SerializeField] private float phaseAdvanceDelay = 0.8f;

    private readonly NetworkVariable<int> currentNarrationCard =
        new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private readonly NetworkVariable<double> narrationCardStartServerTime =
        new NetworkVariable<double>(
            -1d,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private bool isAdvancingPhase;
    private bool hasAutoAdvancedThisCard;
    private ExperimentPhase lastPhase = (ExperimentPhase)255;
    private bool lastSelectingStory;

    public int CurrentNarrationCardIndex => currentNarrationCard.Value;

    public bool IsOnLastNarrationCard => currentNarrationCard.Value == 4;

    private void Awake()
    {
        if (storyCardBoardManager == null)
            storyCardBoardManager =
                FindFirstObjectByType<StoryCardBoardManager>();
    }

    public override void OnNetworkSpawn()
    {
        if (nextCardButton != null)
            nextCardButton.gameObject.SetActive(false);

        if (drawButton != null)
            drawButton.onClick.AddListener(OnDrawButtonClicked);

        if (nextCardButton != null)
            nextCardButton.onClick.AddListener(OnNextCardClicked);

        currentNarrationCard.OnValueChanged += OnNarrationCardChanged;

        RefreshUI();
        ApplyNarrationCard(currentNarrationCard.Value);
    }

    public override void OnNetworkDespawn()
    {
        if (drawButton != null)
            drawButton.onClick.RemoveListener(OnDrawButtonClicked);

        if (nextCardButton != null)
            nextCardButton.onClick.RemoveListener(OnNextCardClicked);

        currentNarrationCard.OnValueChanged -= OnNarrationCardChanged;
    }

    private void Update()
    {
        if (!IsSpawned || experimentFlowManager == null)
            return;

        if (experimentFlowManager.CurrentPhase != lastPhase)
            RefreshUI();
        else if (experimentFlowManager.IsSelectingStory != lastSelectingStory)
            UpdateDrawAndNextCardForSelection();
        else if (ExperimentFlowManager.IsNarrationPhase(experimentFlowManager.CurrentPhase))
            UpdateNextCardButtonVisibility(experimentFlowManager.CurrentPhase);

        lastSelectingStory = experimentFlowManager.IsSelectingStory;

        if (IsServer)
            TryAutoAdvanceNarrationCard();
    }

    private void RefreshUI()
    {
        if (experimentFlowManager == null)
            return;

        ExperimentPhase phase = experimentFlowManager.CurrentPhase;
        ExperimentPhase previousPhase = lastPhase;
        lastPhase = phase;

        if (IsServer)
        {
            if (ExperimentFlowManager.IsDrawAPhase(phase) &&
                !ExperimentFlowManager.IsDrawBPhase(previousPhase))
            {
                currentNarrationCard.Value = 0;
                isAdvancingPhase = false;
                if (storyCardBoardManager != null)
                    storyCardBoardManager.ResetBoardForNewDealServer();
            }

            if (ExperimentFlowManager.IsNarrationPhase(phase) &&
                !ExperimentFlowManager.IsNarrationPhase(previousPhase))
            {
                StartNarrationCardServer(1);
            }

            if (!ExperimentFlowManager.IsNarrationPhase(phase) &&
                ExperimentFlowManager.IsNarrationPhase(previousPhase))
            {
                narrationCardStartServerTime.Value = -1d;
                hasAutoAdvancedThisCard = false;
            }
        }

        bool isStoryPhase = ExperimentFlowManager.IsStoryPhase(phase);

        if (storyInteractionPanel != null)
            storyInteractionPanel.SetActive(isStoryPhase);

        UpdateNextCardButtonVisibility(phase);

        Debug.Log(
            $"[STORY UI] Phase={phase}, CardIndex={currentNarrationCard.Value}, " +
            $"NextButtonRef={(nextCardButton != null)}, " +
            $"Active={(nextCardButton != null && nextCardButton.gameObject.activeSelf)} " +
            $"IsServer={IsServer} IsClient={IsClient}"
        );

        if (phase == ExperimentPhase.Finished)
        {
            if (storyCardBoardManager != null)
                storyCardBoardManager.HideAllCardVisuals();

            if (narrationStatusText != null)
            {
                narrationStatusText.text = string.Empty;
                narrationStatusText.gameObject.SetActive(false);
            }

            return;
        }

        if (!isStoryPhase)
            return;

        bool localIsA = IsLocalParticipantA();
        bool localIsB = IsLocalParticipantB();
        bool selectingStory = experimentFlowManager.IsSelectingStory;

        bool canDraw =
            !selectingStory &&
            ((ExperimentFlowManager.IsDrawAPhase(phase) && localIsA) ||
             (ExperimentFlowManager.IsDrawBPhase(phase) && localIsB));

        if (drawButton != null)
        {
            drawButton.gameObject.SetActive(canDraw);
            drawButton.interactable = canDraw;
        }

        if (drawStatusText == null)
            return;

        if (ExperimentFlowManager.IsDrawAPhase(phase))
        {
            drawStatusText.text = localIsA
                ? "Participant A: Draw your cards."
                : "Waiting for Participant A...";
        }
        else if (ExperimentFlowManager.IsDrawBPhase(phase))
        {
            drawStatusText.text = localIsB
                ? "Participant B: Draw your cards."
                : "Waiting for Participant B...";
        }
        else if (ExperimentFlowManager.IsDiscussionPhase(phase))
        {
            drawStatusText.text = "Discuss the four cards together.";
        }
        else if (ExperimentFlowManager.IsNarrationPhase(phase))
        {
            drawStatusText.text = "Tell the story in order: 1 → 2 → 3 → 4.";
        }
    }

    private void UpdateNextCardButtonVisibility(ExperimentPhase phase)
    {
        if (nextCardButton == null || experimentFlowManager == null)
            return;

        int cardIndex = currentNarrationCard.Value;
        bool showNext =
            !experimentFlowManager.IsSelectingStory &&
            ExperimentFlowManager.IsNarrationPhase(phase) &&
            cardIndex >= 1 &&
            cardIndex <= 3 &&
            CanControlCurrentNarrationCard() &&
            IsNarrationUnlockElapsed();

        if (nextCardButton.gameObject.activeSelf != showNext)
            nextCardButton.gameObject.SetActive(showNext);

        nextCardButton.interactable = showNext;
    }

    private void UpdateDrawAndNextCardForSelection()
    {
        ExperimentPhase phase = experimentFlowManager.CurrentPhase;
        UpdateNextCardButtonVisibility(phase);

        if (drawButton == null)
            return;

        bool localIsA = IsLocalParticipantA();
        bool localIsB = IsLocalParticipantB();
        bool canDraw =
            !experimentFlowManager.IsSelectingStory &&
            ((ExperimentFlowManager.IsDrawAPhase(phase) && localIsA) ||
             (ExperimentFlowManager.IsDrawBPhase(phase) && localIsB));

        drawButton.gameObject.SetActive(canDraw);
        drawButton.interactable = canDraw;
    }

    public bool TryGetNarrationElapsedSeconds(out int elapsedSeconds)
    {
        elapsedSeconds = 0;

        if (experimentFlowManager == null ||
            !ExperimentFlowManager.IsNarrationPhase(experimentFlowManager.CurrentPhase))
        {
            return false;
        }

        if (!TryGetNarrationElapsed(out double elapsed))
            return false;

        elapsedSeconds = (int)elapsed;
        if (elapsedSeconds < 0)
            elapsedSeconds = 0;
        return true;
    }

    public bool TryGetNarrationRemainingSeconds(out double remainingSeconds)
    {
        remainingSeconds = 0d;

        if (experimentFlowManager == null ||
            !ExperimentFlowManager.IsNarrationPhase(experimentFlowManager.CurrentPhase))
        {
            return false;
        }

        if (!TryGetNarrationElapsed(out double elapsed))
            return false;

        remainingSeconds = narrationAutoAdvanceSeconds - elapsed;
        if (remainingSeconds < 0d)
            remainingSeconds = 0d;

        return true;
    }

    public bool IsNarrationUnlockElapsed()
    {
        return TryGetNarrationElapsed(out double elapsed) &&
               elapsed >= narrationNextCardUnlockSeconds;
    }

    bool TryGetNarrationElapsed(out double elapsed)
    {
        elapsed = 0d;

        if (NetworkManager == null)
            return false;

        double startTime = narrationCardStartServerTime.Value;
        if (startTime < 0d)
            return false;

        int cardIndex = currentNarrationCard.Value;
        if (cardIndex < 1 || cardIndex > 4)
            return false;

        double maxSeconds = narrationAutoAdvanceSeconds;
        if (maxSeconds < 0d)
            maxSeconds = 0d;

        elapsed = NetworkManager.ServerTime.Time - startTime;
        if (elapsed < 0d)
            elapsed = 0d;
        if (elapsed > maxSeconds)
            elapsed = maxSeconds;

        return true;
    }

    void StartNarrationCardServer(int cardIndex)
    {
        int clamped = Mathf.Clamp(cardIndex, 1, 4);
        currentNarrationCard.Value = clamped;
        hasAutoAdvancedThisCard = false;
        narrationCardStartServerTime.Value = NetworkManager.ServerTime.Time;
    }

    void TryAutoAdvanceNarrationCard()
    {
        if (experimentFlowManager == null ||
            !ExperimentFlowManager.IsNarrationPhase(experimentFlowManager.CurrentPhase))
        {
            return;
        }

        if (hasAutoAdvancedThisCard)
            return;

        if (!TryGetNarrationElapsed(out double elapsed) ||
            elapsed < narrationAutoAdvanceSeconds)
        {
            return;
        }

        hasAutoAdvancedThisCard = true;
        AdvanceNarrationCardServer();
    }

    void AdvanceNarrationCardServer()
    {
        int cardIndex = currentNarrationCard.Value;
        if (cardIndex < 4)
        {
            StartNarrationCardServer(cardIndex + 1);
            return;
        }

        experimentFlowManager.SetPhase(
            experimentFlowManager.CurrentPhase == ExperimentPhase.PracticeNarration
                ? ExperimentPhase.StoryADraw
                : ExperimentPhase.Finished);
    }

    private bool IsLocalParticipantA()
    {
        return NetworkManager != null && NetworkManager.IsHost;
    }

    private bool IsLocalParticipantB()
    {
        return NetworkManager != null &&
               NetworkManager.IsClient &&
               !NetworkManager.IsHost;
    }

    private bool CanControlCurrentNarrationCard()
    {
        int cardIndex = currentNarrationCard.Value;

        if (cardIndex == 1 || cardIndex == 3)
            return IsLocalParticipantA();

        if (cardIndex == 2 || cardIndex == 4)
            return IsLocalParticipantB();

        return false;
    }

    private void OnNarrationCardChanged(int previous, int current)
    {
        ApplyNarrationCard(current);
    }

    private void ApplyNarrationCard(int cardIndex)
    {
        if (experimentFlowManager != null)
            UpdateNextCardButtonVisibility(experimentFlowManager.CurrentPhase);

        if (storyCardBoardManager != null)
            storyCardBoardManager.SetHighlightedCard(cardIndex);

        if (narrationStatusText == null)
            return;

        switch (cardIndex)
        {
            case 1:
                narrationStatusText.text = "Card 1 — Participant A";
                break;

            case 2:
                narrationStatusText.text = "Card 2 — Participant B";
                break;

            case 3:
                narrationStatusText.text = "Card 3 — Participant A";
                break;

            case 4:
                narrationStatusText.text = "Card 4 — Participant B";
                break;

            default:
                narrationStatusText.text = string.Empty;
                break;
        }
    }

    private void OnNextCardClicked()
    {
        if (!IsSpawned ||
            experimentFlowManager == null ||
            experimentFlowManager.IsSelectingStory ||
            !ExperimentFlowManager.IsNarrationPhase(experimentFlowManager.CurrentPhase))
        {
            return;
        }

        if (nextCardButton != null)
            nextCardButton.interactable = false;

        RequestNextCardRpc();
    }

    [Rpc(SendTo.Server)]
    private void RequestNextCardRpc(RpcParams rpcParams = default)
    {
        if (experimentFlowManager == null ||
            experimentFlowManager.IsSelectingStory ||
            !ExperimentFlowManager.IsNarrationPhase(experimentFlowManager.CurrentPhase))
        {
            return;
        }

        ulong senderClientId = rpcParams.Receive.SenderClientId;
        bool senderIsA = senderClientId == NetworkManager.ServerClientId;
        bool senderIsB = senderClientId != NetworkManager.ServerClientId;
        int cardIndex = currentNarrationCard.Value;

        bool allowed =
            ((cardIndex == 1 || cardIndex == 3) && senderIsA) ||
            (cardIndex == 2 && senderIsB);

        if (!allowed ||
            cardIndex < 1 ||
            cardIndex > 3 ||
            !IsNarrationUnlockElapsed())
        {
            Debug.LogWarning(
                $"[StoryCard] Invalid next-card request. " +
                $"Sender={senderClientId}, Card={cardIndex}"
            );
            return;
        }

        StartNarrationCardServer(cardIndex + 1);
    }

    private void OnDrawButtonClicked()
    {
        if (!IsSpawned)
            return;

        if (experimentFlowManager != null &&
            experimentFlowManager.IsSelectingStory)
            return;

        if (drawButton != null)
            drawButton.interactable = false;

        RequestDrawRpc();
    }

    [Rpc(SendTo.Server)]
    private void RequestDrawRpc(RpcParams rpcParams = default)
    {
        if (experimentFlowManager == null)
            return;

        ulong senderClientId =
            rpcParams.Receive.SenderClientId;

        bool senderIsA =
            senderClientId == NetworkManager.ServerClientId;

        bool senderIsB =
            senderClientId != NetworkManager.ServerClientId;

        ExperimentPhase phase =
            experimentFlowManager.CurrentPhase;

        if (experimentFlowManager.IsSelectingStory)
        {
            Debug.LogWarning("[StoryCard] Draw ignored: story selection in progress.");
            return;
        }

        if (ExperimentFlowManager.IsDrawAPhase(phase) &&
            senderIsA)
        {
            Debug.Log(
                "[StoryCard] Participant A drew cards."
            );

            if (storyCardBoardManager != null)
                storyCardBoardManager.DrawCardsForParticipantAServer();

            StartCoroutine(
                AdvancePhaseAfterDelay(
                    phase == ExperimentPhase.PracticeADraw
                        ? ExperimentPhase.PracticeBDraw
                        : ExperimentPhase.StoryBDraw)
            );

            return;
        }

        if (ExperimentFlowManager.IsDrawBPhase(phase) &&
            senderIsB)
        {
            Debug.Log(
                "[StoryCard] Participant B drew cards."
            );

            if (storyCardBoardManager != null)
                storyCardBoardManager.DrawCardsForParticipantBServer();

            StartCoroutine(
                AdvancePhaseAfterDelay(
                    phase == ExperimentPhase.PracticeBDraw
                        ? ExperimentPhase.PracticeDiscussion
                        : ExperimentPhase.StoryDiscussion)
            );

            return;
        }

        Debug.LogWarning(
            $"[StoryCard] Invalid draw request. " +
            $"Sender={senderClientId}, Phase={phase}"
        );
    }

    private IEnumerator AdvancePhaseAfterDelay(ExperimentPhase nextPhase)
    {
        if (isAdvancingPhase)
            yield break;

        isAdvancingPhase = true;

        float delay = phaseAdvanceDelay;

        if (storyCardBoardManager != null)
            delay = storyCardBoardManager.DealSequenceDuration + 0.1f;

        yield return new WaitForSeconds(delay);

        if (experimentFlowManager != null)
            experimentFlowManager.SetPhase(nextPhase);

        isAdvancingPhase = false;
    }
}

