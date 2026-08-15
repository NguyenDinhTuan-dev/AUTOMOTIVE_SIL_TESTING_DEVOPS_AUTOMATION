using AutVecu.Services;
using AutVecu.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var inputPath = args.Length > 0 ? args[0] : string.Empty;
var outputFolderPath = args.Length > 1 ? args[1] : string.Empty;

if (string.IsNullOrWhiteSpace(inputPath))
{
    Console.Error.WriteLine("Usage: AutVecu.CliRunner <tcl-script-folder|test-cases-json> [generated-output-folder]");
    return 2;
}

using var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddAutVecuServices();
    })
    .Build();

await host.StartAsync();

var scriptFolderPath = inputPath;
var resolvedJsonPath = ResolveJsonInputPath(inputPath);

if (!string.IsNullOrWhiteSpace(resolvedJsonPath))
{
    var generator = host.Services.GetRequiredService<ITclScriptGeneratorService>();
    scriptFolderPath = await generator.GenerateFromJsonAsync(
        resolvedJsonPath,
        string.IsNullOrWhiteSpace(outputFolderPath) ? null : outputFolderPath);

    Console.WriteLine($"Generated TCL scripts: {scriptFolderPath}");
}

var testRunner = host.Services.GetRequiredService<ITestRunnerService>();
var testRun = await testRunner.RunAsync(scriptFolderPath);

Console.WriteLine($"Test Run: {testRun.Id}");
Console.WriteLine($"Status: {testRun.Status}");
Console.WriteLine($"Passed: {testRun.PassedTests}/{testRun.TotalTests}");

foreach (var testCase in testRun.TestCases)
{
    Console.WriteLine($"{testCase.Status}: {testCase.Name} - {testCase.Message}");
}

await host.StopAsync();

return testRun.Status switch
{
    AutVecu.Cores.Enums.TestStatus.Passed => 0,
    AutVecu.Cores.Enums.TestStatus.Failed => 1,
    AutVecu.Cores.Enums.TestStatus.Error => 2,
    AutVecu.Cores.Enums.TestStatus.Stopped => 3,
    _ => 4
};

static string ResolveJsonInputPath(string inputPath)
{
    if (File.Exists(inputPath) &&
        string.Equals(Path.GetExtension(inputPath), ".json", StringComparison.OrdinalIgnoreCase))
    {
        return inputPath;
    }

    if (!Directory.Exists(inputPath))
    {
        return string.Empty;
    }

    var defaultJsonPath = Path.Combine(inputPath, "test_cases.json");
    if (File.Exists(defaultJsonPath))
    {
        return defaultJsonPath;
    }

    return Directory
        .EnumerateFiles(inputPath, "*.json", SearchOption.AllDirectories)
        .Where(filePath => !string.Equals(Path.GetFileName(filePath), "test_env.json", StringComparison.OrdinalIgnoreCase))
        .Where(filePath => !filePath.Contains($"{Path.DirectorySeparatorChar}generated{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        .OrderBy(filePath => filePath, StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault() ?? string.Empty;
}
