using AutVecu.Cores.Enums;

namespace AutVecu.Cores.Models;

public class TestRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime StartedAt { get; set; } = DateTime.Now;

    public DateTime? FinishedAt { get; set; }

    public TestStatus Status { get; set; } = TestStatus.Pending;

    public List<TestCaseResult> TestCases { get; set; } = [];

    public int TotalTests => TestCases.Count;

    public int PassedTests => TestCases.Count(testCase => testCase.Status == TestStatus.Passed);

    public int FailedTests => TestCases.Count(testCase => testCase.Status == TestStatus.Failed);
}
