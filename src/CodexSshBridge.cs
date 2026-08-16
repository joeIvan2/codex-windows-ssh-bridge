// MIT License
//
// Community reference implementation for a Windows OpenSSH forced-command
// bridge. It is intentionally dependency-free so it can be compiled with the
// .NET Framework C# compiler included with Windows.
//
// The normal bridge path must not write to stdout or stderr. Those streams are
// the Codex app-server protocol channel.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

internal sealed class BridgeSettings
{
    internal string BashPath { get; private set; }
    internal string ShellPosixPath { get; private set; }

    private BridgeSettings(string bashPath, string shellPosixPath)
    {
        BashPath = bashPath;
        ShellPosixPath = shellPosixPath;
    }

    internal static BridgeSettings Load(bool requireShellPosixPath)
    {
        string configPath = Path.Combine(AppDirectory(), "bridge.ini");
        if (!File.Exists(configPath))
        {
            throw new InvalidOperationException("Bridge configuration is missing.");
        }

        Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string rawLine in File.ReadAllLines(configPath, new UTF8Encoding(false)))
        {
            string line = rawLine.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal))
            {
                continue;
            }

            int separator = line.IndexOf('=');
            if (separator <= 0)
            {
                throw new InvalidOperationException("Bridge configuration contains an invalid entry.");
            }

            string key = line.Substring(0, separator).Trim();
            string value = line.Substring(separator + 1).Trim();
            if (key.Length == 0 || value.Length == 0)
            {
                throw new InvalidOperationException("Bridge configuration contains an empty entry.");
            }

            if (key != "bash_path" && key != "shell_posix_path")
            {
                throw new InvalidOperationException("Bridge configuration contains an unknown entry.");
            }

            if (values.ContainsKey(key))
            {
                throw new InvalidOperationException("Bridge configuration contains a duplicate entry.");
            }

            values[key] = value;
        }

        string bashPath = Required(values, "bash_path");
        if (!Path.IsPathRooted(bashPath) || bashPath.StartsWith(@"\\", StringComparison.Ordinal) || !File.Exists(bashPath))
        {
            throw new InvalidOperationException("Configured Bash executable is unavailable.");
        }

        string shellPosixPath = null;
        if (requireShellPosixPath)
        {
            shellPosixPath = Required(values, "shell_posix_path");
            if (!shellPosixPath.StartsWith("/", StringComparison.Ordinal) || ContainsControlCharacter(shellPosixPath))
            {
                throw new InvalidOperationException("Configured POSIX shell path is invalid.");
            }

            string shimPath = Path.Combine(AppDirectory(), "codex-ssh-login-shell.exe");
            if (!File.Exists(shimPath))
            {
                throw new InvalidOperationException("Login-shell shim is unavailable.");
            }
        }

        return new BridgeSettings(bashPath, shellPosixPath);
    }

    private static string Required(Dictionary<string, string> values, string key)
    {
        string value;
        if (!values.TryGetValue(key, out value) || String.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Bridge configuration is incomplete.");
        }

        return value;
    }

    private static bool ContainsControlCharacter(string value)
    {
        foreach (char character in value)
        {
            if (character == '\0' || character == '\r' || character == '\n')
            {
                return true;
            }
        }

        return false;
    }

    private static string AppDirectory()
    {
        string location = Assembly.GetExecutingAssembly().Location;
        string directory = Path.GetDirectoryName(location);
        if (String.IsNullOrEmpty(directory))
        {
            throw new InvalidOperationException("Bridge directory cannot be determined.");
        }

        return directory;
    }
}

