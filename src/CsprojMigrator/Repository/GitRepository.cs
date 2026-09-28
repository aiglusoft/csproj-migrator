using System.Diagnostics;

namespace CsprojMigrator.Repository;

public sealed class GitRepository
{
    public static bool IsGitRepo(string dir)
    {
        var r = RunGit(dir, "rev-parse --is-inside-work-tree");
        return r.ExitCode == 0 && r.StdOut.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasUncommittedChanges(string dir)
    {
        var r = RunGit(dir, "status --porcelain");
        return r.ExitCode == 0 && !string.IsNullOrWhiteSpace(r.StdOut);
    }

    public static string? CurrentHead(string dir)
    {
        var r = RunGit(dir, "rev-parse HEAD");
        return r.ExitCode == 0 ? r.StdOut.Trim() : null;
    }

    public static bool BranchExists(string dir, string branch)
    {
        var r = RunGit(dir, $"rev-parse --verify --quiet refs/heads/{branch}");
        return r.ExitCode == 0;
    }

    public static (bool Ok, string Message) CreateOrCheckoutBranch(string dir, string branch, bool nonInteractive)
    {
        if (BranchExists(dir, branch))
        {
            if (nonInteractive)
                return (false, $"Branch '{branch}' already exists. Refusing silent checkout in non-interactive mode.");
            return (false, $"EXISTS:{branch}");
        }
        var r = RunGit(dir, $"checkout -b {branch}");
        return r.ExitCode == 0 ? (true, branch) : (false, r.StdErr);
    }

    public static (bool Ok, string Message) CheckoutBranch(string dir, string branch)
    {
        var r = RunGit(dir, $"checkout {branch}");
        return r.ExitCode == 0 ? (true, branch) : (false, r.StdErr);
    }

    public static bool RestoreToCommit(string dir, string commit)
    {
        var r = RunGit(dir, $"reset --hard {commit}");
        return r.ExitCode == 0;
    }

    public sealed record GitResult(int ExitCode, string StdOut, string StdErr);

    public static GitResult RunGit(string workdir, string args)
    {
        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory = workdir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        try
        {
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForTimeSpan(TimeSpan.FromSeconds(30));
            return new GitResult(p.ExitCode, stdout, stderr);
        }
        catch (Exception ex)
        {
            return new GitResult(1, "", ex.Message);
        }
    }
}

internal static class ProcessExtensions
{
    public static void WaitForTimeSpan(this Process p, TimeSpan timeout)
    {
        if (!p.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { p.Kill(); } catch { }
        }
    }
}
