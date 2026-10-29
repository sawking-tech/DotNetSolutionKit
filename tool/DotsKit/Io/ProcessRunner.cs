using System.Diagnostics;

namespace DotsKit.Io;

internal sealed record ProcessResult(int Code, string Output, string Error);

/// <summary>Runs a program; the one door to the outside processes the tool calls - dotnet and git.</summary>
internal interface IProcessRunner
{
    /// <param name="environment">Variables set for this run only, such as git's GIT_INDEX_FILE.</param>
    Task<ProcessResult> RunAsync(string program, IReadOnlyList<string> args, string workingDirectory, CancellationToken ct,
        IReadOnlyDictionary<string, string>? environment = null);
}

internal static class ProcessRunnerExtensions
{
    /// <summary>Runs a program that must succeed, and returns what it printed.</summary>
    public static async Task<string> RunCheckedAsync(this IProcessRunner runner, string program, IReadOnlyList<string> args,
        string workingDirectory, CancellationToken ct, IReadOnlyDictionary<string, string>? environment = null)
    {
        var result = await runner.RunAsync(program, args, workingDirectory, ct, environment);
        if (result.Code != 0)
            throw new InvalidOperationException($"{program} {string.Join(' ', args)} exited with {result.Code}:\n{Why(result)}");
        return result.Output;
    }

    /// <summary>What a failed process said: its errors, or its output when it wrote its errors there, as dotnet build does.</summary>
    private static string Why(ProcessResult result) =>
        (string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error).Trim();
}

internal sealed class ProcessRunner : IProcessRunner
{
    /// <summary>dotnet prints in the language of the machine; the tool keeps the messages it passes on in English.</summary>
    private const string UiLanguage = "en";

    private const string TraceVariable = "DOTSKIT_TRACE";

    /// <summary>The first arguments say which call it is; the rest is lists of paths.</summary>
    private const int TracedArgs = 4;

    private static readonly System.Text.Encoding Utf8 = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public async Task<ProcessResult> RunAsync(string program, IReadOnlyList<string> args, string workingDirectory, CancellationToken ct,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo(program)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // git prints a file's text as its bytes, UTF-8; read by the console's code page (866 on a Russian
            // Windows) every non-ASCII character of a merged file would be written back broken.
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = UiLanguage;
        // MSBuild nodes and the build server kept alive for reuse hold the redirected output open, and reading it
        // to its end would never finish.
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
            start.Environment[name] = value;

        var clock = Stopwatch.StartNew();
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {program}");
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // A dotnet sln left running would go on writing All.sln after the transaction has put it back.
            process.Kill(entireProcessTree: true);
            throw;
        }
        var result = new ProcessResult(process.ExitCode, await output, await error);
        Trace(program, args, clock.Elapsed);
        return result;
    }

    /// <summary>
    /// With DOTSKIT_TRACE set, every process and its time go to stderr: what a slow command spends its time on.
    /// </summary>
    private static void Trace(string program, IReadOnlyList<string> args, TimeSpan took)
    {
        if (Environment.GetEnvironmentVariable(TraceVariable) is { Length: > 0 })
            Console.Error.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"dotskit trace {took.TotalSeconds,6:F1}s {program} {string.Join(' ', args.Take(TracedArgs))}"));
    }
}
