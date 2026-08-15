using AutVecu.Cores.Enums;

namespace AutVecu.Cores.DTOs;

public class TestCaseResultDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public TestStatus Status { get; set; }

    public string Message { get; set; } = string.Empty;
}
