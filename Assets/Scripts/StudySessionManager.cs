using UnityEngine;
using Unity.Netcode;
using TMPro;

public class StudySessionManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ViveEyeResearchRecorder recorder;

    [Header("UI")]
    [SerializeField] private TMP_Text participantText;
    [SerializeField] private TMP_Text sessionText;
    [SerializeField] private TMP_Text recordingStatusText;

    [Header("Session Settings")]
    [SerializeField] private int participantNumber = 1;
    [SerializeField] private int sessionNumber = 1;

    private bool isRecording = false;

    private void Start()
    {
        RefreshUI();

        if (recordingStatusText != null)
            recordingStatusText.text = "NOT RECORDING";
    }

    public void NextParticipant()
    {
        if (isRecording) return;

        participantNumber++;
        RefreshUI();
    }

    public void PreviousParticipant()
    {
        if (isRecording) return;

        participantNumber = Mathf.Max(1, participantNumber - 1);
        RefreshUI();
    }

    public void NextSession()
    {
        if (isRecording) return;

        sessionNumber++;
        RefreshUI();
    }

    public void PreviousSession()
    {
        if (isRecording) return;

        sessionNumber = Mathf.Max(1, sessionNumber - 1);
        RefreshUI();
    }

    public void StartStudyRecording()
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager != null && networkManager.IsListening)
        {
            if (!networkManager.IsServer)
            {
                Debug.LogWarning(
                    "[StudySession] Only Host can start recording.");
                return;
            }

            ExperimentFlowManager flowManager =
                FindFirstObjectByType<ExperimentFlowManager>();

            if (flowManager != null && flowManager.IsSpawned)
            {
                flowManager.HostStartRecording();
                return;
            }
        }

        StartStudyRecordingLocal();
    }

    public void StopStudyRecording()
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager != null && networkManager.IsListening)
        {
            if (!networkManager.IsServer)
            {
                Debug.LogWarning(
                    "[StudySession] Only Host can stop recording.");
                return;
            }

            ExperimentFlowManager flowManager =
                FindFirstObjectByType<ExperimentFlowManager>();

            if (flowManager != null && flowManager.IsSpawned)
            {
                flowManager.HostStopRecording();
                return;
            }
        }

        StopStudyRecordingLocal();
    }

    public void StartStudyRecordingFromNetwork()
    {
        StartStudyRecordingLocal();
    }

    public void StopStudyRecordingFromNetwork()
    {
        StopStudyRecordingLocal();
    }

    private void StartStudyRecordingLocal()
    {
        if (recorder == null)
        {
            Debug.LogError("[StudySession] Recorder missing.");
            return;
        }

        if (isRecording)
        {
            Debug.LogWarning("[StudySession] Already recording.");
            return;
        }

        string participantId = $"P{participantNumber:000}";
        string sessionId = $"S{sessionNumber:00}";

        recorder.ConfigureSession(participantId, sessionId);

        recorder.StartRecording();

        isRecording = true;

        if (recordingStatusText != null)
        {
            recordingStatusText.text =
                $"RECORDING\n{participantId} | {sessionId}";
        }

        Debug.Log(
            $"[StudySession] Recording started: " +
            $"{participantId}, {sessionId}"
        );
    }

    private void StopStudyRecordingLocal()
    {
        if (!isRecording)
            return;

        recorder.StopRecording();

        isRecording = false;

        if (recordingStatusText != null)
            recordingStatusText.text = "RECORDING STOPPED";

        Debug.Log("[StudySession] Recording stopped.");
    }

    private void RefreshUI()
    {
        if (participantText != null)
            participantText.text = $"P{participantNumber:000}";

        if (sessionText != null)
            sessionText.text = $"S{sessionNumber:00}";
    }
}