internal static class NativeChildProcess
{
    private const int StdInputHandle = -10;
    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;
    private const uint DuplicateSameAccess = 0x00000002;
    private const uint StartfUseStdHandles = 0x00000100;
    private const uint CreateNoWindow = 0x08000000;
    private const uint Infinite = 0xFFFFFFFF;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public uint cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public ushort wShowWindow;
        public ushort cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(
        string applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DuplicateHandle(
        IntPtr sourceProcessHandle,
        IntPtr sourceHandle,
        IntPtr targetProcessHandle,
        out IntPtr targetHandle,
        uint desiredAccess,
        bool inheritHandle,
        uint options);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr handle, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    internal static void ClearShellStartupEnvironment()
    {
        Environment.SetEnvironmentVariable("BASH_ENV", null);
        Environment.SetEnvironmentVariable("ENV", null);
        Environment.SetEnvironmentVariable("CDPATH", null);
        Environment.SetEnvironmentVariable("PROMPT_COMMAND", null);
        Environment.SetEnvironmentVariable("SHELLOPTS", null);
        Environment.SetEnvironmentVariable("BASHOPTS", null);
        Environment.SetEnvironmentVariable("BASH_XTRACEFD", null);
        Environment.SetEnvironmentVariable("PS4", null);
    }

    internal static int Run(string applicationName, IList<string> arguments)
    {
        StringBuilder commandLine = new StringBuilder(QuoteArgument(applicationName));
        foreach (string argument in arguments)
        {
            commandLine.Append(' ');
            commandLine.Append(QuoteArgument(argument));
        }

        IntPtr standardInput = IntPtr.Zero;
        IntPtr standardOutput = IntPtr.Zero;
        IntPtr standardError = IntPtr.Zero;
        ProcessInformation child = new ProcessInformation();

        try
        {
            standardInput = DuplicateStandardHandle(StdInputHandle);
            standardOutput = DuplicateStandardHandle(StdOutputHandle);
            standardError = DuplicateStandardHandle(StdErrorHandle);
            if (standardInput == IntPtr.Zero || standardOutput == IntPtr.Zero || standardError == IntPtr.Zero)
            {
                Console.Error.WriteLine("Codex SSH bridge could not preserve the SSH standard handles.");
                return 127;
            }

            StartupInfo startupInfo = new StartupInfo
            {
                cb = (uint)Marshal.SizeOf(typeof(StartupInfo)),
                dwFlags = StartfUseStdHandles,
                hStdInput = standardInput,
                hStdOutput = standardOutput,
                hStdError = standardError
            };

            if (!CreateProcess(
                applicationName,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                true,
                CreateNoWindow,
                IntPtr.Zero,
                null,
                ref startupInfo,
                out child))
            {
                Console.Error.WriteLine("Codex SSH bridge could not start Bash (Win32 error {0}).", Marshal.GetLastWin32Error());
                return 127;
            }

            uint waitResult = WaitForSingleObject(child.hProcess, Infinite);
            if (waitResult != 0)
            {
                Console.Error.WriteLine("Codex SSH bridge could not wait for Bash.");
                return 127;
            }

            uint exitCode;
            return GetExitCodeProcess(child.hProcess, out exitCode) ? unchecked((int)exitCode) : 127;
        }
        finally
        {
            if (child.hThread != IntPtr.Zero)
            {
                CloseHandle(child.hThread);
            }

            if (child.hProcess != IntPtr.Zero)
            {
                CloseHandle(child.hProcess);
            }

            if (standardInput != IntPtr.Zero)
            {
                CloseHandle(standardInput);
            }

            if (standardOutput != IntPtr.Zero)
            {
                CloseHandle(standardOutput);
            }

            if (standardError != IntPtr.Zero)
            {
                CloseHandle(standardError);
            }
        }
    }

    internal static string QuoteArgument(string value)
    {
        StringBuilder result = new StringBuilder();
        result.Append('"');
        int slashCount = 0;

        foreach (char character in value)
        {
            if (character == '\\')
            {
                slashCount++;
                continue;
            }

            if (character == '"')
            {
                result.Append('\\', slashCount * 2 + 1);
                result.Append('"');
            }
            else
            {
                result.Append('\\', slashCount);
                result.Append(character);
            }

            slashCount = 0;
        }

        result.Append('\\', slashCount * 2);
        result.Append('"');
        return result.ToString();
    }

    private static IntPtr DuplicateStandardHandle(int standardHandle)
    {
        IntPtr source = GetStdHandle(standardHandle);
        if (source == IntPtr.Zero || source == new IntPtr(-1))
        {
            return IntPtr.Zero;
        }

        IntPtr duplicate;
        return DuplicateHandle(
            GetCurrentProcess(),
            source,
            GetCurrentProcess(),
            out duplicate,
            0,
            true,
            DuplicateSameAccess) ? duplicate : IntPtr.Zero;
    }
}

internal static class TemporaryScript
{
    internal static string Write(string originalCommand)
    {
        string baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (String.IsNullOrEmpty(baseDirectory))
        {
            throw new IOException("Bridge local application-data directory is unavailable.");
        }

        string directory = Path.Combine(baseDirectory, "CodexSshBridge", "tmp");
        Directory.CreateDirectory(directory);
        byte[] bytes = new UTF8Encoding(false).GetBytes(originalCommand);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            string candidate = Path.Combine(directory, "command-" + Guid.NewGuid().ToString("N") + ".sh");
            try
            {
                using (FileStream stream = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                }

                return candidate;
            }
            catch (IOException)
            {
                // A random collision is harmless; choose a new filename.
            }
        }

