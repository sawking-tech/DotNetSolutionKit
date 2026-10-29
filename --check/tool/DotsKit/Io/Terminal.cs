namespace DotsKit.Io;

/// <summary>What the person running the tool sees and answers.</summary>
internal interface ITerminal
{
    void Info(string line);

    void Error(string line);

    /// <summary>Asks a yes-or-no question; without someone to answer it, the answer is no.</summary>
    bool Confirm(string question);
}

internal sealed class ConsoleTerminal : ITerminal
{
    public void Info(string line) => Console.WriteLine(line);

    public void Error(string line) => Console.Error.WriteLine(line);

    public bool Confirm(string question)
    {
        // CI or a pipe has no one to answer: --yes is how to say yes there.
        if (Console.IsInputRedirected)
        {
            return false;
        }

        Console.Write($"{question} [y/N] ");
        return Console.ReadLine()?.Trim().ToLowerInvariant() is "y" or "yes";
    }
}
