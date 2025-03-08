using Redis.OM.Contracts;

namespace Redis.OM.Migrations.API;

public class CleanupService(IRedisConnectionProvider provider, ILogger<CleanupService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("CleanupService starting...");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("CleanupService shutting down...");
        provider.Connection.DropIndexAndAssociatedRecords(typeof(User));

        return Task.CompletedTask;
    }
}