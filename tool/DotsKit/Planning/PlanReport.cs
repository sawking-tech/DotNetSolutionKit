using DotsKit.Io;
using DotsKit.Merging;

namespace DotsKit.Planning;

/// <summary>Shows a plan before it is written: every file with what happens to it and why, the projects of All.sln, the manifest.</summary>
internal sealed class PlanReport(ITerminal terminal)
{
    public void Show(Plan plan)
    {
        ShowFiles(plan.Changes);
        ShowMembership(plan);
        foreach (var line in plan.Packages ?? [])
        {
            terminal.Info($"  package   {line}");
        }

        terminal.Info($"  manifest  {Layout.ManifestPath}");
        ShowCounts(plan.Changes);
    }

    /// <summary>Every file the plan touches, by kind: what is written is what was shown.</summary>
    private void ShowFiles(IReadOnlyList<Change> changes)
    {
        foreach (var change in changes.OrderBy(c => c.Kind).ThenBy(c => c.Path, StringComparer.Ordinal))
        {
            terminal.Info($"  {change.Kind,-8}  {change.Path}: {change.Reason}");
        }
    }

    private void ShowCounts(IReadOnlyList<Change> changes)
    {
        var counts = changes.GroupBy(c => c.Kind).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}");
        terminal.Info(changes.Count == 0 ? "No file changes." : string.Join(", ", counts));
    }

    private void ShowMembership(Plan plan)
    {
        foreach (var project in plan.Membership.Add)
        {
            terminal.Info($"  All.sln   + {project}");
        }

        foreach (var project in plan.Membership.Remove)
        {
            terminal.Info($"  All.sln   - {project}");
        }
    }
}
