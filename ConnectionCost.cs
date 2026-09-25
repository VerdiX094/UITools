using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UITools
{
    /// <summary>
    ///     Tells whether the current internet connection is metered
    /// </summary>
    internal static class ConnectionCost
    {
        // System services can hang, so queries run off the main thread
        static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        ///     True when the connection is known to be metered. Unknown states, unsupported platforms and errors
        ///     all count as unmetered.
        /// </summary>
        public static async UniTask<bool> IsMetered()
        {
            try
            {
                // Only ever reported on phones and tablets
                if (Application.internetReachability == NetworkReachability.ReachableViaCarrierDataNetwork)
                    return true;

                Func<bool> query;
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsPlayer:
                    case RuntimePlatform.WindowsEditor:
                        query = IsMeteredWindows;
                        break;
                    case RuntimePlatform.LinuxPlayer:
                    case RuntimePlatform.LinuxEditor:
                        query = IsMeteredLinux;
                        break;
                    default:
                        return false;
                }

                return await UniTask.RunOnThreadPool(query).Timeout(QueryTimeout, DelayType.Realtime);
            }
            catch (Exception e)
            {
                Debug.Log($"[ConnectionCost] Could not tell whether the connection is metered: {e.Message}");
                return false;
            }
        }
        
        static bool IsMeteredWindows()
        {
            object manager = new NetworkListManager();
            try
            {
                ((INetworkCostManager)manager).GetCost(out var cost, IntPtr.Zero);
                return (cost & (NlmConnectionCostFixed | NlmConnectionCostVariable)) != 0;
            }
            finally
            {
                Marshal.ReleaseComObject(manager);
            }
        }
        
        static bool IsMeteredLinux()
        {
            var output = RunCommand("nmcli", "-g METERED general");
            return output != null && output.Trim().StartsWith("yes", StringComparison.OrdinalIgnoreCase);
        }

        // Returns null when the tool is missing, fails or hangs
        static string RunCommand(string fileName, string arguments)
        {
            try
            {
                using Process process = Process.Start(new ProcessStartInfo(fileName, arguments)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                });
                if (process == null) return null;

                var output = process.StandardOutput.ReadToEndAsync();
                if (!process.WaitForExit((int)QueryTimeout.TotalMilliseconds))
                {
                    process.Kill();
                    return null;
                }

                return process.ExitCode == 0 ? output.Result : null;
            }
            catch (Exception e)
            {
                Debug.Log($"[ConnectionCost] Could not run {fileName}, assuming an unmetered connection: {e.Message}");
                return null;
            }
        }

        // From netlistmgr.h
        const uint NlmConnectionCostFixed = 0x2;
        const uint NlmConnectionCostVariable = 0x4;

        [ComImport, Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B")]
        class NetworkListManager
        {
        }

        [ComImport, Guid("DCB00008-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface INetworkCostManager
        {
            void GetCost(out uint cost, IntPtr destinationAddress);
            void GetDataPlanStatus(IntPtr dataPlanStatus, IntPtr destinationAddress);
            void SetDestinationAddresses(uint length, IntPtr destinationAddresses,
                [MarshalAs(UnmanagedType.VariantBool)] bool append);
        }
    }
}
