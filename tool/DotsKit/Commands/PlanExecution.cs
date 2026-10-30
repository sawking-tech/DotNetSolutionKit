using DotsKit.Io;
using DotsKit.Planning;
using DotsKit.Solutions;

namespace DotsKit.Commands;

/// <summary>
/// The end every command that changes a solution shares: show the plan, ask, write it in one transaction, then
/// build - or stop at the conflicts a person resolves first. The plan holds the files as they were read: the tree is
/// written only while it is as the command found it (<see cref="TreeStart"/>), so a file saved, a branch checked out
/// or a commit made while the plan was built or waited for a yes is not written over.
/// </summary>
internal sealed class PlanExecution(PlanReport report, IPlanWriter writer, IBuilder builder, ReferenceRepair repair, ITerminal terminal, IGit git)
{
    public async Task<ExitCode> RunAsync(Plan plan, Options options, TreeStart start, CancellationToken ct)
    {
        report.Show(plan);
        if (!options.Yes && !terminal.Confirm("Write these changes?"))
        {
            terminal.Info("Nothing written: answer y to write, or pass --yes where no one can answer.");
            return ExitCode.Failed;
        }

        if (!await start.IsStillAsync(git, plan.Root, ct))
        {
            throw new InvalidOperationException("The working tree, a file of it or its commit changed after the command started: nothing is written; run the command again on the tree as it is now.");
        }

        var written = await writer.WriteAsync(plan, ct);
        ShowRepairs("before the build, for the namespaces the template gave up", written.Repaired);
        var conflicts = written.Conflicts.Count > 0;
        var code = conflicts ? Conflicts(plan, written.Conflicts) : await BuildAsync(plan, options, ct);
        if (plan.Written(conflicts).Upgrade is { } upgrade)
        {
            terminal.Info((conflicts, plan.OnConflicts is not null) switch
            {
                (true, true) => $"The last part of the upgrade to {upgrade.To}: resolve its conflicts and commit, then dotskit upgrade repairs the usings, builds and finishes the upgrade.",
                (true, false) => $"A part of the upgrade to {upgrade.To}: resolve its conflicts and commit, then the next part, dotskit upgrade.",
                _ => $"A part of the upgrade to {upgrade.To} is written: commit it, then the next part, dotskit upgrade.",
            });
        }

        return code;
    }

    /// <summary>The usings and references a repair changed, by project, so the team sees where the template's moves reached its code.</summary>
    private void ShowRepairs(string when, IReadOnlyList<Repair> repairs)
    {
        if (repairs.Count == 0)
        {
            return;
        }

        terminal.Info($"Written; {repairs.Count} usings and references changed {when}, by project:");
        foreach (var line in RepairReport.ByProject(repairs))
        {
            terminal.Info(line);
        }
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
                ? await repair.RunAsync(plan.Root, plan.Types, first, ct, plan.Waiting)
                : new(first, [], null);
        }
        catch (OperationCanceledException)
        {
            terminal.Info("Written; the build was cancelled: run dotnet build to check the solution.");
            return ExitCode.Failed;
        }
        ShowRepairs("by the errors of the build, for the types the template moved", result.Applied);

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
