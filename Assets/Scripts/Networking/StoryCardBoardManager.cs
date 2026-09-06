using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

public class StoryCardBoardManager : NetworkBehaviour
{
    [System.Serializable]
    public class StoryCardView
    {
        public string cardName;
        public Transform root;
        public Image cardImage;
        public Sprite frontSprite;
        public Transform slotTarget;
        public TMP_Text cardNumberText;

        [System.NonSerialized]
        public Vector3 normalScale;

        [System.NonSerialized]
        public Color defaultButtonColor;

        [System.NonSerialized]
        public bool hasDefaultButtonColor;
    }

    public enum StoryCardParticipant
    {
        ParticipantA,
        ParticipantB
    }

    [Header("Card Look")]
    [SerializeField] private Sprite backSprite;

    [Header("Draw Start Points")]
    [SerializeField] private RectTransform drawStartA;
    [SerializeField] private RectTransform drawStartB;

    [Header("Animation")]
    [SerializeField] private float moveDuration = 0.6f;
    [SerializeField] private float staggerDelay = 0.1f;

    [Header("Highlight")]
    [SerializeField] private float highlightedScale = 1.15f;

    [Header("Cards")]
    [SerializeField] private StoryCardView card1;
    [SerializeField] private StoryCardView card2;
    [SerializeField] private StoryCardView card3;
    [SerializeField] private StoryCardView card4;

    [Header("Per-Part Front Sprites (optional; reuse SharedCard01~04)")]
    [Tooltip("Leave empty to keep each card's current frontSprite.")]
    [SerializeField] private Sprite[] practiceFrontSprites = new Sprite[4];
    [SerializeField] private Sprite[] part1FrontSprites = new Sprite[4];
    [SerializeField] private Sprite[] part2FrontSprites = new Sprite[4];

    [Header("Participant Button Color")]
    [SerializeField] private Color participantAButtonColor = Color.red;
    [SerializeField] private Color participantBButtonColor = Color.green;

    private readonly NetworkVariable<bool> aCardsDrawn =
        new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private readonly NetworkVariable<bool> bCardsDrawn =
        new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private bool playedAAnimation = false;
    private bool playedBAnimation = false;
    private bool m_useParticipantButtonColors;

    public float DealSequenceDuration => moveDuration + staggerDelay;

    private void CacheCardScales()
    {
        CacheScale(card1);
        CacheScale(card2);
        CacheScale(card3);
        CacheScale(card4);
    }

    private static void CacheScale(StoryCardView card)
    {
        if (card.root == null)
            return;

        Debug.Log(
            $"[CARD SCALE] {card.root.name} CacheScale before={card.root.localScale}");

        card.normalScale = card.root.localScale;

        Debug.Log(
            $"[CARD SCALE] {card.root.name} CacheScale after normalScale={card.normalScale}");
    }

    private void Awake()
    {
        CacheCardScales();
        CacheDefaultButtonColors();
        ApplyDefaultButtonColors();
    }

    public void ApplyButtonColorsForPhase(ExperimentPhase phase)
    {
        m_useParticipantButtonColors = phase == ExperimentPhase.StoryNarration;

        if (m_useParticipantButtonColors)
            ApplyParticipantButtonColors();
        else
            ApplyDefaultButtonColors();
    }

    private void CacheDefaultButtonColors()
    {
        CacheDefaultButtonColor(card1);
        CacheDefaultButtonColor(card2);
        CacheDefaultButtonColor(card3);
        CacheDefaultButtonColor(card4);
    }

    private static void CacheDefaultButtonColor(StoryCardView card)
    {
        Image buttonImage = GetCardButtonImage(card);
        if (buttonImage == null || card.hasDefaultButtonColor)
            return;

        card.defaultButtonColor = buttonImage.color;
        card.hasDefaultButtonColor = true;
    }

    private void ApplyDefaultButtonColors()
    {
        SetButtonColor(card1, GetDefaultButtonColor(card1));
        SetButtonColor(card2, GetDefaultButtonColor(card2));
        SetButtonColor(card3, GetDefaultButtonColor(card3));
        SetButtonColor(card4, GetDefaultButtonColor(card4));
    }

    /// <summary>
    /// Ownership is fixed by draw assignment:
    /// Participant A = card1 + card3, Participant B = card2 + card4.
    /// </summary>
    private void ApplyParticipantButtonColors()
    {
        SetParticipantButtonColor(card1, StoryCardParticipant.ParticipantA);
        SetParticipantButtonColor(card3, StoryCardParticipant.ParticipantA);
        SetParticipantButtonColor(card2, StoryCardParticipant.ParticipantB);
        SetParticipantButtonColor(card4, StoryCardParticipant.ParticipantB);
    }

    private void SetParticipantButtonColor(StoryCardView card, StoryCardParticipant participant)
    {
        SetButtonColor(
            card,
            participant == StoryCardParticipant.ParticipantA
                ? participantAButtonColor
                : participantBButtonColor);
    }

