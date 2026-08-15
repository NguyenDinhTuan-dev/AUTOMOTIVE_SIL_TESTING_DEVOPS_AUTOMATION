using AutVecu.Cores.Diagnostics;
using AutVecu.Cores.Enums;
using AutVecu.Cores.Models;
using AutVecu.Services.Interfaces;
using System.Text.RegularExpressions;

namespace AutVecu.Services.Implement;

public class LogParserService : ILogParserService
{
    private const byte DoIpAddressByteOftenMistakenAsService = 0x0E;

    public TestCaseResult ParseTestCaseResult(string testCaseName, string rawLog)
    {
        var status = ResolveStatus(rawLog);
        var now = DateTime.Now;

        return new TestCaseResult
        {
            Name = testCaseName,
            Status = status,
            StartedAt = now,
            FinishedAt = now,
            Message = ResolveMessage(status, rawLog),
            RawLog = rawLog
        };
    }

    public IReadOnlyList<EcuLogEntry> ParseLogEntries(string rawLog)
    {
        if (string.IsNullOrWhiteSpace(rawLog))
        {
            return [];
        }

        return rawLog
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => new EcuLogEntry
            {
                Source = "TestRunner",
                Level = ResolveLogLevel(line),
                Message = line
            })
            .ToList();
    }

    private static TestStatus ResolveStatus(string rawLog)
    {
        if (rawLog.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
        {
            return TestStatus.Error;
        }

        if (rawLog.Contains("FAIL", StringComparison.OrdinalIgnoreCase))
        {
            return TestStatus.Failed;
        }

        if (rawLog.Contains("PASS", StringComparison.OrdinalIgnoreCase))
        {
            return TestStatus.Passed;
        }

        return TestStatus.Pending;
    }

    private static EcuLogLevel ResolveLogLevel(string line)
    {
        if (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("FAIL", StringComparison.OrdinalIgnoreCase))
        {
            return EcuLogLevel.Error;
        }

        if (line.Contains("WARN", StringComparison.OrdinalIgnoreCase))
        {
            return EcuLogLevel.Warning;
        }

        if (line.Contains("DEBUG", StringComparison.OrdinalIgnoreCase))
        {
            return EcuLogLevel.Debug;
        }

        return EcuLogLevel.Information;
    }

    private static string ResolveMessage(TestStatus status, string rawLog)
    {
        return status switch
        {
            TestStatus.Passed => "Test case passed.",
            TestStatus.Failed => ResolveFailureMessage(rawLog) ?? "Test case failed.",
            TestStatus.Error => ResolveReason(rawLog, "ERROR") ?? "Test case error.",
            _ => string.IsNullOrWhiteSpace(rawLog) ? "No log output." : "Test case result is unknown."
        };
    }

    private static string? ResolveFailureMessage(string rawLog)
    {
        var reason = ResolveReason(rawLog, "FAIL");
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        var actual = MatchValue(reason, "Actual");
        var expectedPrefix = MatchValue(reason, "ExpectedPrefix");
        var expectedContains = MatchValue(reason, "ExpectedContains");

        if (string.IsNullOrWhiteSpace(actual))
        {
            return reason;
        }

        var expected = !string.IsNullOrWhiteSpace(expectedPrefix)
            ? expectedPrefix
            : expectedContains;
        var negativeResponseExplanation = ExplainNegativeResponse(actual);

        if (!string.IsNullOrWhiteSpace(negativeResponseExplanation))
        {
            return negativeResponseExplanation;
        }

        return string.IsNullOrWhiteSpace(expected)
            ? "Actual response did not satisfy the assertion."
            : "Actual response did not match expected response.";
    }

    private static string? MatchValue(string message, string key)
    {
        var match = Regex.Match(
            message,
            $@"\b{Regex.Escape(key)}=([0-9A-Fa-f ]+|[A-Za-z0-9_]+)",
            RegexOptions.CultureInvariant);

        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string? ExplainNegativeResponse(string actual)
    {
        if (!UdsProtocol.TryParseHexPayload(actual, out var bytes) ||
            !UdsProtocol.IsNegativeResponse(bytes))
        {
            return null;
        }

        var rejectedService = bytes[1];
        var nrc = bytes[2];
        var hint = rejectedService == DoIpAddressByteOftenMistakenAsService
            ? " Hint: vECU appears to parse 0x0E from the DoIP address as the UDS service; check whether the vECU expects UDS-only payload or strips DoIP source/target addresses correctly."
            : string.Empty;

        return $"vECU returned UDS negative response: service 0x{rejectedService:X2} ({UdsProtocol.GetServiceName(rejectedService)}), NRC 0x{nrc:X2} ({UdsProtocol.GetNegativeResponseCodeName(nrc)}).{hint}";
    }

    private static string? ResolveReason(string rawLog, string token)
    {
        if (string.IsNullOrWhiteSpace(rawLog))
        {
            return null;
        }

        var reason = rawLog
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => line.StartsWith(token, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        var message = reason[token.Length..].Trim();
        return string.IsNullOrWhiteSpace(message) ? reason : message;
    }
}
