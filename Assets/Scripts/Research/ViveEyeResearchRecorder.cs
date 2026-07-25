using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using VIVE.OpenXR;
using VIVE.OpenXR.EyeTracker;

public class ViveEyeResearchRecorder : MonoBehaviour
{
    [Header("Participant Metadata")]
    [SerializeField] private string participantId = "P001";
    [SerializeField] private string sessionId = "S01";
    [SerializeField] private string conditionId = "Baseline";

    [Header("XR References")]
    [Tooltip("指定 XR Origin/Camera Offset")]
    [SerializeField] private Transform trackingSpace;

    [Tooltip("指定 XR Origin/Camera Offset/Main Camera")]
    [SerializeField] private Transform headTransform;

    [Header("Sampling")]
    [SerializeField, Range(1, 120)]
    private int sampleRateHz = 60;

    [SerializeField, Min(1)]
    private int flushEverySamples = 120;

    [Header("Gaze Hit Test")]
    [SerializeField, Min(0.1f)]
    private float maxRayDistance = 20f;

    [SerializeField]
    private LayerMask hitLayers = ~0;

    [Header("Recording")]
    [SerializeField]
    private bool startAutomatically = true;

    public bool IsRecording { get; private set; }
    public string CurrentFilePath { get; private set; }

    private StreamWriter writer;
    private double recordingStartTime;
    private double nextSampleTime;

    private int sampleIndex;
    private int samplesSinceFlush;

    private static readonly CultureInfo Invariant =
        CultureInfo.InvariantCulture;

    private void Start()
    {
        if (startAutomatically)
            StartRecording();
    }

    private void Update()
    {
        if (!IsRecording || writer == null)
            return;

        double now = Time.realtimeSinceStartupAsDouble;

        if (now < nextSampleTime)
            return;

        double interval = 1.0 / Mathf.Max(1, sampleRateHz);

        // 掉幀時不補寫多筆相同資料，只更新到下一個合理時間點。
        do
        {
            nextSampleTime += interval;
        }
        while (nextSampleTime <= now);

        RecordOneSample(now);
    }

    public void StartRecording()
    {
        if (IsRecording)
            return;

        string safeParticipant = SanitizeFileName(participantId);
        string safeSession = SanitizeFileName(sessionId);
        string safeCondition = SanitizeFileName(conditionId);

        string folderPath = Path.Combine(
            Application.persistentDataPath,
            "EyeTrackingData");

        Directory.CreateDirectory(folderPath);

        string fileName =
            $"{safeParticipant}_{safeSession}_{safeCondition}_" +
            $"{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";

        CurrentFilePath = Path.Combine(folderPath, fileName);

        writer = new StreamWriter(
            CurrentFilePath,
            false,
            new UTF8Encoding(false));

        WriteHeader();

        recordingStartTime = Time.realtimeSinceStartupAsDouble;
        nextSampleTime = recordingStartTime;

        sampleIndex = 0;
        samplesSinceFlush = 0;
        IsRecording = true;

        Debug.Log(
            $"[EyeResearch] Recording started\n{CurrentFilePath}");
    }

    public void StopRecording()
    {
        if (!IsRecording && writer == null)
            return;

        IsRecording = false;

        if (writer != null)
        {
            writer.Flush();
            writer.Close();
            writer.Dispose();
            writer = null;
        }

        Debug.Log(
            $"[EyeResearch] Recording saved\n{CurrentFilePath}");
    }

    public void SetExperimentMetadata(
        string newParticipantId,
        string newSessionId,
        string newConditionId)
    {
        if (IsRecording)
        {
            Debug.LogWarning(
                "[EyeResearch] 錄製中不可更改 metadata。請先 StopRecording。");
            return;
        }

        participantId = newParticipantId;
        sessionId = newSessionId;
        conditionId = newConditionId;
    }

