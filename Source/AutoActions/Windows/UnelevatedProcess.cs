using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoActions.Windows
{
    /// <summary>
    /// Starts a program as the logged-on user when arzGUI itself is elevated. A child process
    /// normally inherits the parent's token, so a run-program action fired from an elevated arzGUI
    /// would start the program as administrator - which breaks anything that refuses to run elevated
    /// (OpenTabletDriver) or writes its settings to the wrong place. The token comes from the shell
    /// (explorer.exe), which is always the interactive user at their normal integrity level.
    /// </summary>
    internal static class UnelevatedProcess
    {
        const uint TOKEN_DUPLICATE = 0x0002;
        const uint MAXIMUM_ALLOWED = 0x02000000;
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        const uint CREATE_UNICODE_ENVIRONMENT = 0x0400;
        const int SecurityImpersonation = 2;
        const int TokenPrimary = 1;

        [StructLayout(LayoutKind.Sequential)]
        struct STARTUPINFO
        {
            public int cb;
            public IntPtr lpReserved;
            public IntPtr lpDesktop;
            public IntPtr lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [DllImport("user32.dll")]
        static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        static extern int GetWindowThreadProcessId(IntPtr window, out int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool DuplicateTokenEx(IntPtr existingToken, uint access, IntPtr attributes,
            int impersonationLevel, int tokenType, out IntPtr newToken);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CreateProcessWithTokenW(IntPtr token, int logonFlags, string applicationName,
            StringBuilder commandLine, uint creationFlags, IntPtr environment, string currentDirectory,
            ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInformation);

        /// <summary>
        /// Starts <paramref name="fileName"/> as the shell user. Returns its process id, or 0 with a
        /// reason in <paramref name="error"/> - the caller then falls back to a normal start.
        /// </summary>
        public static int Start(string fileName, string arguments, out string error)
        {
            IntPtr shellToken = IntPtr.Zero;
            IntPtr primaryToken = IntPtr.Zero;
            IntPtr shell = IntPtr.Zero;
            try
            {
                IntPtr shellWindow = GetShellWindow();
                if (shellWindow == IntPtr.Zero)
                {
                    error = "the desktop shell is not running";
                    return 0;
                }
                int shellProcessId;
                GetWindowThreadProcessId(shellWindow, out shellProcessId);
                shell = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, shellProcessId);
                if (shell == IntPtr.Zero)
                    return Failed("OpenProcess", out error);
                if (!OpenProcessToken(shell, TOKEN_DUPLICATE, out shellToken))
                    return Failed("OpenProcessToken", out error);
                if (!DuplicateTokenEx(shellToken, MAXIMUM_ALLOWED, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out primaryToken))
                    return Failed("DuplicateTokenEx", out error);

                StringBuilder commandLine = new StringBuilder("\"" + fileName + "\"");
                if (!string.IsNullOrEmpty(arguments))
                    commandLine.Append(" ").Append(arguments);
                STARTUPINFO startupInfo = new STARTUPINFO();
                startupInfo.cb = Marshal.SizeOf(typeof(STARTUPINFO));
                PROCESS_INFORMATION process;
                if (!CreateProcessWithTokenW(primaryToken, 0, null, commandLine, CREATE_UNICODE_ENVIRONMENT,
                        IntPtr.Zero, Path.GetDirectoryName(fileName), ref startupInfo, out process))
                    return Failed("CreateProcessWithTokenW", out error);

                CloseHandle(process.hThread);
                CloseHandle(process.hProcess);
                error = null;
                return process.dwProcessId;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return 0;
            }
            finally
            {
                if (primaryToken != IntPtr.Zero) CloseHandle(primaryToken);
                if (shellToken != IntPtr.Zero) CloseHandle(shellToken);
                if (shell != IntPtr.Zero) CloseHandle(shell);
            }
        }

        static int Failed(string call, out string error)
        {
            error = call + " failed: " + new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message;
            return 0;
        }
    }
}