    private static Color GetDefaultButtonColor(StoryCardView card)
    {
        return card != null && card.hasDefaultButtonColor
            ? card.defaultButtonColor
            : Color.white;
    }

    private static void SetButtonColor(StoryCardView card, Color color)
    {
        Image buttonImage = GetCardButtonImage(card);
        if (buttonImage == null)
            return;

        if (card.cardImage != null)
        {
            Outline leftoverOutline = card.cardImage.GetComponent<Outline>();
            if (leftoverOutline != null)
                Destroy(leftoverOutline);
        }

        buttonImage.color = color;
    }

    private static Image GetCardButtonImage(StoryCardView card)
    {
        if (card == null || card.cardImage == null)
            return null;

        Button button = card.cardImage.GetComponentInParent<Button>();
        if (button == null)
            return null;

        return button.targetGraphic as Image;
    }

    public void SetHighlightedCard(int cardIndex)
    {
        SetCardHighlight(card1, cardIndex == 1);
        SetCardHighlight(card2, cardIndex == 2);
        SetCardHighlight(card3, cardIndex == 3);
        SetCardHighlight(card4, cardIndex == 4);
    }

    private void SetCardHighlight(StoryCardView card, bool highlighted)
    {
        if (card.root == null)
            return;

        if (card.normalScale.sqrMagnitude < 0.0001f)
            CacheScale(card);

        if (card.normalScale.sqrMagnitude < 0.0001f)
            return;

        Debug.Log(
            $"[CARD SCALE] {card.root.name} SetCardHighlight before={card.root.localScale}");

        card.root.localScale = highlighted
            ? card.normalScale * highlightedScale
            : card.normalScale;

        Debug.Log(
            $"[CARD SCALE] {card.root.name} SetCardHighlight after={card.root.localScale}");
    }

    public override void OnNetworkSpawn()
    {
        aCardsDrawn.OnValueChanged += OnACardsDrawnChanged;
        bCardsDrawn.OnValueChanged += OnBCardsDrawnChanged;

        ApplyInitialBoardState();
    }

    public override void OnNetworkDespawn()
    {
        aCardsDrawn.OnValueChanged -= OnACardsDrawnChanged;
        bCardsDrawn.OnValueChanged -= OnBCardsDrawnChanged;
    }

    public void DrawCardsForParticipantAServer()
    {
        if (!IsServer || aCardsDrawn.Value)
            return;

        aCardsDrawn.Value = true;
    }

    public void DrawCardsForParticipantBServer()
    {
        if (!IsServer || bCardsDrawn.Value)
            return;

        bCardsDrawn.Value = true;
    }

    private void ApplyInitialBoardState()
    {
        HideSlotVisuals();

        HideCard(card1);
        HideCard(card2);
        HideCard(card3);
        HideCard(card4);

        if (aCardsDrawn.Value)
            PlaceACardsImmediate();

        if (bCardsDrawn.Value)
            PlaceBCardsImmediate();
    }

    private void HideSlotVisuals()
    {
        HideSlotVisual(card1.slotTarget);
        HideSlotVisual(card2.slotTarget);
        HideSlotVisual(card3.slotTarget);
        HideSlotVisual(card4.slotTarget);
    }

    private static void HideSlotVisual(Transform slot)
    {
        if (slot == null)
            return;

        // Slots are anchor-only RectTransforms; keep active for animation targets.
        CanvasGroup group = slot.GetComponent<CanvasGroup>();
        if (group == null)
            group = slot.gameObject.AddComponent<CanvasGroup>();

        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        Graphic[] graphics = slot.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
            graphics[i].enabled = false;
    }

    private void OnACardsDrawnChanged(bool previous, bool current)
    {
        if (!current)
            return;

        if (playedAAnimation)
        {
            PlaceACardsImmediate();
            return;
        }

        playedAAnimation = true;
        StartCoroutine(PlayADrawSequence());
    }

    private void OnBCardsDrawnChanged(bool previous, bool current)
    {
        if (!current)
            return;

        if (playedBAnimation)
        {
            PlaceBCardsImmediate();
            return;
        }

        playedBAnimation = true;
        StartCoroutine(PlayBDrawSequence());
    }

    private IEnumerator PlayADrawSequence()
    {
        StartCoroutine(AnimateCard(card1, drawStartA, 0f));
        StartCoroutine(AnimateCard(card3, drawStartA, staggerDelay));
        yield break;
    }

    private IEnumerator PlayBDrawSequence()
    {
        StartCoroutine(AnimateCard(card2, drawStartB, 0f));
        StartCoroutine(AnimateCard(card4, drawStartB, staggerDelay));
        yield break;
    }

    private IEnumerator AnimateCard(
        StoryCardView card,
        RectTransform startPoint,
        float delay)
    {
        if (card.root == null || card.cardImage == null || card.slotTarget == null)
            yield break;

        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        ShowCardVisual(card, interactable: false);
        card.cardImage.enabled = true;
        card.cardImage.sprite = backSprite;

        Vector3 startPos = startPoint.localPosition;
        Vector3 endPos = card.slotTarget.localPosition;
        card.root.localPosition = startPos;

        float t = 0f;

        while (t < moveDuration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / moveDuration);
            card.root.localPosition = Vector3.Lerp(startPos, endPos, p);
            yield return null;
        }

