using System.Collections.ObjectModel;
using System.IO;
using System.Net.Sockets;
using AutVecu.Cores.Diagnostics;
using AutVecu.Cores.Enums;
using AutVecu.Cores.Models;
using AutVecu.Services.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinForms = System.Windows.Forms;

namespace AutVecu.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ITestRunnerService _testRunnerService;
    private readonly IReportStorageService _reportStorageService;
    private readonly ITclScriptGeneratorService _tclScriptGeneratorService;

    [ObservableProperty]
    private string scriptFolderPath = string.Empty;

    [ObservableProperty]
    private string vecuHost = "127.0.0.1";

    [ObservableProperty]
    private string vecuPort = "13400";

    [ObservableProperty]
    private string doipSourceAddress = "0x0E00";

    [ObservableProperty]
    private string doipTargetAddress = "0x0E80";

    [ObservableProperty]
    private string serverStatus = "Stopped";

    [ObservableProperty]
    private string generatedScriptFolderPath = string.Empty;

    [ObservableProperty]
    private string selectedTestMode = "Custom";

    [ObservableProperty]
    private string currentStatus = "Ready";

    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private TestRun? latestTestRun;

    public ObservableCollection<TestCaseResult> TestResults { get; } = [];

    public ObservableCollection<TrafficEntry> TrafficEntries { get; } = [];

    public ObservableCollection<string> Logs { get; } = [];

    public ObservableCollection<string> TestModes { get; } =
    [
        "Regression",
        "Simulink Integration",
        "Custom"
    ];

    public MainViewModel(
        ITestRunnerService testRunnerService,
        IReportStorageService reportStorageService,
        ITclScriptGeneratorService tclScriptGeneratorService)
    {
        _testRunnerService = testRunnerService;
        _reportStorageService = reportStorageService;
        _tclScriptGeneratorService = tclScriptGeneratorService;
    
        var configuredInputPath = Environment.GetEnvironmentVariable("AUT_VECU_TEST_INPUT");
        if (!string.IsNullOrWhiteSpace(configuredInputPath))
        {
            ScriptFolderPath = configuredInputPath;
            SelectedTestMode = "Custom";
        }
        else
        {
            SelectedTestMode = "Regression";
        }
    }


    partial void OnSelectedTestModeChanged(string value)
    {
        if (string.Equals(value, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var modePath = ResolveModeInputPath(value);
        if (!string.IsNullOrWhiteSpace(modePath))
        {
            ScriptFolderPath = modePath;
            GeneratedScriptFolderPath = string.Empty;
            AddLog($"Selected test mode: {value}. Input={modePath}");
        }
    }

    private static string ResolveModeInputPath(string mode)
    {
        var testScriptsRoot = ResolveTestScriptsRoot();
        if (string.IsNullOrWhiteSpace(testScriptsRoot))
        {
            return string.Empty;
        }

        return mode switch
        {
            "Regression" => Path.Combine(testScriptsRoot, "vecu_full_regression"),
            "Simulink Integration" => Path.Combine(testScriptsRoot, "simulink_integration"),
            _ => string.Empty
        };
    }

    private static string ResolveTestScriptsRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "test-scripts");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        var workspaceCandidate = Path.Combine("c:\\Users\\Admin\\Downloads\\vECU_Automated_Testing_Framework", "test-scripts");
        return Directory.Exists(workspaceCandidate) ? workspaceCandidate : string.Empty;
    }
    [RelayCommand(CanExecute = nameof(CanRunTests))]
    private async Task RunTestsAsync()
    {
        IsRunning = true;
        ApplyVecuEnvironment();
        CurrentStatus = "Running tests...";
        TestResults.Clear();
        TrafficEntries.Clear();
        Logs.Clear();
        AddLog("Starting TCL test run.");

        try
        {
            var runnerInputPath = !string.IsNullOrWhiteSpace(GeneratedScriptFolderPath) &&
                Directory.Exists(GeneratedScriptFolderPath)
                    ? GeneratedScriptFolderPath
                    : ResolveRunnerInputPath(ScriptFolderPath);
            if (string.IsNullOrWhiteSpace(runnerInputPath))
            {
                CurrentStatus = "Select test input before running.";
                AddLog("ERROR: Select a test-scripts folder, a generated TCL folder, or set AUT_VECU_TEST_INPUT.");
                return;
            }
            if (IsJsonFile(runnerInputPath))
            {
                runnerInputPath = await GenerateTclAsync(runnerInputPath);
            }

            var testRun = await _testRunnerService.RunAsync(runnerInputPath);
            LatestTestRun = testRun;

            foreach (var testCase in testRun.TestCases)
            {
                TestResults.Add(testCase);
                AddTrafficEntries(testCase.RawLog);
                AddTestCaseLog(testCase);
            }

            CurrentStatus = $"Finished: {testRun.Status} ({testRun.PassedTests}/{testRun.TotalTests} passed)";
            AddLog($"Report saved. Test run id: {testRun.Id}");
        }
        catch (Exception ex)
        {
            CurrentStatus = "Error";
            AddLog($"ERROR: {ex.Message}");
        }
        finally
        {
            IsRunning = false;
            RunTestsCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunTests))]
    private async Task GenerateTclScriptsAsync()
    {
        Logs.Clear();
        ApplyVecuEnvironment();

        try
        {
            var inputPath = ResolveRunnerInputPath(ScriptFolderPath);
            if (!IsJsonFile(inputPath))
            {
                CurrentStatus = "Select a JSON file or a folder containing JSON.";
                AddLog("ERROR: No JSON input was found.");
                return;
            }

            GeneratedScriptFolderPath = await GenerateTclAsync(inputPath);
            CurrentStatus = $"Generated TCL: {GeneratedScriptFolderPath}";
        }
        catch (Exception ex)
        {
            CurrentStatus = "Generate TCL failed.";
            AddLog($"ERROR: {ex.Message}");
        }
    }

    private async Task<string> GenerateTclAsync(string jsonFilePath)
    {
        AddLog($"Generating TCL scripts from JSON: {jsonFilePath}");
        var generatedFolder = await _tclScriptGeneratorService.GenerateFromJsonAsync(jsonFilePath);
        GeneratedScriptFolderPath = generatedFolder;
        AddLog($"Generated TCL script folder: {generatedFolder}");
        return generatedFolder;
    }

    private string ResolveRunnerInputPath(string inputPath)
    {
        
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return Environment.GetEnvironmentVariable("AUT_VECU_TEST_INPUT") ?? string.Empty;
        }

        if (IsJsonFile(inputPath))
        {
            return inputPath;
        }

        if (!Directory.Exists(inputPath))
        {
            return inputPath;
        }

        var defaultJsonPath = Path.Combine(inputPath, "test_cases.json");
        if (File.Exists(defaultJsonPath))
        {
            AddLog($"Found JSON input: {defaultJsonPath}");
            return defaultJsonPath;
        }

        var jsonFiles = Directory
            .EnumerateFiles(inputPath, "*.json", SearchOption.AllDirectories)
            .Where(filePath => !string.Equals(Path.GetFileName(filePath), "test_env.json", StringComparison.OrdinalIgnoreCase))
            .Where(filePath => !filePath.Contains($"{Path.DirectorySeparatorChar}generated{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(filePath => filePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (jsonFiles.Count > 0)
        {
            AddLog($"Found JSON input: {jsonFiles[0]}");
            return jsonFiles[0];
        }

        return inputPath;
    }

    private static bool IsJsonFile(string inputPath)
    {
        return File.Exists(inputPath) &&
            string.Equals(Path.GetExtension(inputPath), ".json", StringComparison.OrdinalIgnoreCase);
    }

    private bool CanRunTests()
    {
        return !IsRunning;
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        await _testRunnerService.StopAsync();
        AddLog("Stop requested.");
    }

    private bool CanStop()
    {
        return IsRunning;
    }

    [RelayCommand]
    private async Task CheckVecuConnectionAsync()
    {
        ApplyVecuEnvironment();
        ServerStatus = "Checking";
        CurrentStatus = $"Checking vECU connection: {VecuHost}:{VecuPort}";

        try
        {
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(VecuHost, int.Parse(VecuPort), timeout.Token);

            ServerStatus = "Reachable";
            CurrentStatus = $"vECU reachable: {VecuHost}:{VecuPort}";
            AddLog($"vECU reachable: {VecuHost}:{VecuPort}");
        }
        catch (Exception ex)
        {
            ServerStatus = "Unreachable";
            CurrentStatus = "vECU connection failed.";
            AddLog($"vECU connection failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ClearVecuTarget()
    {
        ServerStatus = "Stopped";
        CurrentStatus = "vECU target cleared.";
        AddLog("vECU target cleared.");
    }


    private void ApplyVecuEnvironment()
    {
        Environment.SetEnvironmentVariable("VECU_HOST", VecuHost);
        Environment.SetEnvironmentVariable("VECU_PORT", VecuPort);
        Environment.SetEnvironmentVariable("VECU_TRANSPORT", "doip");
        Environment.SetEnvironmentVariable("VECU_DOIP_SOURCE_ADDRESS", DoipSourceAddress);
        Environment.SetEnvironmentVariable("VECU_DOIP_TARGET_ADDRESS", DoipTargetAddress);
    }

    [RelayCommand]
    private void BrowseScriptFolder()
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Select a folder that contains test case JSON or TCL scripts",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(ScriptFolderPath) ? ScriptFolderPath : string.Empty
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            ScriptFolderPath = dialog.SelectedPath;
            SelectedTestMode = "Custom";
            GeneratedScriptFolderPath = string.Empty;
            AddLog($"Selected input folder: {ScriptFolderPath}");
        }
    }

    [RelayCommand]
    private void BrowseJsonFile()
    {
        using var dialog = new WinForms.OpenFileDialog
        {
            Title = "Select test case JSON",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            InitialDirectory = Directory.Exists(ScriptFolderPath)
                ? ScriptFolderPath
                : Environment.CurrentDirectory
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            ScriptFolderPath = dialog.FileName;
            SelectedTestMode = "Custom";
            GeneratedScriptFolderPath = string.Empty;
            AddLog($"Selected JSON input: {ScriptFolderPath}");
        }
    }

    [RelayCommand]
    private void ClearLogs()
    {
        Logs.Clear();
        TrafficEntries.Clear();
        CurrentStatus = "Ready";
    }

    [RelayCommand]
    private async Task LoadHistoryAsync()
    {
        var testRuns = await _reportStorageService.GetAllAsync();
        Logs.Clear();

        foreach (var testRun in testRuns.Take(20))
        {
            AddLog($"{testRun.StartedAt:yyyy-MM-dd HH:mm:ss} | {testRun.Status} | {testRun.PassedTests}/{testRun.TotalTests} passed | {testRun.Id}");
        }

        CurrentStatus = $"Loaded {testRuns.Count} saved test run(s).";
    }

    partial void OnIsRunningChanged(bool value)
    {
        RunTestsCommand.NotifyCanExecuteChanged();
        GenerateTclScriptsCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    private void AddLog(string message)
    {
        Logs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    private void AddTestCaseLog(TestCaseResult testCase)
    {
        AddLog($"{testCase.Name}");
        AddLog($"  Status : {testCase.Status}");
        AddLog($"  Message: {testCase.Message}");

        foreach (var line in FormatRawLog(testCase.RawLog))
        {
            AddLog($"  {line}");
        }
    }

    private static IEnumerable<string> FormatRawLog(string rawLog)
    {
        foreach (var line in rawLog.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("INFO Send DoIP UDS request:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("INFO Send UDS request:", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"TX       : {ExtractAfterColon(line)}";
            }
            else if (line.StartsWith("INFO Response:", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"RX       : {ExtractAfterColon(line)}";
            }
            else if (line.StartsWith("FAIL ", StringComparison.OrdinalIgnoreCase))
            {
                var reason = line["FAIL ".Length..].Trim();
                yield return $"Reason   : {FormatAssertionReason(reason)}";
            }
            else if (line.StartsWith("ERROR ", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"Error    : {line["ERROR ".Length..].Trim()}";
            }
        }
    }

    private static string FormatAssertionReason(string reason)
    {
        var actual = MatchValue(reason, "Actual");
        var expectedPrefix = MatchValue(reason, "ExpectedPrefix");
        var expectedContains = MatchValue(reason, "ExpectedContains");

        if (string.IsNullOrWhiteSpace(actual))
        {
            return reason;
        }

        var expected = !string.IsNullOrWhiteSpace(expectedPrefix)
            ? expectedPrefix
            : expectedContains;

        return string.IsNullOrWhiteSpace(expected)
            ? $"Actual={actual}"
            : $"Actual={actual}; Expected={expected}";
    }

    private static string ExtractAfterColon(string line)
    {
        var colonIndex = line.IndexOf(':');
        return colonIndex < 0 ? line : line[(colonIndex + 1)..].Trim();
    }

    private static string MatchValue(string message, string key)
    {
        var token = $"{key}=";
        var startIndex = message.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
        {
            return string.Empty;
        }

        startIndex += token.Length;
        var endIndex = message.IndexOf(' ', startIndex);

        while (endIndex > 0 &&
            endIndex + 3 <= message.Length &&
            IsHexByte(message.Substring(endIndex + 1, Math.Min(2, message.Length - endIndex - 1))))
        {
            endIndex = message.IndexOf(' ', endIndex + 1);
        }

        if (endIndex < 0)
        {
            endIndex = message.Length;
        }

        return message[startIndex..endIndex].Trim();
    }

    private static bool IsHexByte(string value)
    {
        return value.Length == 2 &&
            value.All(character => Uri.IsHexDigit(character));
    }

    private void AddTrafficEntries(string rawLog)
    {
        foreach (var line in rawLog.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("INFO Send DoIP UDS request:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("INFO Send UDS request:", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line[(line.IndexOf(':') + 1)..].Trim();
                TrafficEntries.Add(new TrafficEntry
                {
                    Direction = "TX",
                    Payload = payload,
                    Meaning = ExplainPayload(payload)
                });
            }
            else if (line.StartsWith("INFO Response:", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line[(line.IndexOf(':') + 1)..].Trim();
                TrafficEntries.Add(new TrafficEntry
                {
                    Direction = "RX",
                    Payload = payload,
                    Meaning = ExplainPayload(payload)
                });
            }
        }
    }

    private static string ExplainPayload(string payload)
    {
        if (!UdsProtocol.TryParseHexPayload(payload, out var bytes))
        {
            return "UDS payload";
        }

        if (UdsProtocol.IsNegativeResponse(bytes))
        {
            var rejectedService = bytes[1];
            var nrc = bytes[2];
            return $"UDS negative response: {UdsProtocol.GetServiceName(rejectedService)}, {UdsProtocol.GetNegativeResponseCodeName(nrc)}";
        }

        if (UdsProtocol.IsServiceRequest(bytes, UdsServiceId.SecurityAccess))
        {
            return bytes.Length > 1 && bytes[1] == 0x01
                ? "SecurityAccess: request seed"
                : "SecurityAccess: send key";
        }

        if (UdsProtocol.IsPositiveResponse(bytes, UdsServiceId.SecurityAccess))
        {
            return bytes.Length > 1 && bytes[1] == 0x01
                ? $"Positive response: security seed {FormatHexTail(bytes, 2)}"
                : "Positive response: security unlocked";
        }

        if (UdsProtocol.IsServiceRequest(bytes, UdsServiceId.ClearDiagnosticInformation))
        {
            return "ClearDiagnosticInformation";
        }

        if (UdsProtocol.IsPositiveResponse(bytes, UdsServiceId.ClearDiagnosticInformation))
        {
            return "Positive response: DTC memory cleared";
        }
        if (UdsProtocol.IsServiceRequest(bytes, UdsServiceId.ReadDataByIdentifier) &&
            UdsProtocol.TryReadDataIdentifier(bytes, 1, out var requestDid))
        {
            return $"ReadDataByIdentifier {UdsProtocol.GetDataIdentifierName(requestDid)}";
        }

        if (UdsProtocol.IsPositiveResponse(bytes, UdsServiceId.ReadDataByIdentifier) &&
            UdsProtocol.TryReadDataIdentifier(bytes, 1, out var responseDid))
        {
            return $"Positive response: {UdsProtocol.GetDataIdentifierName(responseDid)} = {FormatDidValue(responseDid, bytes, 3)}";
        }

        if (UdsProtocol.IsServiceRequest(bytes, UdsServiceId.ReadDtcInformation))
        {
            return bytes.Length > 1 && bytes[1] == (byte)UdsReadDtcReportType.ReportDtcByStatusMask
                ? "ReadDTCInformation: report DTC by status mask"
                : "ReadDTCInformation";
        }

        if (UdsProtocol.IsPositiveResponse(bytes, UdsServiceId.ReadDtcInformation))
        {
            return bytes.Length > 1 && bytes[1] == (byte)UdsReadDtcReportType.ReportDtcByStatusMask
                ? FormatVecuDtcResponse(bytes)
                : "Positive response: DTC information";
        }

        if (UdsProtocol.IsServiceRequest(bytes, UdsServiceId.WriteDataByIdentifier) &&
            UdsProtocol.TryReadDataIdentifier(bytes, 1, out var writeDid))
        {
            return $"WriteDataByIdentifier {UdsProtocol.GetDataIdentifierName(writeDid)} = {FormatDidValue(writeDid, bytes, 3)}";
        }

        if (UdsProtocol.IsPositiveResponse(bytes, UdsServiceId.WriteDataByIdentifier) &&
            UdsProtocol.TryReadDataIdentifier(bytes, 1, out var acceptedDid))
        {
            return $"Positive response: write accepted for {UdsProtocol.GetDataIdentifierName(acceptedDid)}";
        }

        if (bytes.Length > 0)
        {
            return UdsProtocol.GetServiceName(bytes[0]);
        }

        return "UDS payload";
    }

    private static string FormatDidValue(UdsDataIdentifier dataIdentifier, IReadOnlyList<byte> bytes, int offset)
    {
        return dataIdentifier switch
        {
            UdsDataIdentifier.VehicleSpeed when TryReadUInt16(bytes, offset, out var speed) => $"{speed} km/h",
            UdsDataIdentifier.EngineRpm when TryReadUInt16(bytes, offset, out var rpm) => $"{rpm} rpm",
            UdsDataIdentifier.CoolantTemperature or UdsDataIdentifier.CoolantTemperatureFaultInjection
                when bytes.Count > offset => $"{bytes[offset]} C",
            UdsDataIdentifier.Vin when bytes.Count > offset => DecodeAscii(bytes, offset),
            _ => FormatHexTail(bytes, offset)
        };
    }

    private static string FormatVecuDtcResponse(IReadOnlyList<byte> bytes)
    {
        if (bytes.Count >= 7)
        {
            var count = bytes[2];
            var dtc = (bytes[3] << 16) | (bytes[4] << 8) | bytes[5];
            var status = bytes[6];
            return $"Positive response: {count} active DTC(s), DTC 0x{dtc:X6}, status 0x{status:X2}";
        }

        if (bytes.Count >= 3 && bytes[2] == 0x00)
        {
            return "Positive response: no active DTCs";
        }

        return "Positive response: DTC list";
    }

    private static bool TryReadUInt16(IReadOnlyList<byte> bytes, int offset, out ushort value)
    {
        value = 0;
        if (bytes.Count <= offset + 1)
        {
            return false;
        }

        value = (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
        return true;
    }

    private static string DecodeAscii(IReadOnlyList<byte> bytes, int offset)
    {
        var characters = bytes.Skip(offset)
            .TakeWhile(value => value != 0)
            .Select(value => value is >= 0x20 and <= 0x7E ? (char)value : '.')
            .ToArray();

        return characters.Length == 0 ? FormatHexTail(bytes, offset) : new string(characters);
    }

    private static string FormatHexTail(IReadOnlyList<byte> bytes, int offset)
    {
        return bytes.Count <= offset
            ? "no data"
            : UdsProtocol.ToHex([.. bytes.Skip(offset)]);
    }
}
