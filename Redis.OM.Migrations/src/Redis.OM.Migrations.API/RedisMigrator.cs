using Bogus;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Redis.OM.Contracts;

namespace Redis.OM.Migrations.API;

public interface IRedisMigrator
{
    Task InitializeMigrationsAsync();
    Task MigrateAsync();
    Task SeedAsync();
}

public class RedisMigrator(
    IDistributedCache cache,
    IRedisConnectionProvider provider,
    IOptions<ApiSettings> settings) : IRedisMigrator
{
    private const string MigrationsKey = "SchemaMigration:Version";
    private const string LatestMigrationNumber = "20250309";
    private bool _isUpToDate;

    public async Task InitializeMigrationsAsync()
    {
        var appliedMigration = await cache.GetStringAsync(MigrationsKey);

        _isUpToDate = (appliedMigration ?? string.Empty).Equals(LatestMigrationNumber);

        if (!settings.Value.ForceMigration && _isUpToDate)
        {
            return;
        }

        await cache.SetStringAsync(MigrationsKey, LatestMigrationNumber);
    }

    public async Task MigrateAsync()
    {
        if (!settings.Value.ForceMigration && _isUpToDate)
        {
            return;
        }

        var indexTasks = Constants.Indexes
            .Select(index => provider.Connection.CreateIndexAsync(index))
            .Cast<Task>()
            .ToArray();

        await Task.WhenAll(indexTasks);
    }

    public async Task SeedAsync()
    {
        if (!settings.Value.ForceMigration && _isUpToDate)
        {
            return;
        }

        var userFixture = new Faker<User>()
            .StrictMode(true)
            .RuleFor(x => x.Id, _ => null)
            .RuleFor(x => x.Name, (f, _) => f.Name.FullName())
            .RuleFor(x => x.Address, (f, _) => f.Address.StreetAddress());

        var users = provider.RedisCollection<User>();

        await users.InsertAsync(userFixture.Generate(10));
    }
}