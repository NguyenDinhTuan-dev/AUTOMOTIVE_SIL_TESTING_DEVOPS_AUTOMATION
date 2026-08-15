namespace AutVecu.Cores.Models;

public class TestCaseDefinition
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Request { get; set; } = string.Empty;

    public string ExpectedPrefix { get; set; } = string.Empty;

    public string ExpectedContains { get; set; } = string.Empty;

    public string MockResponse { get; set; } = string.Empty;

    public List<TestStepDefinition> Steps { get; set; } = [];
}
