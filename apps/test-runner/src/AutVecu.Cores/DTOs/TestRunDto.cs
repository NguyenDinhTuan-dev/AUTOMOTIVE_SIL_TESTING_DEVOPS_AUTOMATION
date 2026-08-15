using AutVecu.Cores.Enums;

namespace AutVecu.Cores.DTOs;

public class TestRunDto
{
    public Guid Id { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public TestStatus Status { get; set; }

    public int TotalTests { get; set; }

    public int PassedTests { get; set; }

    public int FailedTests { get; set; }
}
