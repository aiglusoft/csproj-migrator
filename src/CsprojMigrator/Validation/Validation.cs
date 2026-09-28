using System.Diagnostics;
using System.Text.Json;

namespace CsprojMigrator.Validation;

public sealed record StepResult(string Step, string Project, string Tfm, bool Success, string? Output);

public sealed class DotNetRunner
{
    public static async Task<StepResult> RunAsync(string workdir, string args, string step, string project, string tfm, int timeoutSeconds = 600)
    {
        var psi = new ProcessStartInfo("dotnet", args)
        {
            WorkingDirectory = workdir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        try
        {
            using var p = new Process { StartInfo = psi };
            var sb = new System.Text.StringBuilder();
            p.OutputDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { try { p.Kill(); } catch { } }
            return new StepResult(step, project, tfm, p.ExitCode == 0, sb.ToString());
        }
        catch (Exception ex)
        {
            return new StepResult(step, project, tfm, false, ex.Message);
        }
    }
}

public sealed class ValidationRunner
{
    private readonly bool _restore;
    private readonly bool _build;
    private readonly bool _test;

    public ValidationRunner(bool restore = true, bool build = true, bool test = true)
    {
        _restore = restore; _build = build; _test = test;
    }

    // Validates each project x each TFM with restore/build/test (§45-46).
    public async Task<IReadOnlyList<StepResult>> ValidateAsync(
        string destinationRepo, IReadOnlyList<(string CsprojPath, IReadOnlyList<string> Tfms)> targets)
    {
        var results = new List<StepResult>();
        foreach (var (csproj, tfms) in targets)
        {
            var workdir = Path.GetDirectoryName(csproj) ?? destinationRepo;
            var name = Path.GetFileNameWithoutExtension(csproj);
            var enumerable = tfms.Count == 0 ? new[] { "" } : tfms;
            foreach (var tfm in enumerable)
            {
                var suffix = string.IsNullOrEmpty(tfm) ? "" : $" -f {tfm}";
                if (_restore)
                    results.Add(await DotNetRunner.RunAsync(workdir, $"restore \"{csproj}\"{suffix}", "RESTORE", name, tfm));
                if (_build)
                    results.Add(await DotNetRunner.RunAsync(workdir, $"build \"{csproj}\"{suffix} --no-restore", "BUILD", name, tfm));
                if (_test)
                {
                    // Only run test step for testable projects; `dotnet test` on a
                    // non-test project fails — record as skipped-success to avoid noise.
                    var isTest = await IsTestProjectAsync(csproj);
                    if (isTest)
                        results.Add(await DotNetRunner.RunAsync(workdir, $"test \"{csproj}\"{suffix} --no-build", "TEST", name, tfm));
                }
            }
        }
        return results;
    }

    private static async Task<bool> IsTestProjectAsync(string csproj)
    {
        try
        {
            var text = await File.ReadAllTextAsync(csproj);
            return text.Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("xunit", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("NUnit", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("MSTest", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static void WriteJsonReport(string path, object report)
    {
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
