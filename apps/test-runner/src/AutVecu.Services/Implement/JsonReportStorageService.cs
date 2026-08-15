using System.Text.Json;
using AutVecu.Cores.Models;
using AutVecu.Services.Interfaces;

namespace AutVecu.Services.Implement;

public class JsonReportStorageService : IReportStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _testRunsDirectory;

    public JsonReportStorageService()
        : this(Path.Combine(AppContext.BaseDirectory, "data", "test-runs"))
    {
    }

    public JsonReportStorageService(string testRunsDirectory)
    {
        _testRunsDirectory = testRunsDirectory;
    }

    public async Task SaveAsync(TestRun testRun, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_testRunsDirectory);

        var fileName = $"run_{testRun.StartedAt:yyyyMMdd_HHmmss}_{testRun.Id:N}.json";
        var filePath = Path.Combine(_testRunsDirectory, fileName);
        var json = JsonSerializer.Serialize(testRun, JsonOptions);

        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    public async Task<IReadOnlyList<TestRun>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_testRunsDirectory))
        {
            return [];
        }

        var testRuns = new List<TestRun>();

        foreach (var filePath in Directory.EnumerateFiles(_testRunsDirectory, "*.json"))
        {
            var testRun = await ReadTestRunAsync(filePath, cancellationToken);
            if (testRun is not null)
            {
                testRuns.Add(testRun);
            }
        }

        return testRuns
            .OrderByDescending(testRun => testRun.StartedAt)
            .ToList();
    }

    public async Task<TestRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var testRuns = await GetAllAsync(cancellationToken);
        return testRuns.FirstOrDefault(testRun => testRun.Id == id);
    }

    private static async Task<TestRun?> ReadTestRunAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<TestRun>(stream, JsonOptions, cancellationToken);
    }
}
