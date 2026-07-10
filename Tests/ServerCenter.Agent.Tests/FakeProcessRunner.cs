using ServerCenter.Agent.Linux;

namespace ServerCenter.Agent.Tests;

// Records each invocation (binary, args, env) and answers with a scripted result. Respond can branch
// on the binary and args, so a multi-step provider (apt-get update -> apt list, or manifest fetch ->
// dpkg) can be driven deterministically. `Calls` keeps the args-only view the service-control tests
// assert on.
internal sealed class FakeProcessRunner : IProcessRunner
{
    public List<IReadOnlyList<string>> Calls { get; } = [];

    public List<Invocation> Invocations { get; } = [];

    public Func<string, IReadOnlyList<string>, ProcessResult> Respond { get; set; } =
        (_, _) => new ProcessResult(0, string.Empty, string.Empty);

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct) =>
        RunAsync(fileName, arguments, new Dictionary<string, string>(), ct);

    public Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken ct)
    {
        Calls.Add(arguments);
        Invocations.Add(new Invocation(fileName, arguments, environment));
        return Task.FromResult(Respond(fileName, arguments));
    }

    internal sealed record Invocation(string File, IReadOnlyList<string> Args, IReadOnlyDictionary<string, string> Env);
}
