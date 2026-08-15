using System.Diagnostics;
using System.Text;
using AutVecu.Cores.Enums;
using AutVecu.Cores.Models;
using AutVecu.Services.Interfaces;

namespace AutVecu.Services.Implement;

public class TclTestRunnerService : ITestRunnerService
{
    private readonly ILogParserService _logParserService;
    private readonly IReportStorageService _reportStorageService;
    private readonly object _processLock = new();
    private Process? _currentProcess;
    private CancellationTokenSource? _currentRunCancellation;

    public TclTestRunnerService(
        ILogParserService logParserService,
        IReportStorageService reportStorageService)
    {
        _logParserService = logParserService;
        _reportStorageService = reportStorageService;
    }

    public async Task<TestRun> RunAsync(string scriptFolderPath, CancellationToken cancellationToken = default)
    {
        scriptFolderPath = string.IsNullOrWhiteSpace(scriptFolderPath)
            ? string.Empty
            : Path.GetFullPath(scriptFolderPath);

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _currentRunCancellation = linkedCancellation;

        var testRun = new TestRun
        {
            Status = TestStatus.Running,
            StartedAt = DateTime.Now
        };

        try
        {
            if (string.IsNullOrWhiteSpace(scriptFolderPath) || !Directory.Exists(scriptFolderPath))
            {
                AddRunnerError(testRun, "TCL script folder does not exist.", scriptFolderPath);
                return testRun;
            }

            var scriptFiles = Directory
                .EnumerateFiles(scriptFolderPath, "*.tcl", SearchOption.AllDirectories)
                .OrderBy(filePath => filePath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (scriptFiles.Count == 0)
            {
                AddRunnerError(testRun, "No .tcl test scripts were found.", scriptFolderPath);
                return testRun;
            }

            foreach (var scriptFile in scriptFiles)
            {
                linkedCancellation.Token.ThrowIfCancellationRequested();

                var output = await RunScriptAsync(scriptFile, linkedCancellation.Token);
                var testCaseName = Path.GetFileNameWithoutExtension(scriptFile);
                var result = _logParserService.ParseTestCaseResult(testCaseName, output);

                testRun.TestCases.Add(result);
            }

            testRun.Status = ResolveRunStatus(testRun);
        }
        catch (OperationCanceledException)
        {
            testRun.Status = TestStatus.Stopped;
        }
        catch (Exception ex)
        {
            testRun.Status = TestStatus.Error;
            testRun.TestCases.Add(new TestCaseResult
            {
                Name = "TCL Test Runner",
                Status = TestStatus.Error,
                StartedAt = DateTime.Now,
                FinishedAt = DateTime.Now,
                Message = ex.Message,
                RawLog = ex.ToString()
            });
        }
        finally
        {
            testRun.FinishedAt = DateTime.Now;
            await _reportStorageService.SaveAsync(testRun, CancellationToken.None);
            _currentRunCancellation = null;
        }

        return testRun;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _currentRunCancellation?.Cancel();

        lock (_processLock)
        {
            if (_currentProcess is { HasExited: false })
            {
                _currentProcess.Kill(entireProcessTree: true);
            }
        }

        return Task.CompletedTask;
    }

    private async Task<string> RunScriptAsync(string scriptFile, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var tclExecutable = ResolveTclExecutable();
        var fullScriptFile = Path.GetFullPath(scriptFile);

        var startInfo = new ProcessStartInfo
        {
            FileName = tclExecutable,
            Arguments = $"\"{fullScriptFile}\"",
            WorkingDirectory = Path.GetDirectoryName(fullScriptFile) ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        lock (_processLock)
        {
            _currentProcess = process;
        }

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                output.AppendLine(args.Data);
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                output.AppendLine($"ERROR {args.Data}");
            }
        };

        try
        {
            if (!process.Start())
            {
                return $"ERROR Failed to start TCL script: {scriptFile}";
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0 && !ContainsResultToken(output.ToString()))
            {
                output.AppendLine($"ERROR TCL script exited with code {process.ExitCode}.");
            }

            return output.ToString();
        }
        finally
        {
            lock (_processLock)
            {
                if (ReferenceEquals(_currentProcess, process))
                {
                    _currentProcess = null;
                }
            }
        }
    }

    private static string ResolveTclExecutable()
    {
        var configuredPath = Environment.GetEnvironmentVariable("TCLSH_PATH");
        return string.IsNullOrWhiteSpace(configuredPath) ? "tclsh" : configuredPath;
    }

    private static bool ContainsResultToken(string output)
    {
        return output.Contains("PASS", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("FAIL", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("ERROR", StringComparison.OrdinalIgnoreCase);
    }

    private static TestStatus ResolveRunStatus(TestRun testRun)
    {
        if (testRun.TestCases.Any(testCase => testCase.Status == TestStatus.Error))
        {
            return TestStatus.Error;
        }

        if (testRun.TestCases.Any(testCase => testCase.Status == TestStatus.Failed))
        {
            return TestStatus.Failed;
        }

        return testRun.TestCases.Count == 0 ? TestStatus.Pending : TestStatus.Passed;
    }

    private static void AddRunnerError(TestRun testRun, string message, string rawLog)
    {
        testRun.Status = TestStatus.Error;
        testRun.TestCases.Add(new TestCaseResult
        {
            Name = "TCL Test Runner",
            Status = TestStatus.Error,
            StartedAt = DateTime.Now,
            FinishedAt = DateTime.Now,
            Message = message,
            RawLog = rawLog
        });
    }
}