    private void RecordOneSample(double currentTime)
    {
        int leftIndex =
            (int)XrEyePositionHTC.XR_EYE_POSITION_LEFT_HTC;

        int rightIndex =
            (int)XrEyePositionHTC.XR_EYE_POSITION_RIGHT_HTC;

        // =====================================================
        // 1. Gaze data
        // =====================================================

        bool gazeApiSuccess =
            XR_HTC_eye_tracker.Interop.GetEyeGazeData(
                out XrSingleEyeGazeDataHTC[] gazeData);

        bool gazeArrayValid =
            gazeApiSuccess &&
            gazeData != null &&
            gazeData.Length >= 2;

        bool leftValid = false;
        bool rightValid = false;

        Vector3 leftOrigin = Vector3.zero;
        Vector3 leftDirection = Vector3.zero;

        Vector3 rightOrigin = Vector3.zero;
        Vector3 rightDirection = Vector3.zero;

        if (gazeArrayValid)
        {
            leftValid = TryConvertGazeToWorld(
                gazeData[leftIndex],
                out leftOrigin,
                out leftDirection);

            rightValid = TryConvertGazeToWorld(
                gazeData[rightIndex],
                out rightOrigin,
                out rightDirection);
        }

        // =====================================================
        // 2. Derived combined gaze
        // =====================================================

        bool combinedValid = false;

        Vector3 combinedOrigin = Vector3.zero;
        Vector3 combinedDirection = Vector3.zero;

        if (leftValid && rightValid)
        {
            combinedOrigin =
                (leftOrigin + rightOrigin) * 0.5f;

            combinedDirection =
                (leftDirection + rightDirection).normalized;

            combinedValid =
                combinedDirection.sqrMagnitude > 0.001f;
        }
        else if (leftValid)
        {
            combinedOrigin = leftOrigin;
            combinedDirection = leftDirection;
            combinedValid = true;
        }
        else if (rightValid)
        {
            combinedOrigin = rightOrigin;
            combinedDirection = rightDirection;
            combinedValid = true;
        }

        // =====================================================
        // 3. Pupil diameter
        // =====================================================

        bool pupilApiSuccess =
            XR_HTC_eye_tracker.Interop.GetEyePupilData(
                out XrSingleEyePupilDataHTC[] pupilData);

        bool pupilArrayValid =
            pupilApiSuccess &&
            pupilData != null &&
            pupilData.Length >= 2;

        bool leftPupilValid = false;
        bool rightPupilValid = false;

        float leftPupilDiameter = 0f;
        float rightPupilDiameter = 0f;

        if (pupilArrayValid)
        {
            leftPupilValid =
                pupilData[leftIndex].isDiameterValid;

            rightPupilValid =
                pupilData[rightIndex].isDiameterValid;

            if (leftPupilValid)
            {
                leftPupilDiameter =
                    pupilData[leftIndex].pupilDiameter;
            }

            if (rightPupilValid)
            {
                rightPupilDiameter =
                    pupilData[rightIndex].pupilDiameter;
            }
        }

        // =====================================================
        // 4. Eye openness
        // =====================================================

        bool geometricApiSuccess =
            XR_HTC_eye_tracker.Interop.GetEyeGeometricData(
                out XrSingleEyeGeometricDataHTC[] geometricData);

        bool geometricArrayValid =
            geometricApiSuccess &&
            geometricData != null &&
            geometricData.Length >= 2;

        bool leftOpennessValid = false;
        bool rightOpennessValid = false;

        float leftOpenness = 0f;
        float rightOpenness = 0f;

        if (geometricArrayValid)
        {
            leftOpennessValid =
                geometricData[leftIndex].isValid;

            rightOpennessValid =
                geometricData[rightIndex].isValid;

            if (leftOpennessValid)
            {
                leftOpenness =
                    geometricData[leftIndex].eyeOpenness;
            }

            if (rightOpennessValid)
            {
                rightOpenness =
                    geometricData[rightIndex].eyeOpenness;
            }
        }

        // =====================================================
        // 5. Head pose
        // =====================================================

        bool headValid = headTransform != null;

        Vector3 headPosition =
            headValid ? headTransform.position : Vector3.zero;

        Quaternion headRotation =
            headValid ? headTransform.rotation : Quaternion.identity;

        // =====================================================
        // 6. Gaze hit
        // =====================================================

        bool hitValid = false;
        string hitObjectName = string.Empty;
        Vector3 hitPoint = Vector3.zero;

        if (combinedValid &&
            Physics.Raycast(
                combinedOrigin,
                combinedDirection,
                out RaycastHit hit,
                maxRayDistance,
                hitLayers,
                QueryTriggerInteraction.Ignore))
        {
            hitValid = true;
            hitObjectName = hit.collider.gameObject.name;
            hitPoint = hit.point;
        }

        // =====================================================
        // 7. Write row
        // =====================================================

        double sessionTime =
            currentTime - recordingStartTime;

        StringBuilder row = new StringBuilder(1024);

        AddField(row, EscapeCsv(participantId));
        AddField(row, EscapeCsv(sessionId));
        AddField(row, EscapeCsv(conditionId));

        AddField(
            row,
            DateTime.UtcNow.ToString("O", Invariant));

        AddField(row, FormatDouble(sessionTime));
        AddField(row, Time.frameCount.ToString(Invariant));
        AddField(row, sampleIndex.ToString(Invariant));

        AddField(row, BoolTo01(gazeApiSuccess));

        AddField(row, BoolTo01(leftValid));
        AddVector3(row, leftOrigin, leftValid);
        AddVector3(row, leftDirection, leftValid);

        AddField(row, BoolTo01(rightValid));
        AddVector3(row, rightOrigin, rightValid);
        AddVector3(row, rightDirection, rightValid);

        AddField(row, BoolTo01(combinedValid));
        AddVector3(row, combinedOrigin, combinedValid);
        AddVector3(row, combinedDirection, combinedValid);

        AddField(row, BoolTo01(leftPupilValid));
        AddNullableFloat(
            row,
            leftPupilDiameter,
            leftPupilValid);

        AddField(row, BoolTo01(rightPupilValid));
        AddNullableFloat(
            row,
            rightPupilDiameter,
            rightPupilValid);

        AddField(row, BoolTo01(leftOpennessValid));
        AddNullableFloat(
            row,
            leftOpenness,
            leftOpennessValid);

        AddField(row, BoolTo01(rightOpennessValid));
        AddNullableFloat(
            row,
            rightOpenness,
            rightOpennessValid);

        AddField(row, BoolTo01(headValid));
        AddVector3(row, headPosition, headValid);
        AddQuaternion(row, headRotation, headValid);

        AddField(row, BoolTo01(hitValid));
        AddField(row, EscapeCsv(hitObjectName));
        AddVector3(row, hitPoint, hitValid);

        writer.WriteLine(row.ToString());

        sampleIndex++;
        samplesSinceFlush++;

        if (samplesSinceFlush >= flushEverySamples)
        {
            writer.Flush();
            samplesSinceFlush = 0;
        }
    }

