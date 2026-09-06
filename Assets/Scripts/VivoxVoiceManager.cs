using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Vivox;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// Minimal Vivox v16 two-user voice manager (LOCAL ONLY voice layer).
/// NGO remains responsible for network/phase; this script only handles microphone + Vivox audio.
/// </summary>
public class VivoxVoiceManager : MonoBehaviour
{
    public const string DefaultChannelName = "EchoSpace_Test";
    const string VoiceStatusObjectName = "VoiceStatusText";
    const int ExpectedParticipantCount = 2;

    enum VoiceUiPhase
    {
        Initializing,
        LoggingIn,
        Joining,
        InChannel,
        MicDenied,
        Disconnected,
        Error
    }

    [Header("Channel")]
    [SerializeField] private string channelName = DefaultChannelName;

    [Header("When to join")]
    [Tooltip("If true, waits a short moment after scene start before joining voice.")]
    [SerializeField] private bool waitBeforeJoin = true;
    [SerializeField] private float waitBeforeJoinSeconds = 0.75f;

    [Header("Status UI")]
    [Tooltip("Optional. If empty, looks up a TMP named VoiceStatusText in the system panel.")]
    [SerializeField] private TMP_Text voiceStatusText;

    static VivoxVoiceManager s_instance;
    bool m_initializedOrInitializing;
    bool m_joined;
    bool m_cleaningUp;
    bool m_eventsSubscribed;
    bool m_channelJoinedEvent;
    VoiceUiPhase m_phase = VoiceUiPhase.Initializing;
    string m_lastUiText;
    readonly HashSet<VivoxParticipant> m_trackedParticipants = new HashSet<VivoxParticipant>();

    public static VivoxVoiceManager Instance => s_instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (s_instance != null)
            return;

        var go = new GameObject("VivoxVoiceManager");
        s_instance = go.AddComponent<VivoxVoiceManager>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (s_instance != null && s_instance != this)
        {
            Destroy(gameObject);
            return;
        }

        s_instance = this;
    }

    private async void Start()
    {
        if (m_initializedOrInitializing)
            return;

        m_initializedOrInitializing = true;

        BindVoiceStatusText();
        SetPhase(VoiceUiPhase.Initializing);
        await RunVivoxLifecycleAsync();
    }

    private void OnDestroy()
    {
        UnsubscribeVivoxEvents();

        // Best-effort cleanup (no waiting needed here).
        if (!m_cleaningUp)
            _ = CleanupAsync();
    }

    private void OnApplicationQuit()
    {
        UnsubscribeVivoxEvents();

        if (!m_cleaningUp)
            _ = CleanupAsync();
    }

    async Task RunVivoxLifecycleAsync()
    {
        try
        {
            SetPhase(VoiceUiPhase.Initializing);
            await EnsureUnityServicesInitializedAsync();
            await EnsureAnonymousAuthAsync();

            SubscribeVivoxEvents();

            await VivoxService.Instance.InitializeAsync();
            SubscribeVivoxEvents();
            Debug.Log("[VOICE] Vivox initialized");

            SetPhase(VoiceUiPhase.LoggingIn);
            await VivoxService.Instance.LoginAsync();
            Debug.Log("[VOICE] Vivox logged in");

            await EnsureMicrophonePermissionAsync();

            if (waitBeforeJoin)
                await Task.Delay(TimeSpan.FromSeconds(waitBeforeJoinSeconds));

            SetPhase(VoiceUiPhase.Joining);
            await VivoxService.Instance.JoinGroupChannelAsync(
                channelName,
                ChatCapability.AudioOnly
            );

            m_joined = true;
            Debug.Log($"[VOICE] Joined {channelName}");
            // Do not treat JoinGroupChannelAsync returning as Connected.
            // ChannelJoined + participant/audio state drive the UI.
            UpdateVoiceStatusUI();
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.LogError($"[VOICE ERROR] Microphone permission denied: {ex}");
            SetPhase(VoiceUiPhase.MicDenied);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[VOICE ERROR] {ex}");
            SetPhase(VoiceUiPhase.Error);
        }
        finally
        {
            // Intentionally keep the guard as-is:
            // this component is bootstrap-created and we want to avoid duplicate joins
            // if Unity re-enables the component.
        }
    }

    async Task EnsureUnityServicesInitializedAsync()
    {
        // UnityServices.InitializeAsync is safe to call even if already initialized,
        // but we still keep a small guard to avoid repeated awaits.
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            await UnityServices.InitializeAsync();
            Debug.Log("[VOICE] Unity Services initialized");
        }
        else
        {
            Debug.Log("[VOICE] Unity Services initialized");
        }
    }

    async Task EnsureAnonymousAuthAsync()
    {
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        Debug.Log(
            $"[VOICE] Anonymous auth success: {AuthenticationService.Instance.PlayerId}");
    }

    async Task EnsureMicrophonePermissionAsync()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        const string microphonePermission = "android.permission.RECORD_AUDIO";

        if (Permission.HasUserAuthorizedPermission(microphonePermission))
            return;

        var tcs = new TaskCompletionSource<bool>();
        var callbacks = new PermissionCallbacks();
        callbacks.PermissionGranted += _ => tcs.TrySetResult(true);
        callbacks.PermissionDenied += _ => tcs.TrySetResult(false);

        Debug.Log("[VOICE] Requesting microphone permission...");
        Permission.RequestUserPermissions(new[] { microphonePermission }, callbacks);

        bool granted = await tcs.Task;
        if (!granted)
            throw new UnauthorizedAccessException("Microphone permission denied.");
