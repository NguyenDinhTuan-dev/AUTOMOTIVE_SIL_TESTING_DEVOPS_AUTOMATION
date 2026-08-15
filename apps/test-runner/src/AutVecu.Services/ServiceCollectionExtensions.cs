using AutVecu.Services.Interfaces;
using AutVecu.Services.Implement;
using Microsoft.Extensions.DependencyInjection;

namespace AutVecu.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAutVecuServices(this IServiceCollection services)
    {
        services.AddSingleton<ILogParserService, LogParserService>();
        services.AddSingleton<IReportStorageService, JsonReportStorageService>();
        services.AddSingleton<ITestRunnerService, TclTestRunnerService>();
        services.AddSingleton<ITclScriptGeneratorService, TclScriptGeneratorService>();

        return services;
    }
}
