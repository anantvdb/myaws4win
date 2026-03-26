using System.Diagnostics;

namespace MyAws.Core.Services;

public sealed class SshService
{
    private readonly string _user;
    private readonly string _knownHostsFile;

    public SshService(string user, string knownHostsFile)
    {
        _user = user;
        _knownHostsFile = knownHostsFile;
    }

    public void OpenTerminal(string host)
    {
        var sshArgs = BuildTerminalArgs(host);

        // Try Windows Terminal first, fall back to cmd.exe
        if (TryLaunchWindowsTerminal(sshArgs))
            return;

        LaunchCmdTerminal(sshArgs);
    }

    public int RunRemoteCommand(string host, string command)
    {
        var sshArgs = BuildRemoteCommandArgs(host, command);

        var psi = new ProcessStartInfo("ssh", sshArgs)
        {
            UseShellExecute = false,
        };

        using var process = Process.Start(psi);
        process?.WaitForExit();
        return process?.ExitCode ?? -1;
    }

    internal string BuildTerminalArgs(string host) =>
        $"-q -o StrictHostKeyChecking=accept-new -o UserKnownHostsFile=\"{_knownHostsFile}\" {_user}@{host}";

    internal string BuildRemoteCommandArgs(string host, string command) =>
        $"-q -t -o StrictHostKeyChecking=accept-new -o UserKnownHostsFile=\"{_knownHostsFile}\" {_user}@{host} \"bash -icl '{command}'\"";

    private static bool TryLaunchWindowsTerminal(string sshArgs)
    {
        try
        {
            var psi = new ProcessStartInfo("wt.exe", $"ssh {sshArgs}")
            {
                UseShellExecute = true,
            };
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void LaunchCmdTerminal(string sshArgs)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c start cmd /k ssh {sshArgs}")
        {
            UseShellExecute = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        Process.Start(psi);
    }
}
