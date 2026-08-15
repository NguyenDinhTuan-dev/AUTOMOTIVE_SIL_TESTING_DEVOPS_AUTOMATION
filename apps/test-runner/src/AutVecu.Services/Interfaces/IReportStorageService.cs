using AutVecu.Cores.Models;

namespace AutVecu.Services.Interfaces;

public interface IReportStorageService
{
    Task SaveAsync(TestRun testRun, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TestRun>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<TestRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
