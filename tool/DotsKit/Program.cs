using DotsKit;
using DotsKit.Commands;
using DotsKit.Generation;
using DotsKit.Io;
using DotsKit.Merging;
using DotsKit.Planning;
using DotsKit.Solutions;

// The composition root: the one place the tool's parts are put together.
var terminal = new ConsoleTerminal();
if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    terminal.Info(Options.Usage);
    return (int)ExitCode.Done;
}
if (args[0] is "--version" or "version")
{
    terminal.Info(ToolVersion.Current);
    return (int)ExitCode.Done;
}

var runner = new ProcessRunner();
var projects = new SolutionProjects(runner);
using var http = new HttpClient();
var staging = new Staging(new TemplateSource(runner, new TagArchive(http)), projects);
var planner = new Planner(staging, new ThreeWayMerge(new GitMergeFile(runner)), projects, new GitRenames(runner), new GitIgnoredFiles(runner));
var report = new PlanReport(terminal);
var git = new Git(runner);
var builder = new DotnetBuilder(runner);
var repair = new ReferenceRepair(new RepairWriter(runner), builder);
var self = SelfCommand.Of(Environment.ProcessPath, typeof(StyleMergeDriver).Assembly.Location);
var writer = new PlanWriter(projects, new GitTemplateMerge(runner, self), repair);
var execution = new PlanExecution(report, writer, builder, repair, terminal);

AppDomain.CurrentDomain.ProcessExit += (_, _) => Folders.RemoveRunFolders();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};
try
{
    // git runs the driver inside the merge of a command that holds the solution's lock: it takes none.
    if (args[0] == StyleMergeDriver.Command)
    {
        return await new StyleMergeDriver(new GitMergeFile(runner)).RunAsync(args[1..], cts.Token);
    }

    var options = Options.Parse(args[0], args[1..]);
    var folder = Folders.RealPath(Directory.GetCurrentDirectory());
    using var solutionLock = SolutionLock.TryTake(SolutionRoot.Find(folder) ?? folder)
        ?? throw new InvalidOperationException("Another dotskit is running in this solution: wait for it to finish, then run the command again.");
    var code = args[0] switch
    {
        "new" => await new NewCommand(planner, execution, git, ToolVersion.Current, TemplateSource.SourcesFolder is not null).RunAsync(options, folder, cts.Token),
        "upgrade" => await new UpgradeCommand(planner, execution, git, terminal, ToolVersion.Current,
            TemplateSource.SourcesFolder is not null).RunAsync(options, folder, cts.Token),
        "init" => await new InitCommand(staging, projects, terminal, ToolVersion.Current, TemplateSource.SourcesFolder is not null).RunAsync(options, folder, cts.Token),
        _ => throw new ArgumentException($"Unknown command {args[0]}.\n{Options.Usage}"),
    };
    return (int)code;
}
// An expected failure is one line that says what to do; a stack trace is for a bug.
// A cancel or a failure of files reaches here before anything is written or after the write was put back;
// a cancel of the build after the write is the command's own.
catch (OperationCanceledException)
{
    terminal.Error("Cancelled; nothing was written.");
    return (int)ExitCode.Failed;
}
catch (Exception e) when (e is IOException or UnauthorizedAccessException)
{
    terminal.Error($"{e.Message} Nothing was written: close what holds the file, or make it writable, and run again.");
    return (int)ExitCode.Failed;
}
catch (System.ComponentModel.Win32Exception e)
{
    terminal.Error($"{e.Message}: dotskit runs dotnet and git, put both on PATH.");
    return (int)ExitCode.Failed;
}
catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
{
    terminal.Error(e is System.Text.Json.JsonException ? $"{Layout.ManifestPath} cannot be read: {e.Message}" : e.Message);
    return (int)ExitCode.Failed;
}
