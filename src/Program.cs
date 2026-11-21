using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Witching Hour Bringer")]
[assembly: AssemblyDescription("Lightweight Pressure 3:00 AM Witching Hour system time macro")]
[assembly: AssemblyProduct("Witching Hour Bringer")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace WitchingHourBringer
{
    internal static class NativeMethods
    {
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_BORDER_COLOR = 34;
        public const int DWMWA_CAPTION_COLOR = 35;
        public const uint WM_TIMECHANGE = 0x001E;
        public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
        private const string SE_SYSTEMTIME_NAME = "SeSystemtimePrivilege";

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEMTIME
        {
            public ushort wYear;
            public ushort wMonth;
            public ushort wDayOfWeek;
            public ushort wDay;
            public ushort wHour;
            public ushort wMinute;
            public ushort wSecond;
            public ushort wMilliseconds;

            public static SYSTEMTIME FromDateTime(DateTime dt)
            {
                SYSTEMTIME st = new SYSTEMTIME();
                st.wYear = (ushort)dt.Year;
                st.wMonth = (ushort)dt.Month;
                st.wDayOfWeek = (ushort)dt.DayOfWeek;
                st.wDay = (ushort)dt.Day;
                st.wHour = (ushort)dt.Hour;
                st.wMinute = (ushort)dt.Minute;
                st.wSecond = (ushort)dt.Second;
                st.wMilliseconds = (ushort)dt.Millisecond;
                return st;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID_AND_ATTRIBUTES
        {
            public LUID Luid;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public LUID_AND_ATTRIBUTES Privileges;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetLocalTime(ref SYSTEMTIME lpSystemTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetSystemTime(ref SYSTEMTIME lpSystemTime);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(
            IntPtr TokenHandle,
            bool DisableAllPrivileges,
            ref TOKEN_PRIVILEGES NewState,
            uint BufferLength,
            IntPtr PreviousState,
            IntPtr ReturnLength);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SendNotifyMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        public static bool EnableSystemTimePrivilege()
        {
            IntPtr tokenHandle = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out tokenHandle))
                {
                    return false;
                }

                LUID luid;
                if (!LookupPrivilegeValue(null, SE_SYSTEMTIME_NAME, out luid))
                {
                    return false;
                }

                TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES();
                tp.PrivilegeCount = 1;
                tp.Privileges.Luid = luid;
                tp.Privileges.Attributes = SE_PRIVILEGE_ENABLED;

                if (!AdjustTokenPrivileges(tokenHandle, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                {
                    return false;
                }

                return Marshal.GetLastWin32Error() == 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (tokenHandle != IntPtr.Zero)
                {
                    CloseHandle(tokenHandle);
                }
            }
        }

        public static void ApplyDarkTitleBar(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            try
            {
                int useDark = 1;
                int hr = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
                if (hr != 0)
                {
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDark, sizeof(int));
                }

                // Windows 11 custom caption & border tint (0x00BBGGRR) -> #0D0814 caption, #4C2875 border
                int captionColor = 0x0014080D;
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));

                int borderColor = 0x0075284C;
                DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
            }
            catch
            {
                // Older Windows versions ignore these attributes cleanly
            }
        }

        public static void NotifyTimeChanged()
        {
            try
            {
                SendNotifyMessage(HWND_BROADCAST, WM_TIMECHANGE, IntPtr.Zero, IntPtr.Zero);
            }
            catch
            {
                // Best effort
            }
        }
    }

    internal enum MacroPhase
    {
        Idle,
        CountingToWitchingHour,
        WitchingHourActive,
        Restoring,
        Completed
    }

    internal struct TickEvaluation
    {
        public MacroPhase Phase;
        public int RemainingSeconds;
        public float Progress;
        public string PrimaryText;
        public string SecondaryText;
        public bool ShouldRestore;
    }

    internal static class TimerLogic
    {
        public static DateTime PickValidTargetDate(DateTime currentLocal)
        {
            DateTime date = currentLocal.Date;
            DateTime candidate = new DateTime(date.Year, date.Month, date.Day, 2, 59, 0, DateTimeKind.Local);
            if (TimeZoneInfo.Local.IsInvalidTime(candidate))
            {
                date = date.AddDays(1);
            }
            return date;
        }

        public static string FormatMinutesSeconds(int totalSeconds)
        {
            if (totalSeconds < 0)
            {
                totalSeconds = 0;
            }
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            return string.Format("{0}:{1:D2}", minutes, seconds);
        }

        public static TickEvaluation EvaluateTick(DateTime nowLocal, DateTime witchingStartLocal, DateTime witchingEndLocal, double countdownTotalSeconds)
        {
            TickEvaluation eval = new TickEvaluation();

            // Safeguard: if an external process unexpectedly moved the clock far before 2:59 AM
            if (nowLocal < witchingStartLocal.AddMinutes(-5))
            {
                eval.Phase = MacroPhase.Restoring;
                eval.ShouldRestore = true;
                eval.Progress = 1.0f;
                eval.PrimaryText = "Clock changed externally";
                eval.SecondaryText = "Restoring Windows automatic time...";
                return eval;
            }

            if (nowLocal < witchingStartLocal)
            {
                double remainingSec = (witchingStartLocal - nowLocal).TotalSeconds;
                int displaySec = (int)Math.Ceiling(remainingSec);
                if (displaySec < 1)
                {
                    displaySec = 1;
                }

                double denom = countdownTotalSeconds > 0.5 ? countdownTotalSeconds : 5.0;
                double progress = (denom - remainingSec) / denom;
                if (progress < 0.0) progress = 0.0;
                if (progress > 1.0) progress = 1.0;

                eval.Phase = MacroPhase.CountingToWitchingHour;
                eval.RemainingSeconds = displaySec;
                eval.Progress = (float)progress;
                eval.PrimaryText = string.Format("{0} until witching hour comes", FormatMinutesSeconds(displaySec));
                eval.SecondaryText = "Clock set to 2:59:55 AM - waiting for 3:00 AM";
                eval.ShouldRestore = false;
                return eval;
            }

            if (nowLocal < witchingEndLocal)
            {
                double remainingSec = (witchingEndLocal - nowLocal).TotalSeconds;
                int displaySec = (int)Math.Ceiling(remainingSec);
                if (displaySec < 1)
                {
                    displaySec = 1;
                }

                double progress = (60.0 - remainingSec) / 60.0;
                if (progress < 0.0) progress = 0.0;
                if (progress > 1.0) progress = 1.0;

                eval.Phase = MacroPhase.WitchingHourActive;
                eval.RemainingSeconds = displaySec;
                eval.Progress = (float)progress;
                eval.PrimaryText = "3:00 AM - Witching Hour is here!";
                eval.SecondaryText = string.Format("Waiting until 3:01 AM - restoring automatic time in {0}", FormatMinutesSeconds(displaySec));
                eval.ShouldRestore = false;
                return eval;
            }

            eval.Phase = MacroPhase.Restoring;
            eval.RemainingSeconds = 0;
            eval.Progress = 1.0f;
            eval.PrimaryText = "3:01 AM reached!";
            eval.SecondaryText = "Restoring Windows automatic time...";
            eval.ShouldRestore = true;
            return eval;
        }
    }

    internal sealed class RestoreResult
    {
        public bool Success;
        public bool SyncedWithNtp;
        public string Message;
    }

    internal sealed class SystemTimeController
    {
        private readonly object _syncLock = new object();
        private bool _isClockModified;
        private DateTime _originalUtc;
        private Stopwatch _elapsed;
        private string _previousW32TimeType = "NTP";

        public DateTime WitchingStartLocal { get; private set; }
        public DateTime WitchingEndLocal { get; private set; }
        public double CountdownTotalSeconds { get; private set; }

        public bool IsClockModified
        {
            get
            {
                lock (_syncLock)
                {
                    return _isClockModified;
                }
            }
        }

        public static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    if (identity == null)
                    {
                        return false;
                    }
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        public bool SetToTwoFiftyNineAm(out string errorMessage)
        {
            errorMessage = null;

            if (!IsAdministrator())
            {
                errorMessage = "Administrator permission is required to change Windows system time.";
                return false;
            }

            NativeMethods.EnableSystemTimePrivilege();

            DateTime currentLocal = DateTime.Now;
            DateTime targetDate = TimerLogic.PickValidTargetDate(currentLocal);

            lock (_syncLock)
            {
                if (!_isClockModified)
                {
                    _originalUtc = DateTime.UtcNow;
                    _elapsed = Stopwatch.StartNew();
                }
            }

            // Temporarily pause Windows Automatic Time sync (NoSync + stop w32time)
            // so Windows cannot prematurely snap the clock back before 3:01 AM
            PauseAutomaticTimeSync();

            DateTime target259 = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, 2, 59, 55, DateTimeKind.Local);
            NativeMethods.SYSTEMTIME st = NativeMethods.SYSTEMTIME.FromDateTime(target259);

            bool ok = NativeMethods.SetLocalTime(ref st);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                errorMessage = string.Format("Windows refused to set system time (Win32 error {0}).", err);
                lock (_syncLock)
                {
                    _isClockModified = false;
                }
                EnsureAutomaticTimeEnabled();
                return false;
            }

            // Per MSDN: on DST boundary days, calling SetLocalTime twice ensures DST bias is updated accurately.
            DateTime verifyNow = DateTime.Now;
            if (verifyNow.Hour != 2 || verifyNow.Minute != 59)
            {
                NativeMethods.SetLocalTime(ref st);
                verifyNow = DateTime.Now;
            }

            // Anchor 3:00 AM and 3:01 AM directly to the live system clock (natural countdown, no forcing)
            DateTime actualDate = verifyNow.Date;
            WitchingStartLocal = new DateTime(actualDate.Year, actualDate.Month, actualDate.Day, 3, 0, 0, DateTimeKind.Local);
            WitchingEndLocal = new DateTime(actualDate.Year, actualDate.Month, actualDate.Day, 3, 1, 0, DateTimeKind.Local);
            double initialRemaining = (WitchingStartLocal - verifyNow).TotalSeconds;
            CountdownTotalSeconds = initialRemaining > 0.5 ? initialRemaining : 5.0;

            lock (_syncLock)
            {
                _isClockModified = true;
            }

            return true;
        }

        public Task<RestoreResult> RestoreRealTimeAsync()
        {
            return Task.Run(new Func<RestoreResult>(RestoreRealTimeCore));
        }

        public void RestoreRealTimeSyncOnExit()
        {
            if (!IsClockModified)
            {
                return;
            }

            try
            {
                ApplyEstimatedRealTime();
                bool configChanged = EnsureAutomaticTimeEnabled();
                StartW32TimeService(2000);
                if (configChanged)
                {
                    RunHiddenProcess("w32tm.exe", "/config /update", 2500);
                }
                RunHiddenProcess("w32tm.exe", "/resync /force", 3500);
            }
            catch
            {
                // Best effort on shutdown
            }
        }

        private RestoreResult RestoreRealTimeCore()
        {
            RestoreResult result = new RestoreResult();

            try
            {
                NativeMethods.EnableSystemTimePrivilege();

                // Step 1: Instantly snap the system clock back to accurate elapsed real time
                bool immediateOk = ApplyEstimatedRealTime();

                // Step 2: Ensure Windows "Set time automatically" registry & service settings are ON
                bool configChanged = EnsureAutomaticTimeEnabled();

                // Step 3: Start W32Time service and trigger Windows Time resync
                bool startedNow = false;
                bool serviceRunning = StartW32TimeService(5000, out startedNow);

                bool w32tmSynced = false;
                if (serviceRunning)
                {
                    if (startedNow)
                    {
                        // Give W32Time NTP client provider a moment to initialize after cold start
                        Thread.Sleep(700);
                    }

                    if (configChanged)
                    {
                        RunHiddenProcess("w32tm.exe", "/config /update", 6000);
                    }

                    int code = RunHiddenProcess("w32tm.exe", "/resync /force", 10000);
                    if (code == 0)
                    {
                        w32tmSynced = true;
                    }
                    else
                    {
                        // Retry once after brief delay (common right after W32Time starts)
                        Thread.Sleep(1200);
                        code = RunHiddenProcess("w32tm.exe", "/resync /force", 10000);
                        if (code == 0)
                        {
                            w32tmSynced = true;
                        }
                    }
                }

                // Step 4: Fallback lightweight direct SNTP sync if w32tm did not resync
                bool sntpSynced = false;
                if (!w32tmSynced)
                {
                    DateTime ntpUtc;
                    if (TryQueryNtpUtc("time.windows.com", out ntpUtc) || TryQueryNtpUtc("pool.ntp.org", out ntpUtc))
                    {
                        NativeMethods.SYSTEMTIME utcSt = NativeMethods.SYSTEMTIME.FromDateTime(ntpUtc);
                        if (NativeMethods.SetSystemTime(ref utcSt))
                        {
                            NativeMethods.NotifyTimeChanged();
                            sntpSynced = true;
                        }
                    }
                }

                result.Success = immediateOk || w32tmSynced || sntpSynced;
                result.SyncedWithNtp = w32tmSynced || sntpSynced;

                if (w32tmSynced)
                {
                    result.Message = "Real time restored (Windows Automatic Time synced)";
                }
                else if (sntpSynced)
                {
                    result.Message = "Real time restored & NTP synced (Automatic Time ON)";
                }
                else if (immediateOk)
                {
                    result.Message = "Real time restored (Automatic Time ON)";
                }
                else
                {
                    result.Message = "Could not verify time sync - please check Windows Time settings";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = "Restore error: " + ex.Message;
            }

            return result;
        }

        private bool ApplyEstimatedRealTime()
        {
            DateTime estimatedUtc;
            lock (_syncLock)
            {
                if (!_isClockModified || _elapsed == null)
                {
                    return true;
                }
                estimatedUtc = _originalUtc + _elapsed.Elapsed;
            }

            NativeMethods.SYSTEMTIME utcSt = NativeMethods.SYSTEMTIME.FromDateTime(estimatedUtc.ToUniversalTime());
            bool ok = NativeMethods.SetSystemTime(ref utcSt);
            if (ok)
            {
                lock (_syncLock)
                {
                    _isClockModified = false;
                }
                NativeMethods.NotifyTimeChanged();
            }
            return ok;
        }

        private void PauseAutomaticTimeSync()
        {
            try
            {
                using (RegistryKey paramKey = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\W32Time\Parameters", true))
                {
                    if (paramKey != null)
                    {
                        string currentType = paramKey.GetValue("Type") as string;
                        if (!string.IsNullOrEmpty(currentType) &&
                            !string.Equals(currentType, "NoSync", StringComparison.OrdinalIgnoreCase))
                        {
                            _previousW32TimeType = currentType;
                        }
                        paramKey.SetValue("Type", "NoSync", RegistryValueKind.String);
                    }
                }
            }
            catch
            {
                // Non-fatal: continue stopping service
            }

            StopW32TimeIfRunning();
        }

        private static void StopW32TimeIfRunning()
        {
            try
            {
                using (ServiceController sc = new ServiceController("w32time"))
                {
                    if (sc.Status == ServiceControllerStatus.Running || sc.Status == ServiceControllerStatus.StartPending)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(4));
                    }
                }
            }
            catch
            {
                // Non-fatal: continue setting time
            }
        }

        private bool EnsureAutomaticTimeEnabled()
        {
            bool changed = false;
            try
            {
                string restoreType = string.IsNullOrEmpty(_previousW32TimeType) ||
                    string.Equals(_previousW32TimeType, "NoSync", StringComparison.OrdinalIgnoreCase)
                    ? "NTP"
                    : _previousW32TimeType;

                using (RegistryKey paramKey = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\W32Time\Parameters", true))
                {
                    if (paramKey != null)
                    {
                        string currentType = paramKey.GetValue("Type") as string;
                        if (string.IsNullOrEmpty(currentType) ||
                            string.Equals(currentType, "NoSync", StringComparison.OrdinalIgnoreCase))
                        {
                            paramKey.SetValue("Type", restoreType, RegistryValueKind.String);
                            changed = true;
                        }

                        string ntpServer = paramKey.GetValue("NtpServer") as string;
                        if (string.IsNullOrEmpty(ntpServer))
                        {
                            paramKey.SetValue("NtpServer", "time.windows.com,0x9", RegistryValueKind.String);
                            changed = true;
                        }
                    }
                }

                // If W32Time service was explicitly Disabled (4), restore standard Manual/Trigger-Start (3)
                using (RegistryKey svcKey = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\W32Time", false))
                {
                    if (svcKey != null)
                    {
                        object startVal = svcKey.GetValue("Start");
                        if (startVal is int && ((int)startVal) == 4)
                        {
                            RunHiddenProcess("sc.exe", "config w32time start= demand", 5000);
                            changed = true;
                        }
                    }
                }
            }
            catch
            {
                // Ignore registry errors; continue with service start
            }
            return changed;
        }

        private static bool StartW32TimeService(int timeoutMs)
        {
            bool startedNow;
            return StartW32TimeService(timeoutMs, out startedNow);
        }

        private static bool StartW32TimeService(int timeoutMs, out bool startedNow)
        {
            startedNow = false;
            try
            {
                using (ServiceController sc = new ServiceController("w32time"))
                {
                    if (sc.Status == ServiceControllerStatus.Running)
                    {
                        return true;
                    }
                    if (sc.Status == ServiceControllerStatus.StopPending)
                    {
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromMilliseconds(timeoutMs));
                    }
                    if (sc.Status == ServiceControllerStatus.Stopped)
                    {
                        sc.Start();
                        startedNow = true;
                    }
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromMilliseconds(timeoutMs));
                    return sc.Status == ServiceControllerStatus.Running;
                }
            }
            catch
            {
                return false;
            }
        }

        private static int RunHiddenProcess(string fileName, string arguments, int timeoutMs)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = fileName;
                psi.Arguments = arguments;
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.WindowStyle = ProcessWindowStyle.Hidden;

                using (Process proc = Process.Start(psi))
                {
                    if (proc == null)
                    {
                        return -1;
                    }
                    if (!proc.WaitForExit(timeoutMs))
                    {
                        try { proc.Kill(); } catch { }
                        return -2;
                    }
                    return proc.ExitCode;
                }
            }
            catch
            {
                return -1;
            }
        }

        private static bool TryQueryNtpUtc(string ntpHost, out DateTime utcTime)
        {
            utcTime = DateTime.MinValue;
            try
            {
                byte[] ntpData = new byte[48];
                ntpData[0] = 0x1B; // LI=0, VN=3, Mode=3 (Client)

                IPAddress[] addresses = Dns.GetHostEntry(ntpHost).AddressList;
                IPAddress targetIp = null;
                for (int i = 0; i < addresses.Length; i++)
                {
                    if (addresses[i].AddressFamily == AddressFamily.InterNetwork)
                    {
                        targetIp = addresses[i];
                        break;
                    }
                }
                if (targetIp == null && addresses.Length > 0)
                {
                    targetIp = addresses[0];
                }
                if (targetIp == null)
                {
                    return false;
                }

                IPEndPoint endPoint = new IPEndPoint(targetIp, 123);
                using (Socket socket = new Socket(targetIp.AddressFamily, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.SendTimeout = 3000;
                    socket.ReceiveTimeout = 3000;
                    socket.Connect(endPoint);
                    socket.Send(ntpData);
                    int received = socket.Receive(ntpData);
                    if (received < 48)
                    {
                        return false;
                    }
                }

                ulong intPart =
                    ((ulong)ntpData[40] << 24) |
                    ((ulong)ntpData[41] << 16) |
                    ((ulong)ntpData[42] << 8) |
                    (ulong)ntpData[43];

                ulong fractPart =
                    ((ulong)ntpData[44] << 24) |
                    ((ulong)ntpData[45] << 16) |
                    ((ulong)ntpData[46] << 8) |
                    (ulong)ntpData[47];

                if (intPart == 0)
                {
                    return false;
                }

                ulong milliseconds = (intPart * 1000UL) + ((fractPart * 1000UL) / 0x100000000UL);
                DateTime networkDateTime = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    .AddMilliseconds((double)milliseconds);

                if (networkDateTime.Year < 2020 || networkDateTime.Year > 2100)
                {
                    return false;
                }

                utcTime = networkDateTime;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    internal static class Theme
    {
        public static readonly Color WindowBgTop = Color.FromArgb(18, 10, 30);
        public static readonly Color WindowBgBottom = Color.FromArgb(10, 6, 17);
        public static readonly Color CardBg = Color.FromArgb(23, 13, 37);
        public static readonly Color CardBgDeep = Color.FromArgb(18, 10, 29);
        public static readonly Color CardBorder = Color.FromArgb(59, 34, 92);
        public static readonly Color CardBorderActive = Color.FromArgb(139, 61, 255);
        public static readonly Color CardBorderWitching = Color.FromArgb(228, 66, 204);

        public static readonly Color AccentPurple = Color.FromArgb(132, 54, 245);
        public static readonly Color AccentPurpleLight = Color.FromArgb(174, 82, 255);
        public static readonly Color AccentMagenta = Color.FromArgb(232, 68, 208);
        public static readonly Color AccentMagentaBright = Color.FromArgb(255, 118, 232);

        public static readonly Color TextPrimary = Color.FromArgb(245, 236, 255);
        public static readonly Color TextSecondary = Color.FromArgb(169, 139, 201);
        public static readonly Color TextMuted = Color.FromArgb(124, 96, 156);
        public static readonly Color TextSuccess = Color.FromArgb(170, 240, 210);
        public static readonly Color TextError = Color.FromArgb(255, 130, 165);

        public static GraphicsPath CreateRoundedRect(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2f;
            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;
            if (d <= 0f)
            {
                path.AddRectangle(rect);
                return path;
            }

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class PurpleActionButton : Control
    {
        private bool _hovered;
        private bool _pressed;
        private bool _focused;
        private bool _secondaryStyle;
        private float _dpiScale = 1f;

        public PurpleActionButton()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable |
                ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public float DpiScale
        {
            get { return _dpiScale; }
            set { _dpiScale = value; Invalidate(); }
        }

        public bool SecondaryStyle
        {
            get { return _secondaryStyle; }
            set
            {
                if (_secondaryStyle != value)
                {
                    _secondaryStyle = value;
                    Invalidate();
                }
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && Enabled)
            {
                _pressed = true;
                Focus();
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_pressed)
            {
                _pressed = false;
                Invalidate();
            }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            _focused = true;
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            _focused = false;
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if ((e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) && Enabled)
            {
                e.Handled = true;
                OnClick(EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            float radius = 10f * _dpiScale;
            RectangleF rect = new RectangleF(1f, 1f, Width - 2.5f, Height - 2.5f);

            using (GraphicsPath path = Theme.CreateRoundedRect(rect, radius))
            {
                if (!Enabled)
                {
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(35, 22, 54)))
                    {
                        g.FillPath(bg, path);
                    }
                    using (Pen pen = new Pen(Color.FromArgb(66, 42, 98), 1f * _dpiScale))
                    {
                        g.DrawPath(pen, path);
                    }
                }
                else if (_secondaryStyle)
                {
                    Color fillTop = _pressed
                        ? Color.FromArgb(48, 22, 66)
                        : (_hovered ? Color.FromArgb(58, 26, 78) : Color.FromArgb(36, 18, 54));
                    Color fillBot = _pressed
                        ? Color.FromArgb(36, 16, 52)
                        : (_hovered ? Color.FromArgb(44, 19, 64) : Color.FromArgb(26, 13, 42));

                    using (LinearGradientBrush bg = new LinearGradientBrush(rect, fillTop, fillBot, LinearGradientMode.Vertical))
                    {
                        g.FillPath(bg, path);
                    }
                    Color borderCol = _hovered ? Theme.AccentMagenta : Color.FromArgb(118, 60, 166);
                    using (Pen pen = new Pen(borderCol, 1.3f * _dpiScale))
                    {
                        g.DrawPath(pen, path);
                    }
                }
                else
                {
                    Color c1, c2;
                    if (_pressed)
                    {
                        c1 = Color.FromArgb(100, 34, 212);
                        c2 = Color.FromArgb(136, 42, 224);
                    }
                    else if (_hovered)
                    {
                        c1 = Color.FromArgb(146, 66, 255);
                        c2 = Color.FromArgb(202, 74, 245);
                    }
                    else
                    {
                        c1 = Color.FromArgb(124, 46, 240);
                        c2 = Color.FromArgb(174, 58, 232);
                    }

                    using (LinearGradientBrush bg = new LinearGradientBrush(rect, c1, c2, LinearGradientMode.Horizontal))
                    {
                        g.FillPath(bg, path);
                    }

                    // Subtle glossy inner highlight
                    RectangleF innerRect = new RectangleF(rect.X + 1f, rect.Y + 1f, rect.Width - 2f, rect.Height - 2f);
                    using (GraphicsPath innerPath = Theme.CreateRoundedRect(innerRect, Math.Max(2f, radius - 1f)))
                    using (Pen hiPen = new Pen(Color.FromArgb(46, 255, 235, 255), 1f * _dpiScale))
                    {
                        g.DrawPath(hiPen, innerPath);
                    }

                    Color outerBorder = (_focused && ShowFocusCues)
                        ? Color.FromArgb(228, 200, 255)
                        : Color.FromArgb(162, 88, 244);
                    using (Pen pen = new Pen(outerBorder, 1.1f * _dpiScale))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }

            Color textCol = !Enabled
                ? Theme.TextMuted
                : (_secondaryStyle ? Color.FromArgb(238, 212, 255) : Color.White);

            using (SolidBrush textBrush = new SolidBrush(textCol))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                RectangleF textRect = new RectangleF(0, _pressed ? 1f : 0f, Width, Height);
                g.DrawString(Text, Font, textBrush, textRect, sf);
            }
        }
    }

    internal sealed class DarkCheckToggle : Control
    {
        private bool _checked;
        private bool _hovered;
        private float _dpiScale = 1f;

        public event EventHandler CheckedChanged;

        public DarkCheckToggle()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        public float DpiScale
        {
            get { return _dpiScale; }
            set { _dpiScale = value; Invalidate(); }
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked != value)
                {
                    _checked = value;
                    Invalidate();
                    EventHandler handler = CheckedChanged;
                    if (handler != null)
                    {
                        handler(this, EventArgs.Empty);
                    }
                }
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Checked = !Checked;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            float boxSize = 14f * _dpiScale;
            float boxY = (Height - boxSize) / 2f;
            RectangleF boxRect = new RectangleF(1f, boxY, boxSize, boxSize);

            using (GraphicsPath boxPath = Theme.CreateRoundedRect(boxRect, 3.5f * _dpiScale))
            {
                if (_checked)
                {
                    using (SolidBrush fill = new SolidBrush(Theme.AccentPurple))
                    {
                        g.FillPath(fill, boxPath);
                    }
                    using (Pen border = new Pen(Theme.AccentPurpleLight, 1f * _dpiScale))
                    {
                        g.DrawPath(border, boxPath);
                    }
                    using (Pen checkPen = new Pen(Color.White, 1.8f * _dpiScale))
                    {
                        checkPen.StartCap = LineCap.Round;
                        checkPen.EndCap = LineCap.Round;
                        checkPen.LineJoin = LineJoin.Round;
                        g.DrawLines(checkPen, new PointF[]
                        {
                            new PointF(boxRect.X + boxSize * 0.22f, boxRect.Y + boxSize * 0.52f),
                            new PointF(boxRect.X + boxSize * 0.43f, boxRect.Y + boxSize * 0.74f),
                            new PointF(boxRect.X + boxSize * 0.78f, boxRect.Y + boxSize * 0.28f)
                        });
                    }
                }
                else
                {
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(24, 14, 38)))
                    {
                        g.FillPath(fill, boxPath);
                    }
                    Color borderCol = _hovered ? Theme.AccentPurpleLight : Color.FromArgb(88, 54, 130);
                    using (Pen border = new Pen(borderCol, 1.1f * _dpiScale))
                    {
                        g.DrawPath(border, boxPath);
                    }
                }
            }

            Color labelCol = _hovered || _checked ? Theme.TextPrimary : Theme.TextSecondary;
            using (SolidBrush textBrush = new SolidBrush(labelCol))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Near;
                sf.LineAlignment = StringAlignment.Center;
                float textX = boxRect.Right + 7f * _dpiScale;
                g.DrawString(Text, Font, textBrush, new RectangleF(textX, 0, Width - textX, Height), sf);
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly SystemTimeController _timeController = new SystemTimeController();
        private readonly System.Windows.Forms.Timer _uiTimer;
        private readonly PurpleActionButton _actionButton;
        private readonly DarkCheckToggle _pinToggle;

        private Image _entityImage;
        private Icon _appIcon;

        private float _dpiScale = 1f;
        private MacroPhase _phase = MacroPhase.Idle;
        private string _statusHeadline = "Ready for the Witching Hour";
        private string _statusDetail = "Sets system time to 2:59:55 AM, waits until 3:01 AM,\nthen restores Windows Automatic Time.";
        private bool _statusIsError;
        private float _progress;
        private float _pulseStep;
        private bool _isRestoring;
#if PREVIEW_MODE
        private DateTime? _previewClockOverride;
#endif

        private Font _titleFont;
        private Font _subtitleFont;
        private Font _clockLabelFont;
        private Font _clockValueFont;
        private Font _countdownFont;
        private Font _detailFont;
        private Font _buttonFont;
        private Font _smallFont;

        public MainForm()
        {
            Text = "Witching Hour Bringer";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            BackColor = Theme.WindowBgBottom;
            ForeColor = Theme.TextPrimary;

            using (Graphics g = CreateGraphics())
            {
                _dpiScale = g.DpiX / 96f;
                if (_dpiScale < 1f) _dpiScale = 1f;
            }

            LoadEmbeddedAssets();
            CreateFonts();

            ClientSize = new Size(S(616), S(338));

            _actionButton = new PurpleActionButton();
            _actionButton.DpiScale = _dpiScale;
            _actionButton.Font = _buttonFont;
            _actionButton.Text = "Bring me witching hour";
            _actionButton.Bounds = new Rectangle(S(244), S(244), S(354), S(44));
            _actionButton.Click += OnActionButtonClick;
            Controls.Add(_actionButton);

            _pinToggle = new DarkCheckToggle();
            _pinToggle.DpiScale = _dpiScale;
            _pinToggle.Font = _smallFont;
            _pinToggle.Text = "Always on top";
            _pinToggle.Bounds = new Rectangle(S(244), S(298), S(130), S(24));
            _pinToggle.CheckedChanged += OnPinToggleChanged;
            Controls.Add(_pinToggle);

            _uiTimer = new System.Windows.Forms.Timer();
            _uiTimer.Interval = 100;
            _uiTimer.Tick += OnUiTimerTick;
            _uiTimer.Start();

            FormClosing += OnMainFormClosing;
        }

        private int S(float logicalPx)
        {
            return (int)Math.Round(logicalPx * _dpiScale);
        }

        private float Sf(float logicalPx)
        {
            return logicalPx * _dpiScale;
        }

        private void CreateFonts()
        {
            _titleFont = new Font("Segoe UI", 16.5f, FontStyle.Bold, GraphicsUnit.Point);
            _subtitleFont = new Font("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
            _clockLabelFont = new Font("Segoe UI", 7.8f, FontStyle.Bold, GraphicsUnit.Point);
            _clockValueFont = new Font("Consolas", 13.5f, FontStyle.Bold, GraphicsUnit.Point);
            _countdownFont = new Font("Segoe UI", 12.5f, FontStyle.Bold, GraphicsUnit.Point);
            _detailFont = new Font("Segoe UI", 8.8f, FontStyle.Regular, GraphicsUnit.Point);
            _buttonFont = new Font("Segoe UI", 10.5f, FontStyle.Bold, GraphicsUnit.Point);
            _smallFont = new Font("Segoe UI", 8.3f, FontStyle.Regular, GraphicsUnit.Point);
        }

        private void LoadEmbeddedAssets()
        {
            Assembly asm = Assembly.GetExecutingAssembly();

            try
            {
                using (Stream icoStream = asm.GetManifestResourceStream("WitchingHour.ico"))
                {
                    if (icoStream != null)
                    {
                        _appIcon = new Icon(icoStream);
                        Icon = _appIcon;
                    }
                }
            }
            catch
            {
                // Fallback gracefully if icon stream unavailable
            }

            try
            {
                using (Stream pngStream = asm.GetManifestResourceStream("WitchingHour.png"))
                {
                    if (pngStream != null)
                    {
                        // Clone into independent Bitmap so underlying stream can close cleanly
                        using (Image temp = Image.FromStream(pngStream))
                        {
                            _entityImage = new Bitmap(temp);
                        }
                    }
                }
            }
            catch
            {
                // Fallback gracefully if PNG unavailable
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.ApplyDarkTitleBar(Handle);
        }

        private void OnPinToggleChanged(object sender, EventArgs e)
        {
            TopMost = _pinToggle.Checked;
        }

        private void OnActionButtonClick(object sender, EventArgs e)
        {
            if (_isRestoring)
            {
                return;
            }

            if (_phase == MacroPhase.CountingToWitchingHour || _phase == MacroPhase.WitchingHourActive)
            {
                BeginRestoreAsync(true);
                return;
            }

            string errorMessage;
            bool started = _timeController.SetToTwoFiftyNineAm(out errorMessage);
            if (!started)
            {
                _statusIsError = true;
                _statusHeadline = "Could not set system time";
                _statusDetail = errorMessage ?? "Please run Witching Hour Bringer as Administrator.";
                Invalidate();
                return;
            }

            _statusIsError = false;
            _phase = MacroPhase.CountingToWitchingHour;
            _actionButton.Text = "Cancel & Restore Real Time";
            _actionButton.SecondaryStyle = true;

            EvaluateAndRefreshState();
        }

        private void OnUiTimerTick(object sender, EventArgs e)
        {
            if (_phase == MacroPhase.WitchingHourActive || _phase == MacroPhase.CountingToWitchingHour)
            {
                _pulseStep += 0.14f;
                EvaluateAndRefreshState();
            }
            else if (_phase == MacroPhase.Restoring)
            {
                _pulseStep += 0.16f;
                Invalidate();
            }
            else
            {
                // Keep live system clock updated every second smoothly
                Invalidate(new Rectangle(S(244), S(76), S(354), S(48)));
            }
        }

        private void EvaluateAndRefreshState()
        {
            if (_isRestoring)
            {
                return;
            }

            TickEvaluation eval = TimerLogic.EvaluateTick(
                DateTime.Now,
                _timeController.WitchingStartLocal,
                _timeController.WitchingEndLocal,
                _timeController.CountdownTotalSeconds);

            _phase = eval.Phase;
            _progress = eval.Progress;
            _statusHeadline = eval.PrimaryText;
            _statusDetail = eval.SecondaryText;

            Invalidate();

            if (eval.ShouldRestore)
            {
                BeginRestoreAsync(false);
            }
        }

        private async void BeginRestoreAsync(bool cancelledByUser)
        {
            if (_isRestoring)
            {
                return;
            }
            _isRestoring = true;
            _phase = MacroPhase.Restoring;
            _statusIsError = false;
            _progress = 1.0f;
            _statusHeadline = cancelledByUser ? "Cancelling..." : "3:01 AM reached!";
            _statusDetail = "Restoring your real time & Windows Automatic Time...";
            _actionButton.Enabled = false;
            _actionButton.Text = "Restoring real time...";
            _actionButton.SecondaryStyle = true;
            Invalidate();

            RestoreResult result = await _timeController.RestoreRealTimeAsync();

            if (IsDisposed)
            {
                return;
            }

            _isRestoring = false;
            _phase = MacroPhase.Completed;
            _progress = 0f;
            _statusIsError = !result.Success;
            _statusHeadline = result.Success
                ? (cancelledByUser ? "Cancelled - Time Restored" : "Witching Hour Complete!")
                : "Time Restore Notice";
            _statusDetail = result.Message;

            _actionButton.Enabled = true;
            _actionButton.SecondaryStyle = false;
            _actionButton.Text = "Bring me witching hour";
            Invalidate();
        }

        private void OnMainFormClosing(object sender, FormClosingEventArgs e)
        {
            _uiTimer.Stop();
            if (_timeController.IsClockModified)
            {
                _timeController.RestoreRealTimeSyncOnExit();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // 1. Background vertical deep-purple gradient
            Rectangle fullRect = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
            using (LinearGradientBrush bgBrush = new LinearGradientBrush(
                fullRect, Theme.WindowBgTop, Theme.WindowBgBottom, LinearGradientMode.Vertical))
            {
                g.FillRectangle(bgBrush, fullRect);
            }

            // Subtle ambient violet glow top-left behind the entity panel
            DrawAmbientBackgroundGlow(g);

            // 2. Left side: Witching Hour Entity Showcase Card
            DrawEntityPanel(g);

            // 3. Right side: Header
            DrawHeader(g);

            // 4. Right side: Live System Time Card
            DrawSystemClockCard(g);

            // 5. Right side: Countdown / Status Card
            DrawStatusCard(g);

            // 6. Right side footer status text (aligned with Always on top toggle)
            DrawFooterStatus(g);
        }

        private void DrawAmbientBackgroundGlow(Graphics g)
        {
            RectangleF glowRect = new RectangleF(Sf(-40), Sf(-30), Sf(340), Sf(340));
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddEllipse(glowRect);
                using (PathGradientBrush pgb = new PathGradientBrush(gp))
                {
                    int alpha = _phase == MacroPhase.WitchingHourActive
                        ? (int)(40 + 14 * Math.Sin(_pulseStep))
                        : 22;
                    Color core = _phase == MacroPhase.WitchingHourActive
                        ? Color.FromArgb(alpha, 224, 56, 204)
                        : Color.FromArgb(alpha, 134, 48, 240);
                    pgb.CenterColor = core;
                    pgb.SurroundColors = new Color[] { Color.FromArgb(0, 14, 8, 24) };
                    g.FillEllipse(pgb, glowRect);
                }
            }
        }

        private void DrawEntityPanel(Graphics g)
        {
            RectangleF panelRect = new RectangleF(Sf(16), Sf(16), Sf(210), Sf(306));
            float radius = Sf(14);

            using (GraphicsPath path = Theme.CreateRoundedRect(panelRect, radius))
            {
                using (LinearGradientBrush cardFill = new LinearGradientBrush(
                    panelRect,
                    Color.FromArgb(24, 13, 39),
                    Color.FromArgb(14, 8, 24),
                    LinearGradientMode.Vertical))
                {
                    g.FillPath(cardFill, path);
                }

                // Clip inside rounded card so aura & entity stay crisp inside the frame
                GraphicsState state = g.Save();
                g.SetClip(path);

                // Radial violet/magenta aura behind the Witching Hour figure
                RectangleF auraRect = new RectangleF(
                    panelRect.X - Sf(15),
                    panelRect.Y + Sf(8),
                    panelRect.Width + Sf(30),
                    panelRect.Height - Sf(16));
                using (GraphicsPath auraPath = new GraphicsPath())
                {
                    auraPath.AddEllipse(auraRect);
                    using (PathGradientBrush aura = new PathGradientBrush(auraPath))
                    {
                        float pulse = (float)(0.5 + 0.5 * Math.Sin(_pulseStep));
                        if (_phase == MacroPhase.WitchingHourActive)
                        {
                            aura.CenterColor = Color.FromArgb((int)(110 + 45 * pulse), 232, 54, 210);
                        }
                        else if (_phase == MacroPhase.CountingToWitchingHour)
                        {
                            aura.CenterColor = Color.FromArgb((int)(88 + 22 * pulse), 156, 56, 245);
                        }
                        else
                        {
                            aura.CenterColor = Color.FromArgb(78, 136, 48, 232);
                        }
                        aura.SurroundColors = new Color[] { Color.FromArgb(0, 16, 9, 28) };
                        g.FillEllipse(aura, auraRect);
                    }
                }

                // Draw provided Witching Hour image centered cleanly preserving aspect ratio
                if (_entityImage != null)
                {
                    float padX = Sf(12);
                    float padY = Sf(14);
                    float maxW = panelRect.Width - padX * 2f;
                    float maxH = panelRect.Height - padY * 2f;

                    float ratioX = maxW / _entityImage.Width;
                    float ratioY = maxH / _entityImage.Height;
                    float ratio = Math.Min(ratioX, ratioY);

                    float drawW = _entityImage.Width * ratio;
                    float drawH = _entityImage.Height * ratio;
                    float drawX = panelRect.X + (panelRect.Width - drawW) / 2f;
                    float drawY = panelRect.Y + (panelRect.Height - drawH) / 2f;

                    g.DrawImage(_entityImage, new RectangleF(drawX, drawY, drawW, drawH));
                }

                g.Restore(state);

                Color borderColor = _phase == MacroPhase.WitchingHourActive
                    ? Theme.CardBorderWitching
                    : (_phase == MacroPhase.CountingToWitchingHour ? Theme.CardBorderActive : Theme.CardBorder);
                using (Pen borderPen = new Pen(borderColor, Sf(1.3f)))
                {
                    g.DrawPath(borderPen, path);
                }
            }
        }

        private void DrawHeader(Graphics g)
        {
            using (SolidBrush titleBrush = new SolidBrush(Theme.TextPrimary))
            {
                g.DrawString("Witching Hour Bringer", _titleFont, titleBrush, Sf(242), Sf(16));
            }

            using (SolidBrush subBrush = new SolidBrush(Theme.TextSecondary))
            {
                g.DrawString("Pressure 3:00 AM System Time Macro", _subtitleFont, subBrush, Sf(244), Sf(47));
            }
        }

        private void DrawSystemClockCard(Graphics g)
        {
            RectangleF rowRect = new RectangleF(Sf(244), Sf(78), Sf(354), Sf(38));

            // Status indicator dot (no island box or borders - sits cleanly on the dark purple canvas)
            Color dotColor = Theme.AccentPurpleLight;
            if (_phase == MacroPhase.WitchingHourActive) dotColor = Theme.AccentMagentaBright;
            else if (_phase == MacroPhase.CountingToWitchingHour) dotColor = Color.FromArgb(182, 108, 255);
            else if (_phase == MacroPhase.Completed && !_statusIsError) dotColor = Theme.TextSuccess;
            else if (_statusIsError) dotColor = Theme.TextError;

            float dotSize = Sf(8);
            float dotX = rowRect.X + Sf(2);
            float dotY = rowRect.Y + (rowRect.Height - dotSize) / 2f;

            using (SolidBrush glow = new SolidBrush(Color.FromArgb(75, dotColor)))
            {
                g.FillEllipse(glow, dotX - Sf(3), dotY - Sf(3), dotSize + Sf(6), dotSize + Sf(6));
            }
            using (SolidBrush dotBrush = new SolidBrush(dotColor))
            {
                g.FillEllipse(dotBrush, dotX, dotY, dotSize, dotSize);
            }

            using (SolidBrush labelBrush = new SolidBrush(Theme.TextSecondary))
            using (StringFormat sfLeft = new StringFormat())
            {
                sfLeft.Alignment = StringAlignment.Near;
                sfLeft.LineAlignment = StringAlignment.Center;
                RectangleF labelRect = new RectangleF(dotX + dotSize + Sf(9), rowRect.Y, Sf(120), rowRect.Height);
                g.DrawString("SYSTEM TIME", _clockLabelFont, labelBrush, labelRect, sfLeft);
            }

#if PREVIEW_MODE
            DateTime displayNow = _previewClockOverride.HasValue ? _previewClockOverride.Value : DateTime.Now;
#else
            DateTime displayNow = DateTime.Now;
#endif
            string clockText = displayNow.ToString("h:mm:ss tt");
            Color clockColor = _phase == MacroPhase.WitchingHourActive
                ? Theme.AccentMagentaBright
                : (_phase == MacroPhase.CountingToWitchingHour ? Color.FromArgb(219, 178, 255) : Theme.TextPrimary);

            using (SolidBrush clockBrush = new SolidBrush(clockColor))
            using (StringFormat sfRight = new StringFormat())
            {
                sfRight.Alignment = StringAlignment.Far;
                sfRight.LineAlignment = StringAlignment.Center;
                RectangleF valRect = new RectangleF(rowRect.X + Sf(140), rowRect.Y + Sf(1), rowRect.Width - Sf(140), rowRect.Height);
                g.DrawString(clockText, _clockValueFont, clockBrush, valRect, sfRight);
            }
        }

        private void DrawStatusCard(Graphics g)
        {
            RectangleF cardRect = new RectangleF(Sf(244), Sf(132), Sf(354), Sf(98));
            bool showProgress = (_phase == MacroPhase.CountingToWitchingHour || _phase == MacroPhase.WitchingHourActive);

            using (GraphicsPath path = Theme.CreateRoundedRect(cardRect, Sf(11)))
            {
                Color bgTop = _phase == MacroPhase.WitchingHourActive
                    ? Color.FromArgb(40, 14, 48)
                    : Color.FromArgb(26, 15, 43);
                Color bgBot = Theme.CardBgDeep;

                using (LinearGradientBrush fill = new LinearGradientBrush(cardRect, bgTop, bgBot, LinearGradientMode.Vertical))
                {
                    g.FillPath(fill, path);
                }

                Color borderColor = Theme.CardBorder;
                if (_statusIsError) borderColor = Color.FromArgb(190, 62, 102);
                else if (_phase == MacroPhase.WitchingHourActive) borderColor = Theme.CardBorderWitching;
                else if (_phase == MacroPhase.CountingToWitchingHour) borderColor = Theme.CardBorderActive;

                using (Pen border = new Pen(borderColor, Sf(1.25f)))
                {
                    g.DrawPath(border, path);
                }
            }

            Color headlineColor = Theme.TextPrimary;
            if (_statusIsError) headlineColor = Theme.TextError;
            else if (_phase == MacroPhase.WitchingHourActive) headlineColor = Theme.AccentMagentaBright;
            else if (_phase == MacroPhase.CountingToWitchingHour) headlineColor = Color.FromArgb(242, 224, 255);
            else if (_phase == MacroPhase.Completed) headlineColor = Theme.TextSuccess;

            using (StringFormat sfCenter = new StringFormat())
            {
                sfCenter.Alignment = StringAlignment.Center;
                sfCenter.LineAlignment = StringAlignment.Center;

                float padX = Sf(12);
                RectangleF headRect = showProgress
                    ? new RectangleF(cardRect.X + padX, cardRect.Y + Sf(12), cardRect.Width - padX * 2f, Sf(30))
                    : new RectangleF(cardRect.X + padX, cardRect.Y + Sf(14), cardRect.Width - padX * 2f, Sf(28));

                using (SolidBrush headBrush = new SolidBrush(headlineColor))
                {
                    g.DrawString(_statusHeadline, _countdownFont, headBrush, headRect, sfCenter);
                }

                RectangleF detailRect = showProgress
                    ? new RectangleF(cardRect.X + padX, cardRect.Y + Sf(44), cardRect.Width - padX * 2f, Sf(24))
                    : new RectangleF(cardRect.X + padX, cardRect.Y + Sf(44), cardRect.Width - padX * 2f, Sf(42));

                Color detailCol = _phase == MacroPhase.WitchingHourActive
                    ? Color.FromArgb(236, 198, 246)
                    : Theme.TextSecondary;

                using (SolidBrush detailBrush = new SolidBrush(detailCol))
                {
                    g.DrawString(_statusDetail, _detailFont, detailBrush, detailRect, sfCenter);
                }
            }

            if (showProgress)
            {
                RectangleF trackRect = new RectangleF(
                    cardRect.X + Sf(18),
                    cardRect.Bottom - Sf(18),
                    cardRect.Width - Sf(36),
                    Sf(7));

                using (GraphicsPath trackPath = Theme.CreateRoundedRect(trackRect, Sf(3.5f)))
                using (SolidBrush trackBrush = new SolidBrush(Color.FromArgb(38, 22, 62)))
                {
                    g.FillPath(trackBrush, trackPath);
                }

                float fillWidth = Math.Max(Sf(7), trackRect.Width * _progress);
                if (fillWidth > trackRect.Width) fillWidth = trackRect.Width;

                RectangleF fillRect = new RectangleF(trackRect.X, trackRect.Y, fillWidth, trackRect.Height);
                Color p1 = _phase == MacroPhase.WitchingHourActive
                    ? Color.FromArgb(196, 44, 218)
                    : Color.FromArgb(124, 48, 242);
                Color p2 = _phase == MacroPhase.WitchingHourActive
                    ? Color.FromArgb(255, 108, 228)
                    : Color.FromArgb(224, 68, 212);

                using (GraphicsPath fillPath = Theme.CreateRoundedRect(fillRect, Sf(3.5f)))
                using (LinearGradientBrush fillBrush = new LinearGradientBrush(fillRect, p1, p2, LinearGradientMode.Horizontal))
                {
                    g.FillPath(fillBrush, fillPath);
                }
            }
        }

        private void DrawFooterStatus(Graphics g)
        {
#if PREVIEW_MODE
            bool isAdmin = true;
#else
            bool isAdmin = SystemTimeController.IsAdministrator();
#endif
            string statusText = isAdmin ? "Admin Ready  |  Auto-Time Safe" : "Run as Administrator";
            Color statusCol = isAdmin ? Theme.TextMuted : Theme.TextError;

            using (SolidBrush brush = new SolidBrush(statusCol))
            using (StringFormat sfRight = new StringFormat())
            {
                sfRight.Alignment = StringAlignment.Far;
                sfRight.LineAlignment = StringAlignment.Center;
                RectangleF rect = new RectangleF(Sf(380), Sf(298), Sf(218), Sf(24));
                g.DrawString(statusText, _smallFont, brush, rect, sfRight);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_uiTimer != null) _uiTimer.Dispose();
                if (_entityImage != null) _entityImage.Dispose();
                if (_appIcon != null) _appIcon.Dispose();
                if (_titleFont != null) _titleFont.Dispose();
                if (_subtitleFont != null) _subtitleFont.Dispose();
                if (_clockLabelFont != null) _clockLabelFont.Dispose();
                if (_clockValueFont != null) _clockValueFont.Dispose();
                if (_countdownFont != null) _countdownFont.Dispose();
                if (_detailFont != null) _detailFont.Dispose();
                if (_buttonFont != null) _buttonFont.Dispose();
                if (_smallFont != null) _smallFont.Dispose();
            }
            base.Dispose(disposing);
        }

#if PREVIEW_MODE
        public void SetPreviewState(MacroPhase phase, string headline, string detail, float progress, DateTime clockOverride, bool secondaryBtn, string buttonText)
        {
            _uiTimer.Stop();
            _phase = phase;
            _statusHeadline = headline;
            _statusDetail = detail;
            _statusIsError = false;
            _progress = progress;
            _previewClockOverride = clockOverride;
            _actionButton.SecondaryStyle = secondaryBtn;
            _actionButton.Text = buttonText;
            Invalidate();
            Update();
        }
#endif
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

#if PREVIEW_MODE
            if (args != null && args.Length >= 2 && args[0] == "--preview")
            {
                RunPreviewCapture(args[1]);
                return;
            }
            if (args != null && args.Length >= 1 && args[0] == "--test")
            {
                RunLogicTests();
                return;
            }
#endif

            using (MainForm form = new MainForm())
            {
                Application.Run(form);
            }
        }

#if PREVIEW_MODE
        private static void RunPreviewCapture(string outDir)
        {
            Directory.CreateDirectory(outDir);
            using (MainForm form = new MainForm())
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-2000, -2000);
                form.ShowInTaskbar = false;
                form.Show();
                Application.DoEvents();

                // 1. Idle state
                form.SetPreviewState(
                    MacroPhase.Idle,
                    "Ready for the Witching Hour",
                    "Sets system time to 2:59:55 AM, waits until 3:01 AM,\nthen restores Windows Automatic Time.",
                    0f,
                    new DateTime(2026, 11, 21, 20, 55, 29),
                    false,
                    "Bring me witching hour");
                SaveControlBitmap(form, Path.Combine(outDir, "state_1_idle.png"));

                // 2. Countdown state (2:59:55 -> 3:00:00)
                form.SetPreviewState(
                    MacroPhase.CountingToWitchingHour,
                    "0:03 until witching hour comes",
                    "Clock set to 2:59:55 AM - waiting for 3:00 AM",
                    2f / 5f,
                    new DateTime(2026, 11, 21, 2, 59, 57),
                    true,
                    "Cancel & Restore Real Time");
                SaveControlBitmap(form, Path.Combine(outDir, "state_2_countdown.png"));

                // 3. Witching Hour Active state (3:00 -> 3:01)
                form.SetPreviewState(
                    MacroPhase.WitchingHourActive,
                    "3:00 AM - Witching Hour is here!",
                    "Waiting until 3:01 AM - restoring automatic time in 0:37",
                    23f / 60f,
                    new DateTime(2026, 11, 21, 3, 0, 23),
                    true,
                    "Cancel & Restore Real Time");
                SaveControlBitmap(form, Path.Combine(outDir, "state_3_witching_hour.png"));

                // 4. Restored / Completed state
                form.SetPreviewState(
                    MacroPhase.Completed,
                    "Witching Hour Complete!",
                    "Real time restored (Windows Automatic Time synced)",
                    0f,
                    new DateTime(2026, 11, 21, 20, 56, 34),
                    false,
                    "Bring me witching hour");
                SaveControlBitmap(form, Path.Combine(outDir, "state_4_completed.png"));

                form.Close();
            }
        }

        private static void SaveControlBitmap(Form form, string path)
        {
            Application.DoEvents();
            using (Bitmap bmp = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
            {
                // Draw Form client area including child controls
                using (Bitmap full = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(full, new Rectangle(0, 0, form.Width, form.Height));
                    Point clientOrigin = form.PointToScreen(Point.Empty);
                    int offsetX = clientOrigin.X - form.Left;
                    int offsetY = clientOrigin.Y - form.Top;
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.DrawImage(
                            full,
                            new Rectangle(0, 0, bmp.Width, bmp.Height),
                            new Rectangle(offsetX, offsetY, bmp.Width, bmp.Height),
                            GraphicsUnit.Pixel);
                    }
                }
                bmp.Save(path, ImageFormat.Png);
            }
        }

        private static void RunLogicTests()
        {
            DateTime start = new DateTime(2026, 11, 21, 3, 0, 0);
            DateTime end = new DateTime(2026, 11, 21, 3, 1, 0);

            // Exactly 2:59:55.000 -> 0:05 until witching hour comes
            TickEvaluation e1 = TimerLogic.EvaluateTick(new DateTime(2026, 11, 21, 2, 59, 55, 0), start, end, 5.0);
            Assert(e1.Phase == MacroPhase.CountingToWitchingHour, "e1 phase");
            Assert(e1.PrimaryText == "0:05 until witching hour comes", "e1 text: " + e1.PrimaryText);
            Assert(!e1.ShouldRestore, "e1 restore");

            // 2:59:55.500 -> still 0:05 until witching hour comes
            TickEvaluation e2 = TimerLogic.EvaluateTick(new DateTime(2026, 11, 21, 2, 59, 55, 500), start, end, 5.0);
            Assert(e2.PrimaryText == "0:05 until witching hour comes", "e2 text: " + e2.PrimaryText);

            // 2:59:56.050 -> 0:04 until witching hour comes
            TickEvaluation e3 = TimerLogic.EvaluateTick(new DateTime(2026, 11, 21, 2, 59, 56, 50), start, end, 5.0);
            Assert(e3.PrimaryText == "0:04 until witching hour comes", "e3 text: " + e3.PrimaryText);

            // 2:59:59.200 -> 0:01 until witching hour comes
            TickEvaluation e5 = TimerLogic.EvaluateTick(new DateTime(2026, 11, 21, 2, 59, 59, 200), start, end, 5.0);
            Assert(e5.PrimaryText == "0:01 until witching hour comes", "e5 text: " + e5.PrimaryText);

            // 3:00:00.000 -> Witching Hour Active, wait 1:00 (must NOT restore yet!)
            TickEvaluation e6 = TimerLogic.EvaluateTick(new DateTime(2026, 11, 21, 3, 0, 0, 0), start, end, 5.0);
            Assert(e6.Phase == MacroPhase.WitchingHourActive, "e6 phase");
            Assert(e6.SecondaryText.Contains("1:00"), "e6 detail: " + e6.SecondaryText);
            Assert(!e6.ShouldRestore, "e6 restore");

            // 3:00:59.500 -> Witching Hour Active, wait 0:01 (must NOT restore yet!)
            TickEvaluation e7 = TimerLogic.EvaluateTick(new DateTime(2026, 11, 21, 3, 0, 59, 500), start, end, 5.0);
            Assert(e7.Phase == MacroPhase.WitchingHourActive, "e7 phase");
            Assert(e7.SecondaryText.Contains("0:01"), "e7 detail: " + e7.SecondaryText);
            Assert(!e7.ShouldRestore, "e7 restore");

            // 3:01:00.000 -> Restore!
            TickEvaluation e8 = TimerLogic.EvaluateTick(new DateTime(2026, 11, 21, 3, 1, 0, 0), start, end, 5.0);
            Assert(e8.ShouldRestore, "e8 restore");

            // External far-past clock jump -> Restore!
            TickEvaluation e9 = TimerLogic.EvaluateTick(new DateTime(2026, 11, 21, 1, 0, 0, 0), start, end, 5.0);
            Assert(e9.ShouldRestore, "e9 external jump restore");

            Console.WriteLine("ALL LOGIC TESTS PASSED!");
        }

        private static void Assert(bool cond, string msg)
        {
            if (!cond)
            {
                throw new Exception("Assertion failed: " + msg);
            }
        }
#endif
    }
}
