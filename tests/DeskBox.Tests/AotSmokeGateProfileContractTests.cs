namespace DeskBox.Tests;

/// <summary>
/// Drift canary for the AOT smoke runner audit gates (R2-P1-2). The smoke
/// runners used to hard-code "profile 59 / schema 55 / WMC1510 790" while
/// publish-aot-audit.ps1 moved on (profile 65 / 808), so every one of them
/// would have refused a perfectly valid audit summary. These contracts keep
/// the runners parameterized and their values synchronized with the audit
/// script that produces the summary.
/// </summary>
public sealed class AotSmokeGateProfileContractTests
{
    private static readonly string[] RunnerPaths =
    [
        "scripts/run-aot-file-properties-smoke.ps1",
        "scripts/run-aot-native-drop-smoke.ps1",
        "scripts/run-aot-hotkey-smoke.ps1",
        "scripts/run-aot-picker-clipboard-smoke.ps1",
        "scripts/run-aot-todo-notification-activation-smoke.ps1",
        "scripts/run-aot-todo-notification-smoke.ps1",
        "scripts/run-aot-todo-recurrence-reminder-smoke.ps1",
    ];

    [Fact]
    public void Runners_GateOnAuditSummary_ThroughRequiredProfileVariables()
    {
        foreach (string runnerPath in RunnerPaths)
        {
            string runner = Read(runnerPath);

            Assert.Contains(
                "$requiredAuditProfileVersion = ",
                runner,
                StringComparison.Ordinal);
            Assert.Contains(
                "-ne $requiredAuditProfileVersion -or",
                runner,
                StringComparison.Ordinal);
            Assert.DoesNotContain("-ne 59 -or", runner, StringComparison.Ordinal);
            Assert.DoesNotContain("-ne 790 -or", runner, StringComparison.Ordinal);
            Assert.DoesNotContain("profile 59 / schema 55", runner, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Runners_ProfileMatchesTheAuditScriptThatProducesTheSummary()
    {
        string auditScript = Read("scripts/publish-aot-audit.ps1");
        string auditProfile = ExtractSingleAssignment(auditScript, "$auditProfileVersion");

        foreach (string runnerPath in RunnerPaths)
        {
            string runner = Read(runnerPath);
            Assert.Equal(
                auditProfile,
                ExtractSingleAssignment(runner, "$requiredAuditProfileVersion"));
        }
    }

    [Fact]
    public void Runners_Wmc1510BaselineIsSharedAcrossAllGatedRunners()
    {
        string? shared = null;
        foreach (string runnerPath in RunnerPaths)
        {
            string runner = Read(runnerPath);
            if (!runner.Contains("$requiredWmc1510Count", StringComparison.Ordinal))
            {
                continue;
            }

            string baseline = ExtractSingleAssignment(runner, "$requiredWmc1510Count");
            shared ??= baseline;
            Assert.Equal(shared, baseline);
        }

        Assert.NotNull(shared);
    }

    /// <summary>
    /// Extracts the value of a single numeric assignment such as
    /// "$auditProfileVersion = 65" and fails the test on zero or multiple
    /// definitions, because two definitions are exactly how the 59/65 drift
    /// stayed invisible for seven runners.
    /// </summary>
    private static string ExtractSingleAssignment(string source, string variable)
    {
        string needle = variable + " = ";
        List<string> values = [];
        int index = 0;
        while ((index = source.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            int valueStart = index + needle.Length;
            int valueEnd = valueStart;
            while (valueEnd < source.Length &&
                (char.IsDigit(source[valueEnd]) || source[valueEnd] == '_'))
            {
                valueEnd++;
            }

            values.Add(source[valueStart..valueEnd]);
            index = valueEnd;
        }

        Assert.True(
            values.Count == 1,
            $"Expected exactly one definition of '{variable}', found {values.Count}: [{string.Join(", ", values)}]");
        return Assert.Single(values);
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(TestPaths.FromRepository(relativePath));
}
