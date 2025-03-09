using Microsoft.Extensions.Options;
using Redis.OM.Contracts;

namespace Redis.OM.Migrations.API;

public class CleanupService(
    IRedisConnectionProvider provider,
    ILogger<CleanupService> logger,
    IOptions<ApiSettings> settings) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("CleanupService starting...");

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("CleanupService shutting down...");
        if (!settings.Value.CleanOnShutdown)
        {
            return;
        }

        var indexTasks = Constants.Indexes
            .Select(index =>
                Task.Run(
                    () => provider.Connection.DropIndexAndAssociatedRecords(index),
                    cancellationToken))
            .ToArray();

        await Task.WhenAll(indexTasks);
    }
}