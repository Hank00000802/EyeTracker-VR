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

    [Header("Board")]
    [SerializeField] private StoryCardBoardManager storyCardBoardManager;
    [SerializeField] private float phaseAdvanceDelay = 0.8f;

    private readonly NetworkVariable<int> currentNarrationCard =
        new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private bool isAdvancingPhase;
    private ExperimentPhase lastPhase = (ExperimentPhase)255;

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
            if (phase == ExperimentPhase.StoryADraw)
                currentNarrationCard.Value = 0;

            if (phase == ExperimentPhase.StoryNarration &&
                previousPhase != ExperimentPhase.StoryNarration)
            {
                currentNarrationCard.Value = 1;
            }
        }

        bool isStoryPhase =
            phase == ExperimentPhase.StoryADraw ||
            phase == ExperimentPhase.StoryBDraw ||
            phase == ExperimentPhase.StoryDiscussion ||
            phase == ExperimentPhase.StoryNarration;

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

        bool canDraw =
            (phase == ExperimentPhase.StoryADraw && localIsA) ||
            (phase == ExperimentPhase.StoryBDraw && localIsB);

        if (drawButton != null)
        {
            drawButton.gameObject.SetActive(canDraw);
            drawButton.interactable = canDraw;
        }

        if (drawStatusText == null)
            return;

        switch (phase)
        {
            case ExperimentPhase.StoryADraw:

                drawStatusText.text = localIsA
                    ? "Participant A: Draw your cards."
                    : "Waiting for Participant A...";

                break;

            case ExperimentPhase.StoryBDraw:

                drawStatusText.text = localIsB
                    ? "Participant B: Draw your cards."
                    : "Waiting for Participant B...";

                break;

            case ExperimentPhase.StoryDiscussion:

                drawStatusText.text =
                    "Discuss the four cards together.";

                break;

            case ExperimentPhase.StoryNarration:

                drawStatusText.text =
                    "Tell the story in order: 1 → 2 → 3 → 4.";

                break;
        }
    }

    private void UpdateNextCardButtonVisibility(ExperimentPhase phase)
    {
        if (nextCardButton == null)
            return;

        bool showNext =
            phase == ExperimentPhase.StoryNarration &&
            CanControlCurrentNarrationCard();

        nextCardButton.gameObject.SetActive(showNext);
        nextCardButton.interactable = showNext;
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
            experimentFlowManager.CurrentPhase != ExperimentPhase.StoryNarration)
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
            experimentFlowManager.CurrentPhase != ExperimentPhase.StoryNarration)
        {
            return;
        }

        ulong senderClientId = rpcParams.Receive.SenderClientId;
        bool senderIsA = senderClientId == NetworkManager.ServerClientId;
        bool senderIsB = senderClientId != NetworkManager.ServerClientId;
        int cardIndex = currentNarrationCard.Value;

        bool allowed =
            ((cardIndex == 1 || cardIndex == 3) && senderIsA) ||
            ((cardIndex == 2 || cardIndex == 4) && senderIsB);

        if (!allowed)
        {
            Debug.LogWarning(
                $"[StoryCard] Invalid next-card request. " +
                $"Sender={senderClientId}, Card={cardIndex}"
            );
            return;
        }

        if (cardIndex < 4)
        {
            currentNarrationCard.Value++;
            return;
        }

        currentNarrationCard.Value = 0;
        experimentFlowManager.SetPhase(ExperimentPhase.Finished);
    }

    private void OnDrawButtonClicked()
    {
        if (!IsSpawned)
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

        if (phase == ExperimentPhase.StoryADraw &&
            senderIsA)
        {
            Debug.Log(
                "[StoryCard] Participant A drew cards."
            );

            if (storyCardBoardManager != null)
                storyCardBoardManager.DrawCardsForParticipantAServer();

            StartCoroutine(
                AdvancePhaseAfterDelay(ExperimentPhase.StoryBDraw)
            );

            return;
        }

        if (phase == ExperimentPhase.StoryBDraw &&
            senderIsB)
        {
            Debug.Log(
                "[StoryCard] Participant B drew cards."
            );

            if (storyCardBoardManager != null)
                storyCardBoardManager.DrawCardsForParticipantBServer();

            StartCoroutine(
                AdvancePhaseAfterDelay(ExperimentPhase.StoryDiscussion)
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

