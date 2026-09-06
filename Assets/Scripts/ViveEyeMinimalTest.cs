using UnityEngine;
using VIVE.OpenXR;
using VIVE.OpenXR.EyeTracker;

public class ViveEyeMinimalTest : MonoBehaviour
{
    [SerializeField, Min(0.1f)]
    private float logInterval = 0.5f;

    private float nextLogTime;

    private void Update()
    {
        // 避免每一幀都印 Log，造成大量訊息與效能負擔。
        if (Time.unscaledTime < nextLogTime)
            return;

        nextLogTime = Time.unscaledTime + logInterval;

        bool success =
            XR_HTC_eye_tracker.Interop.GetEyeGazeData(
                out XrSingleEyeGazeDataHTC[] gazes);

        if (!success)
        {
            Debug.LogWarning("[EyeTracking] GetEyeGazeData API failed.");
            return;
        }

        if (gazes == null || gazes.Length < 2)
        {
            Debug.LogWarning("[EyeTracking] Gaze array is missing or incomplete.");
            return;
        }

        int leftIndex =
            (int)XrEyePositionHTC.XR_EYE_POSITION_LEFT_HTC;

        int rightIndex =
            (int)XrEyePositionHTC.XR_EYE_POSITION_RIGHT_HTC;

        LogEyeData("Left", gazes[leftIndex]);
        LogEyeData("Right", gazes[rightIndex]);
    }

    private static void LogEyeData(
        string eyeName,
        XrSingleEyeGazeDataHTC gaze)
    {
        if (!gaze.isValid)
        {
            Debug.LogWarning($"[EyeTracking] {eyeName} valid=False");
            return;
        }

        Vector3 origin =
            gaze.gazePose.position.ToUnityVector();

        Quaternion rotation =
            gaze.gazePose.orientation.ToUnityQuaternion();

        Vector3 direction =
            (rotation * Vector3.forward).normalized;

        Debug.Log(
            $"[EyeTracking] {eyeName} valid=True | " +
            $"origin={origin:F4} | " +
            $"direction={direction:F4}");
    }
}