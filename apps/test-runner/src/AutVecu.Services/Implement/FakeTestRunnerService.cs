using AutVecu.Cores.Enums;
using AutVecu.Cores.Models;
using AutVecu.Services.Interfaces;

namespace AutVecu.Services.Implement;

public class FakeTestRunnerService : ITestRunnerService
{
    private readonly ILogParserService _logParserService;
    private readonly IReportStorageService _reportStorageService;
    private CancellationTokenSource? _currentRunCancellation;

    public FakeTestRunnerService(
        ILogParserService logParserService,
        IReportStorageService reportStorageService)
    {
        _logParserService = logParserService;
        _reportStorageService = reportStorageService;
    }

    public async Task<TestRun> RunAsync(string scriptFolderPath, CancellationToken cancellationToken = default)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _currentRunCancellation = linkedCancellation;

        var testRun = new TestRun
        {
            Status = TestStatus.Running,
            StartedAt = DateTime.Now
        };

        try
        {
            var fakeOutputs = new[]
            {
                new FakeTestOutput("Read DID 0xF190", "INFO Send UDS request: 22 F1 90\r\nPASS VIN DID response is valid."),
                new FakeTestOutput("Read DTC Information", "INFO Send UDS request: 19 02\r\nPASS DTC list response is valid."),
                new FakeTestOutput("Inject Overheat Fault", "INFO Inject coolant temperature fault\r\nFAIL Expected DTC P0217 was not active.")
            };

            foreach (var output in fakeOutputs)
            {
                linkedCancellation.Token.ThrowIfCancellationRequested();
                await Task.Delay(300, linkedCancellation.Token);

                var result = _logParserService.ParseTestCaseResult(output.Name, output.RawLog);
                testRun.TestCases.Add(result);
            }

            testRun.Status = testRun.TestCases.Any(testCase => testCase.Status == TestStatus.Failed)
                ? TestStatus.Failed
                : TestStatus.Passed;
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
                Name = "Fake Test Runner",
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
        return Task.CompletedTask;
    }

    private sealed record FakeTestOutput(string Name, string RawLog);
}