        throw new IOException("Bridge could not create a private temporary script.");
    }

    internal static void Delete(string path)
    {
        if (String.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // The process is ending and no command content is logged.
        }
    }
}

internal static class LoginShellArguments
{
    internal static List<string> Normalize(string[] arguments)
    {
        List<string> normalized = new List<string> { "--noprofile", "--norc" };
        bool afterCommandOption = false;

        foreach (string argument in arguments)
        {
            if (afterCommandOption)
            {
                normalized.Add(argument);
                continue;
            }

            if (argument == "--")
            {
                normalized.Add(argument);
                afterCommandOption = true;
                continue;
            }

            string rewritten = RemoveInteractiveAndLoginFlags(argument);
            if (rewritten == null)
            {
                continue;
            }

            normalized.Add(rewritten);
            if (IsCommandOption(rewritten))
            {
                afterCommandOption = true;
            }
        }

        return normalized;
    }

    internal static bool ContainsCommandOption(IList<string> arguments)
    {
        foreach (string argument in arguments)
        {
            if (IsCommandOption(argument))
            {
                return true;
            }
        }

        return false;
    }

    private static string RemoveInteractiveAndLoginFlags(string argument)
    {
        if (argument == "-l" || argument == "--login" || argument == "-i" || argument == "--interactive")
        {
            return null;
        }

        if (argument.Length > 2 && argument[0] == '-' && argument[1] != '-')
        {
            StringBuilder remaining = new StringBuilder("-");
            for (int index = 1; index < argument.Length; index++)
            {
                char flag = argument[index];
                if (flag != 'i' && flag != 'l')
                {
                    remaining.Append(flag);
                }
            }

            return remaining.Length == 1 ? null : remaining.ToString();
        }

        return argument;
    }

    private static bool IsCommandOption(string argument)
    {
        return argument == "-c" || argument == "--command" || argument.StartsWith("--command=", StringComparison.Ordinal);
    }
}

internal static class BridgeSelfTest
{
    internal static int Run()
    {
        try
        {
            if (NativeChildProcess.QuoteArgument("two words") != "\"two words\"")
            {
                throw new InvalidOperationException();
            }

            if (NativeChildProcess.QuoteArgument("trailing\\") != "\"trailing\\\\\"")
            {
                throw new InvalidOperationException();
            }

            List<string> normalized = LoginShellArguments.Normalize(new[] { "-lic", "printf ok" });
            if (normalized.Count != 4 ||
                normalized[0] != "--noprofile" ||
                normalized[1] != "--norc" ||
                normalized[2] != "-c" ||
                normalized[3] != "printf ok")
            {
                throw new InvalidOperationException();
            }

            AssertShellStartupVariablesAreCleared();
            Console.Out.WriteLine("codex-ssh-reference self-test: OK");
            return 0;
        }
        catch
        {
            Console.Error.WriteLine("codex-ssh-reference self-test: FAILED");
            return 1;
        }
    }

