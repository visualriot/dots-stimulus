using System.Threading;
using UnityEngine;
using UnityEngine.XR;

namespace DotsStimulus.EyeTracking
{
    public interface IEyeTrackingProvider
    {
        string ProviderName { get; }
        bool IsAvailable { get; }
        bool IsCapturing { get; }
        string Status { get; }
    }

    public interface INativeEyeTrackingProvider : IEyeTrackingProvider
    {
        long ReceivedCount { get; }
        long DroppedCount { get; }
        bool StartCapture();
        void StopCapture();
        bool TryDequeue(out NativeEyeSample sample);
    }

    public struct FrameGazeSample
    {
        public bool valid;
        public Vector3 originWorld;
        public Vector3 directionWorld;
        public InputTrackingState trackingState;
        public string source;
    }

    /// <summary>Primitive-only copy of SRanipal EyeData, safe to create on its native callback thread.</summary>
    public struct NativeEyeSample
    {
        public long hostStopwatchTicks;
        public long hostUtcTicks;
        public long sranipalTimestamp;
        public ulong leftValidity, rightValidity, combinedValidity;
        public float leftOriginX, leftOriginY, leftOriginZ, leftDirectionX, leftDirectionY, leftDirectionZ;
        public float rightOriginX, rightOriginY, rightOriginZ, rightDirectionX, rightDirectionY, rightDirectionZ;
        public float combinedOriginX, combinedOriginY, combinedOriginZ, combinedDirectionX, combinedDirectionY, combinedDirectionZ;
        public float leftOpenness, rightOpenness, combinedOpenness;
        public float leftPupilMm, rightPupilMm, combinedPupilMm;
        public float leftPupilX, leftPupilY, rightPupilX, rightPupilY, combinedPupilX, combinedPupilY;
        public float convergenceDistanceMm;
        public bool convergenceValid;
    }

    public enum RecordingState { Idle, Recording, Saved, Error }

    public sealed class RecordingStatusSnapshot
    {
        public RecordingState state = RecordingState.Idle;
        public string participantId = "";
        public string sessionId = "";
        public string message = "Press F5 to begin recording (after consent).";
        public string outputPath = "";
        public string[] savedFiles = new string[0];
        public double elapsedSeconds;
        public long nativeSamples;
        public long frameSamples;
        public long queueDrops;
        public double measuredNativeHz;

        public RecordingStatusSnapshot Copy() => (RecordingStatusSnapshot)MemberwiseClone();
    }
}
