using System.Text;
using System.Text.Json;
using AutVecu.Cores.Models;
using AutVecu.Services.Interfaces;

namespace AutVecu.Services.Implement;

public class TclScriptGeneratorService : ITclScriptGeneratorService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<string> GenerateFromJsonAsync(
        string jsonFilePath,
        string? outputFolderPath = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jsonFilePath) || !File.Exists(jsonFilePath))
        {
            throw new FileNotFoundException("Test case JSON file does not exist.", jsonFilePath);
        }

        var jsonDirectory = Path.GetDirectoryName(Path.GetFullPath(jsonFilePath))
            ?? Environment.CurrentDirectory;
        var testCases = await ReadTestCasesAsync(jsonFilePath, cancellationToken);
        var outputFolder = string.IsNullOrWhiteSpace(outputFolderPath)
            ? Path.Combine(jsonDirectory, "generated", "vtc_cases")
            : Path.GetFullPath(outputFolderPath);
        var socketClientPath = ResolveSocketClientPath(jsonDirectory);

        if (!File.Exists(socketClientPath))
        {
            throw new FileNotFoundException("TCL socket client library does not exist.", socketClientPath);
        }

        await GenerateAsync(testCases, outputFolder, socketClientPath, cancellationToken);
        return outputFolder;
    }

    public async Task<IReadOnlyList<string>> GenerateAsync(
        IReadOnlyList<TestCaseDefinition> testCases,
        string outputFolderPath,
        string socketClientPath,
        CancellationToken cancellationToken = default)
    {
        if (testCases.Count == 0)
        {
            throw new InvalidOperationException("No test cases were found in the input data.");
        }

        Directory.CreateDirectory(outputFolderPath);

        var generatedFiles = new List<string>();
        foreach (var testCase in testCases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Validate(testCase);

            var fileName = $"{SanitizeFilePart(testCase.Id)}_{SanitizeFilePart(testCase.Name)}.tcl";
            var filePath = Path.Combine(outputFolderPath, fileName);
            var content = BuildScript(testCase, socketClientPath);

            await File.WriteAllTextAsync(filePath, content, Encoding.UTF8, cancellationToken);
            generatedFiles.Add(filePath);
        }

        return generatedFiles;
    }

    private static async Task<IReadOnlyList<TestCaseDefinition>> ReadTestCasesAsync(
        string jsonFilePath,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(jsonFilePath);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return document.RootElement.Deserialize<List<TestCaseDefinition>>(JsonOptions) ?? [];
        }

        if (document.RootElement.TryGetProperty("testCases", out var testCasesElement) &&
            testCasesElement.ValueKind == JsonValueKind.Array)
        {
            return testCasesElement.Deserialize<List<TestCaseDefinition>>(JsonOptions) ?? [];
        }

        throw new InvalidOperationException("JSON input must be an array or an object with a testCases array.");
    }

    private static string ResolveSocketClientPath(string jsonDirectory)
    {
        var candidateDirectories = new[]
        {
            jsonDirectory,
            Path.GetDirectoryName(jsonDirectory) ?? string.Empty
        };

        foreach (var candidateDirectory in candidateDirectories.Where(directory => !string.IsNullOrWhiteSpace(directory)))
        {
            var socketClientPath = Path.Combine(candidateDirectory, "lib", "socket_client.tcl");
            if (File.Exists(socketClientPath))
            {
                return socketClientPath;
            }
        }

        return Path.Combine(jsonDirectory, "lib", "socket_client.tcl");
    }

    private static string BuildScript(TestCaseDefinition testCase, string socketClientPath)
    {
        var builder = new StringBuilder();
        var normalizedSocketClientPath = NormalizeTclPath(socketClientPath);
        var testName = TclEscape($"{testCase.Id}_{testCase.Name}".Trim('_'));
        var steps = NormalizeSteps(testCase);

        builder.AppendLine($"source [file normalize \"{normalizedSocketClientPath}\"]");
        builder.AppendLine();
        builder.AppendLine($"log_info \"Start {testName}\"");
        builder.AppendLine();

        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var stepName = string.IsNullOrWhiteSpace(step.Name)
                ? $"Step {index + 1}"
                : step.Name;
            var variablePrefix = $"step{index + 1}";

            builder.AppendLine($"log_info \"{TclEscape(stepName)}\"");
            if (IsCommand(step, "unlockSecurity"))
            {
                AppendSecurityUnlock(builder, stepName);
                builder.AppendLine($"log_pass \"{TclEscape(stepName)} passed.\"");
                builder.AppendLine();
                continue;
            }

            builder.AppendLine($"set {variablePrefix}Request \"{TclEscape(step.Request)}\"");
            builder.AppendLine($"set {variablePrefix}Response [send_uds_request ${variablePrefix}Request]");
            AppendAssertion(builder, step, variablePrefix, stepName);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static void AppendSecurityUnlock(StringBuilder builder, string stepName)
    {
        var escapedStepName = TclEscape(stepName);
        builder.AppendLine("set sessionResponse [send_uds_request \"10 03\"]");
        builder.AppendLine("set sessionClean [string map {\" \" \"\" \"\\t\" \"\" \"\\r\" \"\" \"\\n\" \"\"} $sessionResponse]");
        builder.AppendLine("if {![string match \"5003*\" $sessionClean]} {");
        builder.AppendLine($"    log_fail \"{escapedStepName} (Session Control 10 03) failed. Actual=$sessionResponse ExpectedPrefix=50 03\"");
        builder.AppendLine("    exit 1");
        builder.AppendLine("}");
        builder.AppendLine("set seedResponse [send_uds_request \"27 01\"]");
        builder.AppendLine("set seedClean [string map {\" \" \"\" \"\\t\" \"\" \"\\r\" \"\" \"\\n\" \"\"} $seedResponse]");
        builder.AppendLine("if {![string match \"6701*\" $seedClean] || [string length $seedClean] < 8} {");
        builder.AppendLine($"    log_fail \"{escapedStepName} failed. Actual=$seedResponse ExpectedPrefix=67 01\"");
        builder.AppendLine("    exit 1");
        builder.AppendLine("}");
        builder.AppendLine("scan [string range $seedClean 4 5] %x seedHigh");
        builder.AppendLine("scan [string range $seedClean 6 7] %x seedLow");
        builder.AppendLine("set seed [expr {(($seedHigh & 0xFF) << 8) | ($seedLow & 0xFF)}]");
        builder.AppendLine("set key [expr {(($seed ^ 0x5A5A) + 0x1234) & 0xFFFF}]");
        builder.AppendLine("set keyRequest [format \"27 02 %02X %02X\" [expr {($key >> 8) & 0xFF}] [expr {$key & 0xFF}]]");
        builder.AppendLine("set keyResponse [send_uds_request $keyRequest]");
        builder.AppendLine("if {![string match \"67 02*\" $keyResponse]} {");
        builder.AppendLine($"    log_fail \"{escapedStepName} failed. Actual=$keyResponse ExpectedPrefix=67 02\"");
        builder.AppendLine("    exit 1");
        builder.AppendLine("}");
    }
    private static void AppendAssertion(
        StringBuilder builder,
        TestStepDefinition step,
        string variablePrefix,
        string stepName)
    {
        if (!string.IsNullOrWhiteSpace(step.ExpectedContains))
        {
            builder.AppendLine($"if {{[string first \"{TclEscape(step.ExpectedContains)}\" ${variablePrefix}Response] >= 0}} {{");
            builder.AppendLine($"    log_pass \"{TclEscape(stepName)} passed.\"");
            builder.AppendLine("} else {");
            builder.AppendLine($"    log_fail \"{TclEscape(stepName)} failed. Actual=${variablePrefix}Response ExpectedContains={TclEscape(step.ExpectedContains)}\"");
            builder.AppendLine("}");
            return;
        }

        builder.AppendLine(
            $"assert_prefix ${variablePrefix}Response \"{TclEscape(step.ExpectedPrefix)}\" \"{TclEscape(stepName)} passed.\" \"{TclEscape(stepName)} failed.\"");
    }

    private static List<TestStepDefinition> NormalizeSteps(TestCaseDefinition testCase)
    {
        if (testCase.Steps.Count > 0)
        {
            return testCase.Steps;
        }

        return
        [
            new TestStepDefinition
            {
                Name = testCase.Name,
                Request = testCase.Request,
                ExpectedPrefix = testCase.ExpectedPrefix,
                ExpectedContains = testCase.ExpectedContains,
                MockResponse = testCase.MockResponse
            }
        ];
    }

    private static void Validate(TestCaseDefinition testCase)
    {
        if (string.IsNullOrWhiteSpace(testCase.Id))
        {
            throw new InvalidOperationException("Test case id is required.");
        }

        if (string.IsNullOrWhiteSpace(testCase.Name))
        {
            throw new InvalidOperationException($"Test case name is required for {testCase.Id}.");
        }

        foreach (var step in NormalizeSteps(testCase))
        {
            if (IsCommand(step, "unlockSecurity"))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(step.Request))
            {
                throw new InvalidOperationException($"Request is required for test case {testCase.Id}.");
            }

            if (string.IsNullOrWhiteSpace(step.ExpectedPrefix) &&
                string.IsNullOrWhiteSpace(step.ExpectedContains))
            {
                throw new InvalidOperationException(
                    $"ExpectedPrefix or ExpectedContains is required for test case {testCase.Id}.");
            }
        }
    }

    private static bool IsCommand(TestStepDefinition step, string command)
    {
        return string.Equals(step.Command, command, StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeFilePart(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Where(character => !invalidCharacters.Contains(character))
            .Select(character => char.IsWhiteSpace(character) ? '_' : character)
            .ToArray());

        return string.IsNullOrWhiteSpace(sanitized) ? "TestCase" : sanitized;
    }

    private static string NormalizeTclPath(string path)
    {
        return Path.GetFullPath(path).Replace('\\', '/');
    }

    private static string TclEscape(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}

