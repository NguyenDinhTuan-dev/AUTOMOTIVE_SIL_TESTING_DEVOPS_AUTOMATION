namespace AutVecu.Cores.Models;

public class TestStepDefinition
{
    public string Name { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;

    public string Request { get; set; } = string.Empty;

    public string ExpectedPrefix { get; set; } = string.Empty;

    public string ExpectedContains { get; set; } = string.Empty;

    public string MockResponse { get; set; } = string.Empty;
}
