using WindowsSystemGuardian.Contracts.Remediation;
using WindowsSystemGuardian.Core.Remediation;

namespace WindowsSystemGuardian.Core.Tests;

public sealed class ElevatedHostLaunchSpecTests
{
    private static readonly Guid RequestId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private const string OutputPath = @"C:\Users\test\AppData\Local\WindowsSystemGuardian\Data\Remediation\Results\outcome.json";

    [Fact]
    public void BuildArguments_ProducesQuotedAndOrderedGrammar()
    {
        var args = ElevatedHostLaunchSpec.BuildArguments(
            RemediationActionId.ComponentStoreRepair, RequestId, OutputPath);

        Assert.Contains("--command ComponentStoreRepair", args, StringComparison.Ordinal);
        Assert.Contains($"--request {RequestId:D}", args, StringComparison.Ordinal);
        Assert.Contains($"--out \"{OutputPath}\"", args, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RoundTrip_WithLauncherGrammar()
    {
        var args = ElevatedHostLaunchSpec.BuildArguments(
            RemediationActionId.SystemFileRepair, RequestId, OutputPath);

        var parsed = ParseTokens(args);

        Assert.True(parsed.ok);
        Assert.Equal(RemediationActionId.SystemFileRepair, parsed.invocation!.Action);
        Assert.Equal(RequestId, parsed.invocation.RequestId);
        Assert.Equal(OutputPath, parsed.invocation.OutputFilePath);
    }

    [Fact]
    public void Parse_RejectsNoneAndNumericActions()
    {
        Assert.False(TryParseRaw("--command None --request 11111111-2222-3333-4444-555555555555 --out C:\\x.json", out _));
        Assert.False(TryParseRaw("--command 999 --request 11111111-2222-3333-4444-555555555555 --out C:\\x.json", out _));
        Assert.False(TryParseRaw("--command ComponentStoreRepaired --request 11111111-2222-3333-4444-555555555555 --out C:\\x.json", out _));
    }

    [Fact]
    public void Parse_RejectsUnknownOrDuplicatedFlags()
    {
        Assert.False(TryParseRaw("--command SystemFileRepair --command SystemFileRepair --request 11111111-2222-3333-4444-555555555555 --out C:\\x.json", out _));
        Assert.False(TryParseRaw("--exec cmd.exe --request 11111111-2222-3333-4444-555555555555 --out C:\\x.json", out _));
    }

    [Fact]
    public void Parse_RejectsMissingArguments()
    {
        Assert.False(TryParseRaw(SplitTokens("--command SystemFileRepair --request 11111111-2222-3333-4444-555555555555"), out _));
        Assert.False(TryParseRaw(Array.Empty<string>(), out _));
    }

    [Fact]
    public void Parse_RejectsRelativeOutputPath()
    {
        Assert.False(TryParseRaw(
            "--command SystemFileRepair --request 11111111-2222-3333-4444-555555555555 --out just\\relative.json",
            out _));
    }

    [Fact]
    public void Parse_RejectsInvalidGuid()
    {
        Assert.False(TryParseRaw(
            "--command SystemFileRepair --request no-es-guid --out C:\\x.json",
            out _));
    }

    [Fact]
    public void BuildArguments_RejectsUnsupportedActionAndEmptyGuid()
    {
        Assert.Throws<InvalidOperationException>(
            () => ElevatedHostLaunchSpec.BuildArguments(RemediationActionId.None, RequestId, OutputPath));
        Assert.Throws<InvalidOperationException>(
            () => ElevatedHostLaunchSpec.BuildArguments(
                RemediationActionId.SystemFileRepair, Guid.Empty, OutputPath));
    }

    private static (bool ok, ElevatedHostInvocation? invocation) ParseTokens(string arguments)
    {
        var tokens = SplitTokens(arguments);
        var ok = ElevatedHostLaunchSpec.TryParse(tokens, out var invocation, out _);
        return (ok, invocation);
    }

    private static bool TryParseRaw(string arguments, out ElevatedHostInvocation? invocation) =>
        TryParseRaw(SplitTokens(arguments), out invocation);

    private static bool TryParseRaw(string[] tokens, out ElevatedHostInvocation? invocation)
    {
        var ok = ElevatedHostLaunchSpec.TryParse(tokens, out var parsed, out _);
        invocation = ok ? parsed : null;
        return ok;
    }

    /// <summary>Tokeniza como lo haría la línea de comandos de Windows (respetando comillas).</summary>
    private static string[] SplitTokens(string arguments)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var insideQuotes = false;

        foreach (var ch in arguments)
        {
            switch (ch)
            {
                case '"':
                    insideQuotes = !insideQuotes;
                    break;
                case ' ' when !insideQuotes:
                    if (current.Length > 0)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                    }
                    break;
                default:
                    current.Append(ch);
                    break;
            }
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());
        return [.. tokens];
    }
}
