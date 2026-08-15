using AutVecu.Cores.Models;

namespace AutVecu.Services.Interfaces;

public interface ITclScriptGeneratorService
{
    Task<string> GenerateFromJsonAsync(
        string jsonFilePath,
        string? outputFolderPath = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GenerateAsync(
        IReadOnlyList<TestCaseDefinition> testCases,
        string outputFolderPath,
        string socketClientPath,
        CancellationToken cancellationToken = default);
}
