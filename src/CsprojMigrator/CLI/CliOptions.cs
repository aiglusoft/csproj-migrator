// Copyright © 2026 Aiglusoft. Tous droits réservés.
// Ce fichier fait partie du patrimoine logiciel d'Aiglusoft. Toute reproduction,
// modification, distribution ou utilisation sans autorisation écrite préalable est interdite.

namespace Aiglusoft.CsprojMigrator.Cli;

public sealed class CliOptions
{
    public string Command { get; init; } = "";
    public string Source { get; init; } = "";
    public string Reference { get; init; } = "";
    public string Destination { get; init; } = "";
    public string Branch { get; init; } = "feature/multitarget";
    public bool DryRun { get; init; }
    public bool NonInteractive { get; init; }
    public string? Config { get; init; }
    public string? PlanOut { get; init; }

    public static bool TryParse(string[] args, out CliOptions? options, out string error)
    {
        options = null; error = "";
        if (args.Length == 0)
        {
            error = "Usage: csproj-migrator <analyze|plan|migrate|validate> --source <dir> --reference <dir> --destination <dir> [--branch <name>] [--dry-run] [--non-interactive] [--config <file>]";
            return false;
        }
        var cmd = args[0].ToLowerInvariant();
        if (cmd is "-h" or "--help" or "help")
        {
            error = "HELP";
            return false;
        }
        if (cmd is not ("analyze" or "plan" or "migrate" or "validate"))
        {
            error = $"Unknown command '{args[0]}'. Expected analyze|plan|migrate|validate.";
            return false;
        }
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "--dry-run" or "--non-interactive" or "-h" or "--help")
            {
                flags.Add(a.ToLowerInvariant());
                continue;
            }
            if (a.StartsWith("--"))
            {
                var key = a[2..].ToLowerInvariant();
                if (i + 1 >= args.Length) { error = $"Missing value for {a}."; return false; }
                map[key] = args[++i];
            }
            else { error = $"Unexpected argument '{a}'."; return false; }
        }
        if (flags.Contains("-h") || flags.Contains("--help"))
        {
            error = "HELP";
            return false;
        }
        map.TryGetValue("source", out var source);
        map.TryGetValue("reference", out var reference);
        map.TryGetValue("destination", out var destination);
        map.TryGetValue("branch", out var branch);
        map.TryGetValue("config", out var config);
        map.TryGetValue("plan-out", out var planOut);

        if (cmd == "validate")
        {
            if (string.IsNullOrWhiteSpace(destination)) { error = "validate requires --destination <dir>."; return false; }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(destination))
            { error = $"{cmd} requires --source, --reference and --destination."; return false; }
        }

        options = new CliOptions
        {
            Command = cmd,
            Source = source ?? "",
            Reference = reference ?? "",
            Destination = destination ?? "",
            Branch = string.IsNullOrWhiteSpace(branch) ? "feature/multitarget" : branch!,
            DryRun = flags.Contains("--dry-run"),
            NonInteractive = flags.Contains("--non-interactive"),
            Config = config,
            PlanOut = planOut,
        };
        return true;
    }

    public static string Help() => """
        CSProj Migrator
        Usage:
          csproj-migrator analyze --source <dir> --reference <dir> --destination <dir> [--config <file>]
          csproj-migrator plan    --source <dir> --reference <dir> --destination <dir> [--config <file>] [--plan-out <file>]
          csproj-migrator migrate --source <dir> --reference <dir> --destination <dir> [--branch <name>] [--dry-run] [--non-interactive] [--config <file>]
          csproj-migrator validate --destination <dir>

        Rules: A and B are read-only. Only C (destination) is modified. Ambiguities
        require explicit mapping in non-interactive mode (exit code 2, MIGRATION_AMBIGUOUS_MAPPING).
        """;
}