    private static void AssertShellStartupVariablesAreCleared()
    {
        string[] variableNames = new[]
        {
            "BASH_ENV",
            "ENV",
            "CDPATH",
            "PROMPT_COMMAND",
            "SHELLOPTS",
            "BASHOPTS",
            "BASH_XTRACEFD",
            "PS4"
        };
        Dictionary<string, string> originalValues = new Dictionary<string, string>();

        try
        {
            foreach (string variableName in variableNames)
            {
                originalValues[variableName] = Environment.GetEnvironmentVariable(variableName);
                Environment.SetEnvironmentVariable(variableName, "self-test-value");
            }

            NativeChildProcess.ClearShellStartupEnvironment();
            foreach (string variableName in variableNames)
            {
                if (Environment.GetEnvironmentVariable(variableName) != null)
                {
                    throw new InvalidOperationException();
                }
            }
        }
        finally
        {
            foreach (string variableName in variableNames)
            {
                Environment.SetEnvironmentVariable(variableName, originalValues[variableName]);
            }
        }
    }
}

public static class CodexSshBridge
{
    public static int Main(string[] arguments)
    {
        if (arguments.Length == 1 && arguments[0] == "--self-test")
        {
            return BridgeSelfTest.Run();
        }

        if (arguments.Length != 0)
        {
            Console.Error.WriteLine("Codex SSH bridge must be invoked as a forced command.");
            return 64;
        }

        string originalCommand = Environment.GetEnvironmentVariable("SSH_ORIGINAL_COMMAND");
        if (String.IsNullOrWhiteSpace(originalCommand))
        {
            Console.Error.WriteLine("Codex SSH bridge received no remote command.");
            return 126;
        }

        string scriptPath = null;
        try
        {
            BridgeSettings settings = BridgeSettings.Load(true);
            scriptPath = TemporaryScript.Write(originalCommand);
            NativeChildProcess.ClearShellStartupEnvironment();
            Environment.SetEnvironmentVariable("SHELL", settings.ShellPosixPath);
            Environment.SetEnvironmentVariable("SSH_ORIGINAL_COMMAND", null);

            return NativeChildProcess.Run(
                settings.BashPath,
                new[] { "--noprofile", "--norc", "--", scriptPath });
        }
        catch
        {
            Console.Error.WriteLine("Codex SSH bridge configuration or startup failed.");
            return 127;
        }
        finally
        {
            TemporaryScript.Delete(scriptPath);
        }
    }
}

public static class CodexSshLoginShell
{
    public static int Main(string[] arguments)
    {
        if (arguments.Length == 1 && arguments[0] == "--self-test")
        {
            return BridgeSelfTest.Run();
        }

        List<string> childArguments = LoginShellArguments.Normalize(arguments);
        if (!LoginShellArguments.ContainsCommandOption(childArguments))
        {
            Console.Error.WriteLine("Codex SSH login-shell shim requires a command.");
            return 64;
        }

        try
        {
            BridgeSettings settings = BridgeSettings.Load(false);
            NativeChildProcess.ClearShellStartupEnvironment();
            Environment.SetEnvironmentVariable("SHELL", "/usr/bin/bash");
            Environment.SetEnvironmentVariable("SSH_ORIGINAL_COMMAND", null);
            return NativeChildProcess.Run(settings.BashPath, childArguments);
        }
        catch
        {
            Console.Error.WriteLine("Codex SSH login-shell shim configuration or startup failed.");
            return 127;
        }
    }
}
