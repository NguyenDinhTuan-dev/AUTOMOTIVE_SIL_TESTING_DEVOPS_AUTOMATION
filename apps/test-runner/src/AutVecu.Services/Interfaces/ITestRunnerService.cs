using AutVecu.Cores.Models;

namespace AutVecu.Services.Interfaces;

public interface ITestRunnerService
{
    Task<TestRun> RunAsync(string scriptFolderPath, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
