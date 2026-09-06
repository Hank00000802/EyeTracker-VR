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

    private readonly NetworkVariable<bool> isRevealed =
        new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private bool buttonListenerAdded;

    public override void OnNetworkSpawn()
    {
        Debug.Log("[CARD] OnNetworkSpawn", this);

        if (cardImage != null)
            cardImage.raycastTarget = false;

        isRevealed.OnValueChanged += OnRevealStateChanged;

        ApplyVisual(isRevealed.Value);

        if (cardButton != null && !buttonListenerAdded)
        {
            cardButton.onClick.AddListener(OnCardClicked);
            buttonListenerAdded = true;
        }
    }

    public override void OnNetworkDespawn()
    {
        isRevealed.OnValueChanged -= OnRevealStateChanged;

        if (cardButton != null && buttonListenerAdded)
        {
            cardButton.onClick.RemoveListener(OnCardClicked);
            buttonListenerAdded = false;
        }
    }

    private void OnCardClicked()
    {
        Debug.Log("[CARD] Button clicked", this);

        if (!IsSpawned)
        {
            Debug.LogWarning("[CARD] Click ignored: NetworkObject not spawned.", this);
            return;
        }

        // Host / Client 都可請求；只有 Server 會改 NetworkVariable。
        RequestToggleCardRpc();
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

    private void OnRevealStateChanged(bool previousValue, bool newValue)
    {
        Debug.Log(
            $"[CARD] isRevealed changed: {previousValue} -> {newValue}",
            this);

        ApplyVisual(newValue);
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
}