#endif
        await Task.CompletedTask;
    }

    async Task CleanupAsync()
    {
        if (m_cleaningUp)
            return;

        m_cleaningUp = true;

        try
        {
            // Leave channel first (best-effort).
            if (m_joined)
            {
                try
                {
                    await VivoxService.Instance.LeaveChannelAsync(channelName);
                    Debug.Log("[VOICE] Left channel");
                }
                catch (Exception leaveEx)
                {
                    Debug.LogError($"[VOICE ERROR] LeaveChannelAsync failed: {leaveEx.Message}");
                }

                m_joined = false;
            }

            // Logout at the end.
            try
            {
                await VivoxService.Instance.LogoutAsync();
            }
            catch (Exception logoutEx)
            {
                Debug.LogError($"[VOICE ERROR] LogoutAsync failed: {logoutEx.Message}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[VOICE ERROR] Cleanup failed: {ex}");
        }
        finally
        {
            m_channelJoinedEvent = false;
            if (m_phase != VoiceUiPhase.MicDenied && m_phase != VoiceUiPhase.Error)
                SetPhase(VoiceUiPhase.Disconnected);
            m_cleaningUp = false;
        }
    }

    void BindVoiceStatusText()
    {
        if (voiceStatusText != null)
            return;

        var go = GameObject.Find(VoiceStatusObjectName);
        if (go == null)
        {
            Debug.LogWarning("[VOICE] VoiceStatusText not found in scene.");
            return;
        }

        voiceStatusText = go.GetComponent<TMP_Text>();
        if (voiceStatusText == null)
            Debug.LogWarning("[VOICE] VoiceStatusText GameObject has no TMP_Text.");
    }

    void SetPhase(VoiceUiPhase phase)
    {
        m_phase = phase;
        UpdateVoiceStatusUI();
    }

    void UpdateVoiceStatusUI()
    {
        string text = BuildVoiceStatusText();
        if (text == m_lastUiText)
            return;

        m_lastUiText = text;
        if (voiceStatusText != null)
            voiceStatusText.text = text;
    }

    string BuildVoiceStatusText()
    {
        switch (m_phase)
        {
            case VoiceUiPhase.Initializing:
                return "VOICE: Initializing...";
            case VoiceUiPhase.LoggingIn:
                return "VOICE: Logging in...";
            case VoiceUiPhase.Joining:
                return "VOICE: Joining...";
            case VoiceUiPhase.MicDenied:
                return "VOICE: Mic permission denied";
            case VoiceUiPhase.Disconnected:
                return "VOICE: Disconnected";
            case VoiceUiPhase.Error:
                return "VOICE: Error";
            case VoiceUiPhase.InChannel:
                return BuildInChannelStatusText();
            default:
                return "VOICE: Initializing...";
        }
    }

    string BuildInChannelStatusText()
    {
        int participantCount = CountChannelParticipants(out int inAudioCount, out bool remoteSpeaking);

        // Connected only when both users are present AND in audio.
        bool connected = participantCount >= ExpectedParticipantCount &&
                         inAudioCount >= ExpectedParticipantCount;

        if (!connected)
            return "VOICE: Waiting for partner";

        if (remoteSpeaking)
            return "VOICE: Connected (2/2) | Partner speaking";

        return "VOICE: Connected (2/2)";
    }

    int CountChannelParticipants(out int inAudioCount, out bool remoteSpeaking)
    {
        inAudioCount = 0;
        remoteSpeaking = false;
        int count = 0;

        if (VivoxService.Instance == null)
            return 0;

        if (!VivoxService.Instance.ActiveChannels.TryGetValue(channelName, out var participants) ||
            participants == null)
            return 0;

        for (int i = 0; i < participants.Count; i++)
        {
            VivoxParticipant participant = participants[i];
            if (participant == null)
                continue;

            count++;
            if (participant.IsInAudio)
                inAudioCount++;

            if (!participant.IsSelf && participant.SpeechDetected)
                remoteSpeaking = true;
        }

        return count;
    }

    void SubscribeVivoxEvents()
    {
        if (m_eventsSubscribed || VivoxService.Instance == null)
            return;

        VivoxService.Instance.InitializationFailed += OnInitializationFailed;
        VivoxService.Instance.LoggedIn += OnLoggedIn;
        VivoxService.Instance.LoggedOut += OnLoggedOut;
        VivoxService.Instance.ChannelJoined += OnChannelJoined;
        VivoxService.Instance.ChannelLeft += OnChannelLeft;
        VivoxService.Instance.ParticipantAddedToChannel += OnParticipantAddedToChannel;
        VivoxService.Instance.ParticipantRemovedFromChannel += OnParticipantRemovedFromChannel;
        VivoxService.Instance.ConnectionRecovering += OnConnectionRecovering;
        VivoxService.Instance.ConnectionRecovered += OnConnectionRecovered;
        VivoxService.Instance.ConnectionFailedToRecover += OnConnectionFailedToRecover;
        m_eventsSubscribed = true;
    }

    void UnsubscribeVivoxEvents()
    {
        UnsubscribeAllParticipants();

        if (!m_eventsSubscribed)
            return;

        m_eventsSubscribed = false;

        if (VivoxService.Instance == null)
            return;

        VivoxService.Instance.InitializationFailed -= OnInitializationFailed;
        VivoxService.Instance.LoggedIn -= OnLoggedIn;
        VivoxService.Instance.LoggedOut -= OnLoggedOut;
        VivoxService.Instance.ChannelJoined -= OnChannelJoined;
        VivoxService.Instance.ChannelLeft -= OnChannelLeft;
        VivoxService.Instance.ParticipantAddedToChannel -= OnParticipantAddedToChannel;
        VivoxService.Instance.ParticipantRemovedFromChannel -= OnParticipantRemovedFromChannel;
        VivoxService.Instance.ConnectionRecovering -= OnConnectionRecovering;
        VivoxService.Instance.ConnectionRecovered -= OnConnectionRecovered;
        VivoxService.Instance.ConnectionFailedToRecover -= OnConnectionFailedToRecover;
    }

    void SubscribeParticipant(VivoxParticipant participant)
    {
        if (participant == null || !m_trackedParticipants.Add(participant))
            return;

        participant.ParticipantAudioStateChanged += OnParticipantAudioStateChanged;
        participant.ParticipantSpeechDetected += OnParticipantSpeechDetected;
    }

    void UnsubscribeParticipant(VivoxParticipant participant)
    {
        if (participant == null || !m_trackedParticipants.Remove(participant))
            return;

        participant.ParticipantAudioStateChanged -= OnParticipantAudioStateChanged;
        participant.ParticipantSpeechDetected -= OnParticipantSpeechDetected;
    }

    void UnsubscribeAllParticipants()
    {
        foreach (VivoxParticipant participant in m_trackedParticipants)
        {
            if (participant == null)
                continue;

            participant.ParticipantAudioStateChanged -= OnParticipantAudioStateChanged;
            participant.ParticipantSpeechDetected -= OnParticipantSpeechDetected;
        }

        m_trackedParticipants.Clear();
    }

    void SyncTrackedParticipants()
    {
        if (VivoxService.Instance == null)
            return;

        if (!VivoxService.Instance.ActiveChannels.TryGetValue(channelName, out var participants) ||
            participants == null)
            return;

        for (int i = 0; i < participants.Count; i++)
            SubscribeParticipant(participants[i]);
    }

    void OnInitializationFailed(Exception ex)
    {
        Debug.LogError($"[VOICE ERROR] Initialization failed: {ex}");
        SetPhase(VoiceUiPhase.Error);
    }

    void OnLoggedIn()
    {
        Debug.Log("[VOICE] LoggedIn event");
        UpdateVoiceStatusUI();
    }

    void OnLoggedOut()
    {
        Debug.Log("[VOICE] LoggedOut event");
        m_channelJoinedEvent = false;
        UnsubscribeAllParticipants();
        if (m_phase != VoiceUiPhase.MicDenied && m_phase != VoiceUiPhase.Error)
            SetPhase(VoiceUiPhase.Disconnected);
    }

    void OnChannelJoined(string joinedChannel)
    {
        if (!IsOurChannel(joinedChannel))
            return;

        m_channelJoinedEvent = true;
        Debug.Log($"[VOICE] ChannelJoined event: {joinedChannel}");
        SyncTrackedParticipants();
        SetPhase(VoiceUiPhase.InChannel);
        LogParticipantSnapshot("ChannelJoined");
    }

    void OnChannelLeft(string leftChannel)
    {
        if (!IsOurChannel(leftChannel))
            return;

        Debug.Log($"[VOICE] ChannelLeft event: {leftChannel}");
        m_channelJoinedEvent = false;
        m_joined = false;
        UnsubscribeAllParticipants();
        if (m_phase != VoiceUiPhase.MicDenied && m_phase != VoiceUiPhase.Error)
            SetPhase(VoiceUiPhase.Disconnected);
    }

    void OnParticipantAddedToChannel(VivoxParticipant participant)
    {
        if (participant == null || !IsOurChannel(participant.ChannelName))
            return;

        SubscribeParticipant(participant);
        Debug.Log(
            $"[VOICE] Participant added: id={participant.PlayerId} self={participant.IsSelf} inAudio={participant.IsInAudio}");
        if (m_channelJoinedEvent)
            SetPhase(VoiceUiPhase.InChannel);
        else
            UpdateVoiceStatusUI();
        LogParticipantSnapshot("ParticipantAdded");
    }

    void OnParticipantRemovedFromChannel(VivoxParticipant participant)
    {
        if (participant == null || !IsOurChannel(participant.ChannelName))
            return;

        UnsubscribeParticipant(participant);
        Debug.Log(
            $"[VOICE] Participant removed: id={participant.PlayerId} self={participant.IsSelf}");
        if (m_channelJoinedEvent)
            SetPhase(VoiceUiPhase.InChannel);
        else
            UpdateVoiceStatusUI();
        LogParticipantSnapshot("ParticipantRemoved");
    }

    void OnParticipantAudioStateChanged()
    {
        UpdateVoiceStatusUI();
        LogParticipantSnapshot("AudioStateChanged");
    }

    void OnParticipantSpeechDetected()
    {
        UpdateVoiceStatusUI();
    }

    void OnConnectionRecovering()
    {
        Debug.LogWarning("[VOICE] Connection recovering...");
        if (m_phase != VoiceUiPhase.MicDenied && m_phase != VoiceUiPhase.Error)
            SetPhase(VoiceUiPhase.Disconnected);
    }

    void OnConnectionRecovered()
    {
        Debug.Log("[VOICE] Connection recovered");
        if (m_channelJoinedEvent)
            SetPhase(VoiceUiPhase.InChannel);
        else
            UpdateVoiceStatusUI();
    }

    void OnConnectionFailedToRecover()
    {
        Debug.LogError("[VOICE ERROR] Connection failed to recover.");
        m_channelJoinedEvent = false;
        if (m_phase != VoiceUiPhase.MicDenied)
            SetPhase(VoiceUiPhase.Disconnected);
    }

    bool IsOurChannel(string name)
    {
        return !string.IsNullOrEmpty(name) &&
               string.Equals(name, channelName, StringComparison.Ordinal);
    }

    void LogParticipantSnapshot(string reason)
    {
        int count = CountChannelParticipants(out int inAudioCount, out bool remoteSpeaking);
        Debug.Log(
            $"[VOICE] {reason}: participants={count} inAudio={inAudioCount} remoteSpeaking={remoteSpeaking} channelJoinedEvent={m_channelJoinedEvent}");
    }
}
