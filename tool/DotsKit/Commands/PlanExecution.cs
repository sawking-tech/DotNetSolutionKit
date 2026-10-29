using DotsKit.Io;
using DotsKit.Planning;
using DotsKit.Solutions;

namespace DotsKit.Commands;

/// <summary>
/// The end every command that changes a solution shares: show the plan, ask, write it in one transaction, then
/// build - or stop at the conflicts a person resolves first. The plan holds the files as they were read: a tree the
/// command found clean at a commit, startHead, is written only while it is still clean at that commit, so a
/// file saved or a commit made while the plan was built or waited for a yes is not written over.
/// </summary>
internal sealed class PlanExecution(PlanReport report, IPlanWriter writer, IBuilder builder, ReferenceRepair repair, ITerminal terminal, IGit git)
{
    public async Task<ExitCode> RunAsync(Plan plan, Options options, string? startHead, CancellationToken ct)
    {
        report.Show(plan);
        if (!options.Yes && !terminal.Confirm("Write these changes?"))
        {
            terminal.Info("Nothing written: answer y to write, or pass --yes where no one can answer.");
            return ExitCode.Failed;
        }

        if (startHead is not null && await git.CleanHeadAsync(plan.Root, ct) != startHead)
        {
            throw new InvalidOperationException("The working tree or its commit changed after the command started: nothing is written; run the command again on the tree as it is now.");
        }

        var written = await writer.WriteAsync(plan, ct);
        if (written.Repaired.Count > 0)
        {
            terminal.Info($"Written; {written.Repaired.Count} usings and references changed for the namespaces the template gave up, before the build.");
        }

        return written.Conflicts.Count > 0 ? Conflicts(plan, written.Conflicts) : await BuildAsync(plan, options, ct);
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
        {
            terminal.Info($"  {path}");
        }

        terminal.Info("Resolve them in the IDE or with git mergetool, then git commit and dotnet build; git merge --abort undoes the merge.");
        return ExitCode.Conflicts;
    }

    private async Task<ExitCode> BuildAsync(Plan plan, Options options, CancellationToken ct)
    {
        if (options.NoBuild)
        {
            return ExitCode.Done;
        }

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
        {
            terminal.Info($"Written; {result.Changed} references and usings changed for the types the template moved: git diff shows them.");
        }

        if (result.Stopped is { } why)
        {
            terminal.Info($"Written; the repair of references stopped: {why}");
        }

        var build = result.Build;
        if (build.Code == 0)
        {
            terminal.Info("Written; the solution builds.");
            return ExitCode.Done;
        }
        terminal.Info("Written; the build fails, by project:");
        foreach (var line in BuildErrors.ByProject(build.Output))
        {
            terminal.Info(line);
        }

        return ExitCode.Failed;
    }
}
