using AutVecu.Cores.Enums;

namespace AutVecu.Cores.Models;

public class TestCaseResult
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public TestStatus Status { get; set; } = TestStatus.Pending;

    public DateTime StartedAt { get; set; } = DateTime.Now;

    public DateTime? FinishedAt { get; set; }

    public string Message { get; set; } = string.Empty;

    public string RawLog { get; set; } = string.Empty;
}