    private bool TryConvertGazeToWorld(
        XrSingleEyeGazeDataHTC gaze,
        out Vector3 worldOrigin,
        out Vector3 worldDirection)
    {
        worldOrigin = Vector3.zero;
        worldDirection = Vector3.zero;

        if (!gaze.isValid)
            return false;

        Vector3 localOrigin =
            gaze.gazePose.position.ToUnityVector();

        Quaternion localRotation =
            gaze.gazePose.orientation.ToUnityQuaternion();

        Vector3 localDirection =
            (localRotation * Vector3.forward).normalized;

        if (trackingSpace != null)
        {
            worldOrigin =
                trackingSpace.TransformPoint(localOrigin);

            worldDirection =
                trackingSpace
                    .TransformDirection(localDirection)
                    .normalized;
        }
        else
        {
            worldOrigin = localOrigin;
            worldDirection = localDirection;
        }

        return worldDirection.sqrMagnitude > 0.001f;
    }

    private void WriteHeader()
    {
        writer.WriteLine(
            "participant_id,session_id,condition_id," +

            "timestamp_utc,session_time_s,unity_frame,sample_index," +
            "gaze_api_success," +

            "left_valid," +
            "left_origin_x,left_origin_y,left_origin_z," +
            "left_direction_x,left_direction_y,left_direction_z," +

            "right_valid," +
            "right_origin_x,right_origin_y,right_origin_z," +
            "right_direction_x,right_direction_y,right_direction_z," +

            "combined_valid," +
            "combined_origin_x,combined_origin_y,combined_origin_z," +
            "combined_direction_x,combined_direction_y,combined_direction_z," +

            "left_pupil_valid,left_pupil_diameter," +
            "right_pupil_valid,right_pupil_diameter," +

            "left_openness_valid,left_openness," +
            "right_openness_valid,right_openness," +

            "head_valid," +
            "head_position_x,head_position_y,head_position_z," +
            "head_rotation_x,head_rotation_y," +
            "head_rotation_z,head_rotation_w," +

            "hit_valid,hit_object," +
            "hit_point_x,hit_point_y,hit_point_z"
        );
    }

    private static void AddField(
        StringBuilder builder,
        string value)
    {
        if (builder.Length > 0)
            builder.Append(',');

        builder.Append(value);
    }

    private static void AddVector3(
        StringBuilder builder,
        Vector3 value,
        bool valid)
    {
        AddField(
            builder,
            valid ? FormatFloat(value.x) : string.Empty);

        AddField(
            builder,
            valid ? FormatFloat(value.y) : string.Empty);

        AddField(
            builder,
            valid ? FormatFloat(value.z) : string.Empty);
    }

    private static void AddQuaternion(
        StringBuilder builder,
        Quaternion value,
        bool valid)
    {
        AddField(
            builder,
            valid ? FormatFloat(value.x) : string.Empty);

        AddField(
            builder,
            valid ? FormatFloat(value.y) : string.Empty);

        AddField(
            builder,
            valid ? FormatFloat(value.z) : string.Empty);

        AddField(
            builder,
            valid ? FormatFloat(value.w) : string.Empty);
    }

    private static void AddNullableFloat(
        StringBuilder builder,
        float value,
        bool valid)
    {
        AddField(
            builder,
            valid ? FormatFloat(value) : string.Empty);
    }

    private static string FormatFloat(float value)
    {
        if (float.IsNaN(value) ||
            float.IsInfinity(value))
        {
            return string.Empty;
        }

        return value.ToString("R", Invariant);
    }

    private static string FormatDouble(double value)
    {
        if (double.IsNaN(value) ||
            double.IsInfinity(value))
        {
            return string.Empty;
        }

        return value.ToString("R", Invariant);
    }

    private static string BoolTo01(bool value)
    {
        return value ? "1" : "0";
    }

    private static string EscapeCsv(string value)
    {
        value ??= string.Empty;

        return "\"" +
               value.Replace("\"", "\"\"") +
               "\"";
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Unknown";

        foreach (char invalidChar
                 in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalidChar, '_');
        }

        return value.Trim();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused && writer != null)
            writer.Flush();
    }

    private void OnApplicationQuit()
    {
        StopRecording();
    }

    private void OnDestroy()
    {
        StopRecording();
    }
}