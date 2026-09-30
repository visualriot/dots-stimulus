using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using FileVersionInfo = System.Diagnostics.FileVersionInfo;
using Process = System.Diagnostics.Process;
#if VIVE_SRANIPAL
using ViveSR.anipal.Eye;
#endif

namespace DotsStimulus.EyeTracking
{
    [Serializable]
    public sealed class SranipalEnvironmentReport
    {
        public string activeOpenXrRuntime = "unknown";
        public bool steamVrIsActiveOpenXrRuntime;
        public bool srRuntimeIsRunning;
        public bool nativeClientFound;
        public string nativeClientVersion = "not found";
        public string installedRuntimeVersion = "not found";
        public string summary = "Not checked";
    }

    /// <summary>Read-only checks which never load SRanipal native code.</summary>
    public static class SranipalEnvironmentValidator
    {
        public const string RequestedSdkVersion = "1.3.2.0";

        public static SranipalEnvironmentReport Evaluate()
        {
            var report = new SranipalEnvironmentReport();
            report.activeOpenXrRuntime = ReadActiveOpenXrRuntime();
            report.steamVrIsActiveOpenXrRuntime = report.activeOpenXrRuntime.IndexOf("steamxr", StringComparison.OrdinalIgnoreCase) >= 0;
            report.srRuntimeIsRunning = IsProcessRunning("sr_runtime");
            report.nativeClientVersion = SranipalNativeEyeProvider.DetectNativeDllVersion();
            report.nativeClientFound = report.nativeClientVersion != "not found" && report.nativeClientVersion != "unreadable";
            report.installedRuntimeVersion = DetectInstalledRuntimeVersion();

            var problems = new List<string>();
            if (!report.steamVrIsActiveOpenXrRuntime)
                problems.Add("SteamVR is not the active OpenXR runtime (active: " + report.activeOpenXrRuntime + ")");
            if (!report.srRuntimeIsRunning)
                problems.Add("SR_Runtime is not running");
            if (!report.nativeClientFound)
                problems.Add("SRanipal.dll was not found in the Unity project/player");
            report.summary = problems.Count == 0
                ? "SteamVR, SR_Runtime, and the SRanipal native client are ready."
                : string.Join("; ", problems);
            return report;
        }

        private static bool IsProcessRunning(string processName)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            IntPtr snapshot = CreateToolhelp32Snapshot(2, 0);
            if (snapshot != new IntPtr(-1))
            {
                try
                {
                    var entry = new ProcessEntry32 { size = (uint)Marshal.SizeOf<ProcessEntry32>() };
                    if (Process32First(snapshot, ref entry))
                    {
                        do
                        {
                            if (string.Equals(Path.GetFileNameWithoutExtension(entry.executableFile), processName,
                                    StringComparison.OrdinalIgnoreCase)) return true;
                        }
                        while (Process32Next(snapshot, ref entry));
                    }
                }
                finally { CloseHandle(snapshot); }
            }
#endif
            try
            {
                Process[] processes = Process.GetProcessesByName(processName);
                bool running = processes.Length > 0;
                foreach (Process process in processes) process.Dispose();
                return running;
            }
            catch { return false; }
        }

        private static string DetectInstalledRuntimeVersion()
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VIVE", "SRanipal", "sr_runtime.exe");
                return File.Exists(path) ? FileVersionInfo.GetVersionInfo(path).FileVersion ?? "unknown" : "not found";
            }
            catch { return "unreadable"; }
        }

        private static string ReadActiveOpenXrRuntime()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            try
            {
                const uint stringValue = 2;
                uint type;
                uint bytes = 0;
                IntPtr localMachine = new IntPtr(unchecked((int)0x80000002));
                int result = RegGetValue(localMachine, @"SOFTWARE\Khronos\OpenXR\1", "ActiveRuntime", stringValue, out type, null, ref bytes);
                if (result != 0 || bytes < 2) return "not configured (registry error " + result + ")";
                var value = new StringBuilder((int)(bytes / 2));
                result = RegGetValue(localMachine, @"SOFTWARE\Khronos\OpenXR\1", "ActiveRuntime", stringValue, out type, value, ref bytes);
                return result == 0 ? value.ToString() : "unreadable (registry error " + result + ")";
            }
            catch (Exception exception) { return "unreadable: " + exception.GetType().Name; }
#else
            return "unsupported platform";
#endif
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProcessEntry32
        {
            public uint size;
            public uint usage;
            public uint processId;
            public IntPtr defaultHeapId;
            public uint moduleId;
            public uint threads;
            public uint parentProcessId;
            public int priorityClassBase;
            public uint flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string executableFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegGetValue(IntPtr key, string subKey, string value, uint flags,
            out uint type, StringBuilder data, ref uint dataSize);
#endif
    }

    /// <summary>
    /// Prevents SRanipal's unmanaged API from being loaded until its external runtime is ready.
    /// A native heap failure cannot be caught by C#, so all checks happen before the vendor framework is enabled.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class SranipalSafeBootstrap : MonoBehaviour
    {
        [Tooltip("Disable to run the VR environment without initializing legacy SRanipal.")]
        public bool enableNativeSranipal = true;
        [Tooltip("Do not initialize SRanipal unless SteamVR is the active system OpenXR runtime.")]
        public bool requireSteamVrOpenXrRuntime = true;
        [Tooltip("Do not initialize SRanipal unless SR_Runtime was started and is visible in the system tray.")]
        public bool requireSrRuntimeAlreadyRunning = true;
#if VIVE_SRANIPAL
        public SRanipal_Eye_Framework framework;
#endif

        public bool NativeStartupAllowed { get; private set; }
        public SranipalEnvironmentReport Report { get; private set; }
        public string Status => Report?.summary ?? "SRanipal preflight has not run";

        private void Awake()
        {
#if VIVE_SRANIPAL
            if (framework) framework.enabled = false;
#endif
            Report = SranipalEnvironmentValidator.Evaluate();
            NativeStartupAllowed = enableNativeSranipal &&
                (!requireSteamVrOpenXrRuntime || Report.steamVrIsActiveOpenXrRuntime) &&
                (!requireSrRuntimeAlreadyRunning || Report.srRuntimeIsRunning) &&
                Report.nativeClientFound;

#if VIVE_SRANIPAL
            if (framework) framework.enabled = NativeStartupAllowed;
            if (!framework)
            {
                NativeStartupAllowed = false;
                Debug.LogError("SRanipal safe bootstrap has no SRanipal_Eye_Framework reference.", this);
            }
#else
            NativeStartupAllowed = false;
#endif
            if (NativeStartupAllowed)
                Debug.Log("SRanipal native startup allowed after preflight. " + VersionSummary(), this);
            else
                Debug.LogWarning("SRanipal native startup BLOCKED to protect Unity from a native crash. " + Status + ". " + VersionSummary(), this);
        }

        public string VersionSummary() => "Requested SDK " + SranipalEnvironmentValidator.RequestedSdkVersion +
            "; project SRanipal.dll " + (Report?.nativeClientVersion ?? "unknown") +
            "; installed SR_Runtime " + (Report?.installedRuntimeVersion ?? "unknown");
    }
}
