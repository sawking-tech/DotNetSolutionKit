namespace DotsKit.Ignoring;

/// <summary>
/// What the tool writes into <c>.dotskitignore</c> after an upgrade, for the pins that upgrade spent. A spent pin whose
/// files the upgrade brought to the template's own has done its work: its rule is commented out, never erased, so the
/// team sees what the tool took out. A spent pin whose files still differ is re-pinned to the version the upgrade went
/// to, so the team's change stands another round. A pin of the whole major is the exception: at a new major the tool
/// comments the old line out and writes the next one commented out of its own, for the team to confirm. Every line the
/// tool did not touch stays as the team wrote it.
/// </summary>
internal static class PinReview
{
    /// <summary>The file's text with the spent pins taken out or re-pinned, and one line per rule for the report.</summary>
    /// <param name="file">The file as read.</param>
    /// <param name="spent">The rules the upgrade spent, each with whether the upgrade left a difference in its files.</param>
    /// <param name="to">The release the upgrade went to, which a live pin is re-pinned to.</param>
    public static (string Text, IReadOnlyList<string> Lines) Of(DotskitIgnore file, IReadOnlyList<(IgnoreRule Rule, bool Difference)> spent, string to)
    {
        if (spent.Count == 0)
        {
            return (file.Text, []);
        }

        var lines = file.Text.Split('\n');
        var byLine = spent.ToDictionary(s => s.Rule.Line);
        List<string> written = [];
        List<string> report = [];
        for (var i = 0; i < lines.Length; i++)
        {
            if (!byLine.TryGetValue(i + 1, out var at))
            {
                written.Add(lines[i]);
                continue;
            }

            var (rule, difference) = at;
            var pin = rule.Pin!.RenewedTo(to);
            if (difference && !rule.Pin!.EveryMajor)
            {
                // The team's change stands: the rule is re-pinned to the version the upgrade went to, and keeps the path.
                written.Add($"{rule.Pattern}@{pin}");
                report.Add($"{rule.Pattern}: spent by the upgrade to {to}, the files still differ - pinned on @{pin} again");
            }
            else if (rule.Pin!.EveryMajor)
            {
                // ADR-009: at a new major the tool moves the pin itself - the old line commented out with the reason, the
                // next one written commented out of its own, so a rule that held for good does not come back unread.
                written.Add($"# {rule.Pattern}@{pin} # dotskit: spent by the upgrade to {to}; uncomment the line below to hold again");
                written.Add($"# {rule.Pattern}@{pin}");
                report.Add($"{rule.Pattern}: spent by the upgrade to {to}, the rule is commented out and @{pin} is written below it for the team");
            }
            else
            {
                written.Add($"# {rule.Pattern}@{pin} # dotskit: the pin is spent by the upgrade to {to} and the files are the template's own; remove the line when the team agrees");
                report.Add($"{rule.Pattern}: spent by the upgrade to {to}, the files are the template's own - the rule is commented out");
            }
        }

        return (string.Join('\n', written), report);
    }
}