        card.root.localPosition = endPos;
        card.cardImage.sprite = card.frontSprite;
        SetCardButtonInteractable(card, true);
    }

    public void HideAllCardVisuals()
    {
        HideCard(card1);
        HideCard(card2);
        HideCard(card3);
        HideCard(card4);
        SetHighlightedCard(0);
    }

    /// <summary>
    /// Swaps front sprites on the existing SharedCard01~04 views.
    /// Does not create/duplicate NetworkObjects. Empty slots keep current sprites.
    /// </summary>
    public void ApplyFrontSpritesForPart(ExperimentPart part)
    {
        Sprite[] sprites = part switch
        {
            ExperimentPart.Practice => practiceFrontSprites,
            ExperimentPart.Part2 => part2FrontSprites,
            _ => part1FrontSprites
        };

        ApplyFrontSpriteIfProvided(card1, sprites, 0);
        ApplyFrontSpriteIfProvided(card2, sprites, 1);
        ApplyFrontSpriteIfProvided(card3, sprites, 2);
        ApplyFrontSpriteIfProvided(card4, sprites, 3);
    }

    private static void ApplyFrontSpriteIfProvided(
        StoryCardView card,
        Sprite[] sprites,
        int index)
    {
        if (card == null)
            return;

        if (sprites == null ||
            index < 0 ||
            index >= sprites.Length ||
            sprites[index] == null)
        {
            return;
        }

        card.frontSprite = sprites[index];

        // If the card face is currently visible, refresh the Image immediately.
        if (card.cardImage != null && card.cardImage.enabled)
            card.cardImage.sprite = card.frontSprite;
    }

    private void HideCard(StoryCardView card)
    {
        // Never disable NetworkObject root (SharedCard01~04); that breaks NGO join sync.
        if (card.cardImage != null)
        {
            if (backSprite != null)
                card.cardImage.sprite = backSprite;

            card.cardImage.enabled = false;
        }

        SetCardButtonGraphicEnabled(card, false);
        SetCardButtonInteractable(card, false);
        SetCardNumberVisible(card, false);
    }

    private void ShowCardVisual(StoryCardView card, bool interactable)
    {
        if (card.root != null && card.normalScale.sqrMagnitude > 0.0001f)
        {
            Debug.Log(
                $"[CARD SCALE] {card.root.name} ShowCardVisual before={card.root.localScale}");

            card.root.localScale = card.normalScale;

            Debug.Log(
                $"[CARD SCALE] {card.root.name} ShowCardVisual after={card.root.localScale}");
        }

        if (card.cardImage != null)
            card.cardImage.enabled = true;

        SetCardButtonGraphicEnabled(card, true);
        SetCardButtonInteractable(card, interactable);
        SetCardNumberVisible(card, true);
        RestoreButtonColor(card);
    }

    private static void SetCardButtonGraphicEnabled(StoryCardView card, bool enabled)
    {
        if (card.cardImage == null)
            return;

        Button button = card.cardImage.GetComponentInParent<Button>();
        if (button != null && button.targetGraphic != null)
            button.targetGraphic.enabled = enabled;
    }

    private static void SetCardButtonInteractable(StoryCardView card, bool interactable)
    {
        if (card.cardImage == null)
            return;

        Button button = card.cardImage.GetComponentInParent<Button>();
        if (button != null)
            button.interactable = interactable;
    }

    private static void SetCardNumberVisible(StoryCardView card, bool visible)
    {
        if (card.cardNumberText == null)
            return;

        card.cardNumberText.enabled = visible;
    }

    private void RestoreButtonColor(StoryCardView card)
    {
        if (!m_useParticipantButtonColors)
        {
            SetButtonColor(card, GetDefaultButtonColor(card));
            return;
        }

        if (ReferenceEquals(card, card1) || ReferenceEquals(card, card3))
            SetParticipantButtonColor(card, StoryCardParticipant.ParticipantA);
        else if (ReferenceEquals(card, card2) || ReferenceEquals(card, card4))
            SetParticipantButtonColor(card, StoryCardParticipant.ParticipantB);
    }

    private void PlaceACardsImmediate()
    {
        PlaceCardImmediate(card1);
        PlaceCardImmediate(card3);
    }

    private void PlaceBCardsImmediate()
    {
        PlaceCardImmediate(card2);
        PlaceCardImmediate(card4);
    }

    private void PlaceCardImmediate(StoryCardView card)
    {
        if (card.root == null || card.cardImage == null || card.slotTarget == null)
            return;

        ShowCardVisual(card, interactable: true);
        card.cardImage.enabled = true;
        card.root.localPosition = card.slotTarget.localPosition;
        card.cardImage.sprite = card.frontSprite;
    }
}