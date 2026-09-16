using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
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
    const string VoiceToggleObjectName = "VoiceToggleButton";
    const string VoiceVolumeObjectName = "VoiceVolumeSlider";
    const int ExpectedParticipantCount = 2;
    const int MaxJoinAttempts = 3;
    const int OutputVolumeMinDb = -50;
    const int OutputVolumeMaxDb = 10;

    enum VoiceUiPhase
    {
        Initializing,
        PermissionRequired,
        LoggingIn,
        Joining,
        Retrying,
        InChannel,
        MicDenied,
        JoinFailed,
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
    [SerializeField] private Button voiceToggleButton;
    [SerializeField] private Slider voiceVolumeSlider;

    static VivoxVoiceManager s_instance;
    bool m_initializedOrInitializing;
    bool m_joined;
    bool m_cleaningUp;
    bool m_eventsSubscribed;
    bool m_channelJoinedEvent;
    bool m_voiceEnabled = true;
    float m_outputVolume01 = 50f / 60f;
    int m_joinAttempt;
    VoiceUiPhase m_phase = VoiceUiPhase.Initializing;
    string m_lastUiText;
    CancellationTokenSource m_lifetimeCts;
    readonly HashSet<VivoxParticipant> m_trackedParticipants = new HashSet<VivoxParticipant>();

    public static VivoxVoiceManager Instance => s_instance;

    public bool IsVoiceEnabled => m_voiceEnabled;

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
        m_lifetimeCts = new CancellationTokenSource();
    }

    private async void Start()
    {
        if (m_initializedOrInitializing)
            return;

        m_initializedOrInitializing = true;

        BindVoiceStatusText();
        BindVoiceControls();
        SetPhase(VoiceUiPhase.Initializing);
        await RunVivoxLifecycleAsync();
    }

    private void OnDestroy()
    {
        CancelLifetime();
        UnsubscribeVivoxEvents();

        if (!m_cleaningUp)
            _ = CleanupAsync();
    }

    private void OnApplicationQuit()
    {
        CancelLifetime();
        UnsubscribeVivoxEvents();

        if (!m_cleaningUp)
            _ = CleanupAsync();
    }

    public void ToggleVoice()
    {
        SetVoiceEnabled(!m_voiceEnabled);
    }

    public void SetVoiceEnabled(bool enabled)
    {
        m_voiceEnabled = enabled;
        ApplyVoiceMuteState();
        UpdateVoiceStatusUI();
        RefreshToggleLabel();
        Debug.Log($"[VOICE] Voice {(m_voiceEnabled ? "ON" : "OFF")}");
    }

    public void SetVoiceOutputVolume(float value)
    {
        m_outputVolume01 = Mathf.Clamp01(value);
        ApplyOutputVolume();
    }

    public async void RebindVoiceDevices()
    {
        try
        {
            if (VivoxService.Instance == null)
            {
                LogVoiceDevices("Rebind skipped");
                return;
            }

            VivoxInputDevice input = VivoxService.Instance.EffectiveInputDevice;
            VivoxOutputDevice output = VivoxService.Instance.EffectiveOutputDevice;

            if (input != null)
                await VivoxService.Instance.SetActiveInputDeviceAsync(input);
            if (output != null)
                await VivoxService.Instance.SetActiveOutputDeviceAsync(output);

            ApplyVoiceMuteState();
            ApplyOutputVolume();
            LogVoiceDevices("RebindVoiceDevices");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VOICE DEVICE] Rebind failed: {ex.Message}");
            LogVoiceDevices("Rebind failed");
        }
    }

    async Task RunVivoxLifecycleAsync()
    {
        CancellationToken ct = LifetimeToken();

        try
        {
            SetPhase(VoiceUiPhase.Initializing);
            await EnsureUnityServicesInitializedAsync();
            ThrowIfCanceled(ct);
            await EnsureAnonymousAuthAsync();
            ThrowIfCanceled(ct);

            SubscribeVivoxEvents();

            await VivoxService.Instance.InitializeAsync();
            SubscribeVivoxEvents();
            Debug.Log("[VOICE] Vivox initialized");
            LogVoiceDevices("After Initialize");

            SetPhase(VoiceUiPhase.LoggingIn);
            await VivoxService.Instance.LoginAsync();
            ThrowIfCanceled(ct);
            Debug.Log("[VOICE] Vivox logged in");

            bool micGranted = await EnsureMicrophonePermissionAsync();
            ThrowIfCanceled(ct);
            if (!micGranted)
            {
                SetPhase(VoiceUiPhase.MicDenied);
                return;
            }

            if (waitBeforeJoin)
                await DelayCancellable(TimeSpan.FromSeconds(waitBeforeJoinSeconds), ct);

            await JoinChannelWithRetryAsync(ct);
        }
        catch (OperationCanceledException)
        {
            Debug.Log("[VOICE] Lifecycle canceled");
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.LogError($"[VOICE ERROR] Microphone permission denied: {ex}");
            SetPhase(VoiceUiPhase.MicDenied);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[VOICE ERROR] {ex}");
            SetPhase(LooksLikeNetworkError(ex)
                ? VoiceUiPhase.JoinFailed
                : VoiceUiPhase.Error);
        }
    }

    async Task JoinChannelWithRetryAsync(CancellationToken ct)
    {
        Exception lastError = null;

        for (int attempt = 1; attempt <= MaxJoinAttempts; attempt++)
        {
            ThrowIfCanceled(ct);
            m_joinAttempt = attempt;

            if (attempt == 1)
                SetPhase(VoiceUiPhase.Joining);
            else
                SetPhase(VoiceUiPhase.Retrying);

            try
            {
                await VivoxService.Instance.JoinGroupChannelAsync(
                    channelName,
                    ChatCapability.AudioOnly
                );

                m_joined = true;
                Debug.Log($"[VOICE] Joined {channelName} (attempt {attempt}/{MaxJoinAttempts})");
                LogVoiceDevices("After Join");
                ApplyVoiceMuteState();
                ApplyOutputVolume();
                UpdateVoiceStatusUI();
                return;
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                Debug.LogWarning(
                    $"[VOICE] Join attempt {attempt}/{MaxJoinAttempts} failed: {ex.Message}");

                if (attempt >= MaxJoinAttempts)
                    break;

                int delayMs = 1000 * (1 << (attempt - 1));
                await DelayCancellable(TimeSpan.FromMilliseconds(delayMs), ct);
            }
        }

        if (LooksLikeNetworkError(lastError))
            SetPhase(VoiceUiPhase.JoinFailed);
        else
            SetPhase(VoiceUiPhase.Error);

        if (lastError != null)
            Debug.LogError($"[VOICE ERROR] Join failed after {MaxJoinAttempts} attempts: {lastError}");
    }

    async Task EnsureUnityServicesInitializedAsync()
    {
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

    async Task<bool> EnsureMicrophonePermissionAsync()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        const string microphonePermission = "android.permission.RECORD_AUDIO";
        const string bluetoothConnectPermission = "android.permission.BLUETOOTH_CONNECT";

        var needed = new List<string>();
        if (!Permission.HasUserAuthorizedPermission(microphonePermission))
            needed.Add(microphonePermission);

        if (AndroidSdkInt() >= 31 &&
            !Permission.HasUserAuthorizedPermission(bluetoothConnectPermission))
        {
            needed.Add(bluetoothConnectPermission);
        }

        if (needed.Count == 0)
            return true;

        SetPhase(VoiceUiPhase.PermissionRequired);

        var tcs = new TaskCompletionSource<bool>();
        var callbacks = new PermissionCallbacks();
        int remaining = needed.Count;
        bool micGranted = Permission.HasUserAuthorizedPermission(microphonePermission);

        void CompleteOne(string permission, bool granted)
        {
            if (permission == microphonePermission)
                micGranted = granted;
            remaining--;
            if (remaining <= 0)
                tcs.TrySetResult(micGranted);
        }

        callbacks.PermissionGranted += permission => CompleteOne(permission, true);
        callbacks.PermissionDenied += permission => CompleteOne(permission, false);
        callbacks.PermissionDeniedAndDontAskAgain += permission => CompleteOne(permission, false);

        Debug.Log("[VOICE] Requesting microphone permission...");
        Permission.RequestUserPermissions(needed.ToArray(), callbacks);

        bool grantedResult = await tcs.Task;
        if (!grantedResult)
            Debug.LogError("[VOICE ERROR] RECORD_AUDIO denied.");

        return grantedResult;
#else
        await Task.CompletedTask;
        return true;
#endif
    }

    async Task CleanupAsync()
    {
        if (m_cleaningUp)
            return;

        m_cleaningUp = true;

        try
        {
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
            if (m_phase != VoiceUiPhase.MicDenied &&
                m_phase != VoiceUiPhase.Error &&
                m_phase != VoiceUiPhase.JoinFailed)
            {
                SetPhase(VoiceUiPhase.Disconnected);
            }

            m_cleaningUp = false;
        }
    }

    void ApplyVoiceMuteState()
    {
        if (VivoxService.Instance == null)
            return;

        try
        {
            if (m_voiceEnabled)
            {
                VivoxService.Instance.UnmuteInputDevice();
                VivoxService.Instance.UnmuteOutputDevice();
            }
            else
            {
                VivoxService.Instance.MuteInputDevice();
                VivoxService.Instance.MuteOutputDevice();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VOICE] Mute state apply failed: {ex.Message}");
        }
    }

    void ApplyOutputVolume()
    {
        if (VivoxService.Instance == null)
            return;

        int db = Mathf.RoundToInt(
            Mathf.Lerp(OutputVolumeMinDb, OutputVolumeMaxDb, m_outputVolume01));
        db = Mathf.Clamp(db, OutputVolumeMinDb, OutputVolumeMaxDb);

        try
        {
            VivoxService.Instance.SetOutputDeviceVolume(db);
            Debug.Log($"[VOICE] Output volume {m_outputVolume01:F2} -> {db} dB");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VOICE] SetOutputDeviceVolume failed: {ex.Message}");
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

    void BindVoiceControls()
    {
        if (voiceToggleButton == null)
        {
            var toggleGo = GameObject.Find(VoiceToggleObjectName);
            if (toggleGo != null)
                voiceToggleButton = toggleGo.GetComponent<Button>();
        }

        if (voiceToggleButton != null)
        {
            voiceToggleButton.onClick.RemoveListener(ToggleVoice);
            voiceToggleButton.onClick.AddListener(ToggleVoice);
            RefreshToggleLabel();
        }

        if (voiceVolumeSlider == null)
        {
            var sliderGo = GameObject.Find(VoiceVolumeObjectName);
            if (sliderGo != null)
                voiceVolumeSlider = sliderGo.GetComponent<Slider>();
        }

        if (voiceVolumeSlider != null)
        {
            voiceVolumeSlider.minValue = 0f;
            voiceVolumeSlider.maxValue = 1f;
            voiceVolumeSlider.SetValueWithoutNotify(m_outputVolume01);
            voiceVolumeSlider.onValueChanged.RemoveListener(SetVoiceOutputVolume);
            voiceVolumeSlider.onValueChanged.AddListener(SetVoiceOutputVolume);
        }
    }

    void RefreshToggleLabel()
    {
        if (voiceToggleButton == null)
            return;

        TMP_Text label = voiceToggleButton.GetComponentInChildren<TMP_Text>();
        if (label != null)
            label.text = m_voiceEnabled ? "Voice ON" : "Voice OFF";
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
                return "VOICE: Initializing";
            case VoiceUiPhase.PermissionRequired:
                return "VOICE: Permission required";
            case VoiceUiPhase.LoggingIn:
                return "VOICE: Logging in";
            case VoiceUiPhase.Joining:
                return "VOICE: Joining";
            case VoiceUiPhase.Retrying:
                return $"VOICE: Retrying ({m_joinAttempt}/{MaxJoinAttempts})";
            case VoiceUiPhase.MicDenied:
                return "VOICE: Permission denied";
            case VoiceUiPhase.JoinFailed:
                return "VOICE: No internet / Join failed";
            case VoiceUiPhase.Disconnected:
                return "VOICE: Disconnected";
            case VoiceUiPhase.Error:
                return "VOICE: Error";
            case VoiceUiPhase.InChannel:
                return BuildInChannelStatusText();
            default:
                return "VOICE: Initializing";
        }
    }

    string BuildInChannelStatusText()
    {
        if (!m_voiceEnabled)
            return "VOICE: Voice muted";

        int participantCount = CountChannelParticipants(out int inAudioCount, out bool remoteSpeaking);

        bool connected = participantCount >= ExpectedParticipantCount &&
                         inAudioCount >= ExpectedParticipantCount;

        if (!connected)
            return $"VOICE: Waiting for partner ({Mathf.Max(participantCount, 1)}/2)";

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
        VivoxService.Instance.AvailableInputDevicesChanged += OnAvailableInputDevicesChanged;
        VivoxService.Instance.EffectiveInputDeviceChanged += OnEffectiveInputDeviceChanged;
        VivoxService.Instance.AvailableOutputDevicesChanged += OnAvailableOutputDevicesChanged;
        VivoxService.Instance.EffectiveOutputDeviceChanged += OnEffectiveOutputDeviceChanged;
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
        VivoxService.Instance.AvailableInputDevicesChanged -= OnAvailableInputDevicesChanged;
        VivoxService.Instance.EffectiveInputDeviceChanged -= OnEffectiveInputDeviceChanged;
        VivoxService.Instance.AvailableOutputDevicesChanged -= OnAvailableOutputDevicesChanged;
        VivoxService.Instance.EffectiveOutputDeviceChanged -= OnEffectiveOutputDeviceChanged;
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
        if (m_phase != VoiceUiPhase.MicDenied &&
            m_phase != VoiceUiPhase.Error &&
            m_phase != VoiceUiPhase.JoinFailed)
        {
            SetPhase(VoiceUiPhase.Disconnected);
        }
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
        LogVoiceDevices("ChannelJoined");
    }

    void OnChannelLeft(string leftChannel)
    {
        if (!IsOurChannel(leftChannel))
            return;

        Debug.Log($"[VOICE] ChannelLeft event: {leftChannel}");
        m_channelJoinedEvent = false;
        m_joined = false;
        UnsubscribeAllParticipants();
        if (m_phase != VoiceUiPhase.MicDenied &&
            m_phase != VoiceUiPhase.Error &&
            m_phase != VoiceUiPhase.JoinFailed)
        {
            SetPhase(VoiceUiPhase.Disconnected);
        }
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
        if (m_phase != VoiceUiPhase.MicDenied &&
            m_phase != VoiceUiPhase.Error &&
            m_phase != VoiceUiPhase.JoinFailed)
        {
            SetPhase(VoiceUiPhase.Disconnected);
        }
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

    void OnAvailableInputDevicesChanged()
    {
        LogVoiceDevices("AvailableInputDevicesChanged");
    }

    void OnEffectiveInputDeviceChanged()
    {
        LogVoiceDevices("EffectiveInputDeviceChanged");
    }

    void OnAvailableOutputDevicesChanged()
    {
        LogVoiceDevices("AvailableOutputDevicesChanged");
    }

    void OnEffectiveOutputDeviceChanged()
    {
        LogVoiceDevices("EffectiveOutputDeviceChanged");
    }

    void LogVoiceDevices(string reason)
    {
        string input = "(none)";
        string output = "(none)";

        if (VivoxService.Instance != null)
        {
            if (VivoxService.Instance.EffectiveInputDevice != null)
                input = VivoxService.Instance.EffectiveInputDevice.DeviceName;
            if (VivoxService.Instance.EffectiveOutputDevice != null)
                output = VivoxService.Instance.EffectiveOutputDevice.DeviceName;
        }

        Debug.Log($"[VOICE DEVICE] {reason} input={input} output={output}");
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

    static bool LooksLikeNetworkError(Exception ex)
    {
        if (ex == null)
            return false;

        string text = ex.ToString().ToLowerInvariant();
        return text.Contains("network") ||
               text.Contains("internet") ||
               text.Contains("offline") ||
               text.Contains("timeout") ||
               text.Contains("unreachable") ||
               text.Contains("noconnection") ||
               text.Contains("http") ||
               text.Contains("dns");
    }

    CancellationToken LifetimeToken()
    {
        return m_lifetimeCts != null ? m_lifetimeCts.Token : CancellationToken.None;
    }

    void CancelLifetime()
    {
        if (m_lifetimeCts == null)
            return;

        try
        {
            if (!m_lifetimeCts.IsCancellationRequested)
                m_lifetimeCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    static async Task DelayCancellable(TimeSpan delay, CancellationToken ct)
    {
        await Task.Delay(delay, ct);
    }

    static void ThrowIfCanceled(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    static int AndroidSdkInt()
    {
        using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
            return version.GetStatic<int>("SDK_INT");
    }
#endif
}
