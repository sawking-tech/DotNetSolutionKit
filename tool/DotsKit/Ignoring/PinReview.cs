namespace DotsKit.Ignoring;

/// <summary>
/// What the tool writes into <c>.dotskitignore</c> after an upgrade, for the pins that upgrade spent. A spent pin whose
/// files the upgrade brought to the template's own has done its work: its rule is commented out, never erased, so the
/// team sees what the tool took out. A spent pin whose files still differ is re-pinned to the version the upgrade went
/// to, so the team's change stands another round. Every line the tool did not touch stays as the team wrote it.
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

        var text = file.Text.Split('\n');
        List<string> report = [];
        foreach (var (rule, difference) in spent.OrderBy(s => s.Rule.Line))
        {
            var index = rule.Line - 1;
            if (index < 0 || index >= text.Length)
            {
                continue;
            }

            if (difference)
            {
                var pin = rule.Pin!.RenewedTo(to);
                text[index] = $"{rule.Pattern}@{pin}";
                report.Add($"{rule.Pattern}: spent by the upgrade to {to}, the files still differ - pinned on @{pin} again");
            }
            else
            {
                text[index] = $"# {rule.Pattern}@{rule.Pin!.RenewedTo(to)} # dotskit: the pin is spent by the upgrade to {to} and the files are the template's own; remove the line when the team agrees";
                report.Add($"{rule.Pattern}: spent by the upgrade to {to}, the files are the template's own - the rule is commented out");
            }
        }

        return (string.Join('\n', text), report);
    }
}
