using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public class SharedNetworkCard : NetworkBehaviour
{
    [Header("UI")]
    [SerializeField] private Button cardButton;
    [SerializeField] private Image cardImage;

    [Header("Card Visual")]
    [SerializeField] private Sprite cardBackSprite;
    [SerializeField] private Sprite cardFrontSprite;

    [Header("Enlarge (StoryDiscussion / StoryNarration)")]
    [Tooltip("Moved object. Leave empty to use this SharedCard transform.")]
    [SerializeField] private Transform cardRoot;
    [Tooltip("Local position while enlarged (table center). Tune in Inspector.")]
    [SerializeField] private Vector3 enlargedLocalPosition;
    [Tooltip("If true, rest scale is multiplied. If false, Enlarged Local Scale is used as-is.")]
    [SerializeField] private bool useScaleMultiplier = true;
    [SerializeField] private float enlargedScaleMultiplier = 2f;
    [SerializeField] private Vector3 enlargedLocalScale = Vector3.one;
    [SerializeField] private float enlargeDuration = 0.2f;

    private readonly NetworkVariable<bool> isRevealed =
        new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private readonly NetworkVariable<bool> isEnlarged =
        new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private bool buttonListenerAdded;
    private bool restPoseCached;
    private Vector3 restLocalPosition;
    private Vector3 restLocalScale;
    private int restSiblingIndex;
    private Coroutine enlargeRoutine;
    private ExperimentFlowManager experimentFlow;

    public bool IsEnlarged => isEnlarged.Value;

    private Transform CardRoot => cardRoot != null ? cardRoot : transform;

    public override void OnNetworkSpawn()
    {
        Debug.Log("[CARD] OnNetworkSpawn", this);

        if (cardImage != null)
            cardImage.raycastTarget = false;

        experimentFlow = FindFirstObjectByType<ExperimentFlowManager>();

        isRevealed.OnValueChanged += OnRevealStateChanged;
        isEnlarged.OnValueChanged += OnEnlargeStateChanged;

        ApplyVisual(isRevealed.Value);
        ApplyEnlarge(isEnlarged.Value, animate: false);

        if (cardButton != null && !buttonListenerAdded)
        {
            cardButton.onClick.AddListener(OnCardClicked);
            buttonListenerAdded = true;
        }
    }

    public override void OnNetworkDespawn()
    {
        isRevealed.OnValueChanged -= OnRevealStateChanged;
        isEnlarged.OnValueChanged -= OnEnlargeStateChanged;

        if (cardButton != null && buttonListenerAdded)
        {
            cardButton.onClick.RemoveListener(OnCardClicked);
            buttonListenerAdded = false;
        }
    }

    private void Update()
    {
        if (!IsSpawned || !IsServer)
            return;

        if (isEnlarged.Value && !IsEnlargePhase())
            isEnlarged.Value = false;
    }

    private void OnCardClicked()
    {
        Debug.Log("[CARD] Button clicked", this);

        if (!IsSpawned)
        {
            Debug.LogWarning("[CARD] Click ignored: NetworkObject not spawned.", this);
            return;
        }

        if (IsStorySelectionActive())
            return;

        if (IsEnlargePhase())
        {
            RequestToggleEnlargeRpc();
            return;
        }

        RequestToggleCardRpc();
    }

    private bool IsEnlargePhase()
    {
        if (experimentFlow == null)
            experimentFlow = FindFirstObjectByType<ExperimentFlowManager>();

        if (experimentFlow == null)
            return false;

        ExperimentPhase phase = experimentFlow.CurrentPhase;
        if (experimentFlow.IsSelectingStory)
            return false;

        return ExperimentFlowManager.IsDiscussionPhase(phase) ||
               ExperimentFlowManager.IsNarrationPhase(phase);
    }

    private bool IsStorySelectionActive()
    {
        if (experimentFlow == null)
            experimentFlow = FindFirstObjectByType<ExperimentFlowManager>();

        return experimentFlow != null && experimentFlow.IsSelectingStory;
    }

    [Rpc(SendTo.Server)]
    private void RequestToggleCardRpc()
    {
        Debug.Log("[CARD] RPC received on server", this);

        bool previous = isRevealed.Value;
        isRevealed.Value = !previous;

        Debug.Log(
            $"[CARD] isRevealed changed: {previous} -> {isRevealed.Value}",
            this);
    }

    [Rpc(SendTo.Server)]
    private void RequestToggleEnlargeRpc()
    {
        if (!IsEnlargePhase())
            return;

        bool next = !isEnlarged.Value;

        if (next)
            CollapseOtherEnlargedCards();

        isEnlarged.Value = next;
    }

    private void CollapseOtherEnlargedCards()
    {
        SharedNetworkCard[] cards =
            FindObjectsByType<SharedNetworkCard>(FindObjectsSortMode.None);

        for (int i = 0; i < cards.Length; i++)
        {
            SharedNetworkCard card = cards[i];
            if (card == null || card == this || !card.IsSpawned)
                continue;

            if (card.isEnlarged.Value)
                card.isEnlarged.Value = false;
        }
    }

    private void OnRevealStateChanged(bool previousValue, bool newValue)
    {
        Debug.Log(
            $"[CARD] isRevealed changed: {previousValue} -> {newValue}",
            this);

        ApplyVisual(newValue);
    }

    private void OnEnlargeStateChanged(bool previousValue, bool newValue)
    {
        ApplyEnlarge(newValue, animate: true);
    }

    private void ApplyVisual(bool revealed)
    {
        if (cardImage == null)
            return;

        cardImage.sprite =
            revealed ? cardFrontSprite : cardBackSprite;

        Debug.Log(
            revealed
                ? "[CARD] ApplyVisual: Front"
                : "[CARD] ApplyVisual: Back",
            this);
    }

    private void ApplyEnlarge(bool enlarged, bool animate)
    {
        Transform root = CardRoot;
        if (root == null)
            return;

        if (enlarged)
            CacheRestPose(root);

        Vector3 targetPos = enlarged ? enlargedLocalPosition : restLocalPosition;
        Vector3 targetScale = enlarged ? GetEnlargedScale() : restLocalScale;

        if (enlarged)
            root.SetAsLastSibling();
        else if (root.parent != null)
            root.SetSiblingIndex(Mathf.Clamp(restSiblingIndex, 0, root.parent.childCount - 1));

        if (!animate || enlargeDuration <= 0.001f)
        {
            root.localPosition = targetPos;
            root.localScale = targetScale;
            if (!enlarged)
                restPoseCached = false;
            return;
        }

        if (enlargeRoutine != null)
            StopCoroutine(enlargeRoutine);

        enlargeRoutine = StartCoroutine(
            AnimateEnlarge(root, targetPos, targetScale, enlarged));
    }

    private IEnumerator AnimateEnlarge(
        Transform root,
        Vector3 targetPos,
        Vector3 targetScale,
        bool enlarged)
    {
        Vector3 startPos = root.localPosition;
        Vector3 startScale = root.localScale;
        float t = 0f;

        while (t < enlargeDuration)
        {
            t += Time.deltaTime;
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / enlargeDuration));
            root.localPosition = Vector3.Lerp(startPos, targetPos, p);
            root.localScale = Vector3.Lerp(startScale, targetScale, p);
            yield return null;
        }

        root.localPosition = targetPos;
        root.localScale = targetScale;
        enlargeRoutine = null;

        if (!enlarged)
            restPoseCached = false;
    }

    private void CacheRestPose(Transform root)
    {
        if (restPoseCached)
            return;

        restLocalPosition = root.localPosition;
        restLocalScale = root.localScale;
        restSiblingIndex = root.GetSiblingIndex();
        restPoseCached = true;
    }

    private Vector3 GetEnlargedScale()
    {
        if (useScaleMultiplier)
            return restLocalScale * enlargedScaleMultiplier;

        return enlargedLocalScale;
    }
}
