// Windows shutdown / execution-state approach adapted from Shutdown Timer Classic.
// https://github.com/lukaslangrock/ShutdownTimerClassic
// Copyright (c) 2026 Lukas Langrock. MIT license: see THIRD-PARTY-NOTICES.txt.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DailyReflection
{
    public interface IShutdownService { void RequestShutdown(); }

    public sealed class WindowsShutdownService : IShutdownService
    {
        // Graceful power-off only; apps may show their normal unsaved-document prompts.
        internal const uint PowerOffFlags = 0x00000008;
        private const uint PlannedApplicationReason = 0x80040000;

        [StructLayout(LayoutKind.Sequential)]
        private struct Luid { public uint LowPart; public int HighPart; }
        [StructLayout(LayoutKind.Sequential)]
        private struct TokenPrivileges { public uint Count; public Luid Luid; public uint Attributes; }

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool LookupPrivilegeValue(string system, string name, out Luid luid);
        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges state, uint length, out TokenPrivileges previous, out uint required);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ExitWindowsEx(uint flags, uint reason);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SetThreadExecutionState(uint state);

        public static void KeepAwake(bool enabled)
        {
            uint result = SetThreadExecutionState(enabled ? 0x80000001U : 0x80000000U);
            if (result == 0 && enabled) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法开始关机倒计时。");
        }

        public void RequestShutdown()
        {
            IntPtr token;
            using (Process process = Process.GetCurrentProcess())
            {
                if (!OpenProcessToken(process.Handle, 0x0020U | 0x0008U, out token)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            TokenPrivileges previous = new TokenPrivileges();
            bool changed = false;
            try
            {
                Luid luid;
                if (!LookupPrivilegeValue(null, "SeShutdownPrivilege", out luid)) throw new Win32Exception(Marshal.GetLastWin32Error());
                TokenPrivileges requested = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 2 };
                uint required;
                bool adjusted = AdjustTokenPrivileges(token, false, ref requested, (uint)Marshal.SizeOf(typeof(TokenPrivileges)), out previous, out required);
                int error = Marshal.GetLastWin32Error();
                if (!adjusted || error != 0) throw new Win32Exception(error);
                changed = true;
                if (!ExitWindowsEx(PowerOffFlags, PlannedApplicationReason)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                if (changed)
                {
                    TokenPrivileges ignored;
                    uint required;
                    AdjustTokenPrivileges(token, false, ref previous, (uint)Marshal.SizeOf(typeof(TokenPrivileges)), out ignored, out required);
                }
                CloseHandle(token);
            }
        }
    }

    public sealed class ShutdownCountdown
    {
        private readonly IShutdownService shutdown;
        private readonly Action<bool> keepAwake;
        private readonly Func<double> clock;
        private double deadline;
        public bool IsArmed { get; private set; }
        public int SecondsRemaining { get { return IsArmed ? Math.Max(0, (int)Math.Ceiling(deadline - clock())) : 0; } }

        public ShutdownCountdown(IShutdownService service, Action<bool> awake, Func<double> monotonicClock)
        { shutdown = service; keepAwake = awake; clock = monotonicClock; }

        public void Begin()
        {
            if (IsArmed) return;
            keepAwake(true);
            deadline = clock() + 10;
            IsArmed = true;
        }

        public void Cancel()
        {
            if (!IsArmed) return;
            IsArmed = false;
            keepAwake(false);
        }

        public bool Tick()
        {
            if (!IsArmed || SecondsRemaining > 0) return false;
            IsArmed = false; // Never repeat the request, including when the API fails.
            keepAwake(false);
            shutdown.RequestShutdown();
            return true;
        }
    }
}
