using DotsKit.Io;
using DotsKit.Planning;
using DotsKit.Solutions;

namespace DotsKit.Commands;

/// <summary>
/// The end every command that changes a solution shares: show the plan, ask, write it in one transaction, then
/// build - or stop at the conflicts a person resolves first.
/// </summary>
internal sealed class PlanExecution(PlanReport report, IPlanWriter writer, IBuilder builder, ReferenceRepair repair, ITerminal terminal)
{
    public async Task<ExitCode> RunAsync(Plan plan, Options options, CancellationToken ct)
    {
        report.Show(plan);
        if (!options.Yes && !terminal.Confirm("Write these changes?"))
        {
            terminal.Info("Nothing written: answer y to write, or pass --yes where no one can answer.");
            return ExitCode.Failed;
        }
        var conflicts = await writer.WriteAsync(plan, ct);
        return conflicts.Count > 0 ? Conflicts(plan, conflicts) : await BuildAsync(plan, options, ct);
    }

    private ExitCode Conflicts(Plan plan, IReadOnlyList<string> conflicts)
    {
        if (plan.ByGit is null)
        {
            terminal.Info("Written. Resolve the conflicts marked <<<<<<< solution / >>>>>>> template, then build.");
            return ExitCode.Conflicts;
        }
        terminal.Info($"Merged by git, not committed; {conflicts.Count} file(s) in conflict:");
        foreach (var path in conflicts)
            terminal.Info($"  {path}");
        terminal.Info("Resolve them in the IDE or with git mergetool, then git commit and dotnet build; git merge --abort undoes the merge.");
        return ExitCode.Conflicts;
    }

    private async Task<ExitCode> BuildAsync(Plan plan, Options options, CancellationToken ct)
    {
        if (options.NoBuild)
            return ExitCode.Done;
        RepairResult result;
        try
        {
            var first = await builder.BuildAsync(plan.Root, ct);
            result = first.Code != 0 && plan.Types is not null
                ? await repair.RunAsync(plan.Root, plan.Types, first, ct)
                : new(first, 0, null);
        }
        catch (OperationCanceledException)
        {
            terminal.Info("Written; the build was cancelled: run dotnet build to check the solution.");
            return ExitCode.Failed;
        }
        if (result.Changed > 0)
            terminal.Info($"Written; {result.Changed} references and usings changed for the types the template moved: git diff shows them.");
        if (result.Stopped is { } why)
            terminal.Info($"Written; the repair of references stopped: {why}");
        var build = result.Build;
        if (build.Code == 0)
        {
            terminal.Info("Written; the solution builds.");
            return ExitCode.Done;
        }
        terminal.Info("Written; the build fails, by project:");
        foreach (var line in BuildErrors.ByProject(build.Output))
            terminal.Info(line);
        return ExitCode.Failed;
    }
}
