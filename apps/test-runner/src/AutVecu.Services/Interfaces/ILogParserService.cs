using AutVecu.Cores.Models;

namespace AutVecu.Services.Interfaces;

public interface ILogParserService
{
    TestCaseResult ParseTestCaseResult(string testCaseName, string rawLog);

    IReadOnlyList<EcuLogEntry> ParseLogEntries(string rawLog);
}
