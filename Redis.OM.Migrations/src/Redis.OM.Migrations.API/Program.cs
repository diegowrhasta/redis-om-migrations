using Microsoft.Extensions.Options;
using Redis.OM;
using Redis.OM.Contracts;
using Redis.OM.Migrations.API;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Configurations
builder.Services.Configure<ApiSettings>(builder.Configuration.GetSection(nameof(ApiSettings)));

// Redis Configurations
builder.Services.AddSingleton<IRedisMigrator, RedisMigrator>();
builder.Services.AddStackExchangeRedisCache(x => x.ConfigurationOptions = new ConfigurationOptions
{
    EndPoints = { "localhost:6379" },
    Password = string.Empty,
});
builder.Services.AddSingleton<IRedisConnectionProvider>(new RedisConnectionProvider("redis://localhost:6379"));

// Hosted Services
builder.Services.AddHostedService<CleanupService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
    {
        var forecast = Enumerable.Range(1, 5).Select(index =>
                new WeatherForecast
                (
                    DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    Random.Shared.Next(-20, 55),
                    summaries[Random.Shared.Next(summaries.Length)]
                ))
            .ToArray();
        return forecast;
    })
    .WithName("GetWeatherForecast");

app.MapPost("/user/index",
    async (IRedisConnectionProvider provider) => { await provider.Connection.CreateIndexAsync(typeof(User)); });

app.MapPost("/user", async (IRedisConnectionProvider provider) =>
{
    var users = provider.RedisCollection<User>();
    var user = new User
    {
        Address = "Kyoto",
        Name = "Brother"
    };

    var key = await users.InsertAsync(user);

    return Results.Ok(new
    {
        user.Id,
        Key = key,
    });
});
app.MapGet("/user", async (IRedisConnectionProvider provider) =>
{
    var users = provider.RedisCollection<User>();

    return Results.Ok(await users.ToListAsync());
});

app.MapPost("/migration", async (ILogger<Program> logger, IRedisMigrator migrator, IOptions<ApiSettings> settings) =>
{
    logger.LogInformation("Initializing migration...");
    await migrator.InitializeMigrationsAsync();

    logger.LogInformation("Running migrations...");
    await migrator.MigrateAsync();

    logger.LogInformation("Running seeds...");
    await migrator.SeedAsync();

    return Results.Ok(new
    {
        WillCleanOnShutdown = settings.Value.CleanOnShutdown,
        ForcedMigration = settings.Value.ForceMigration,
    });
});

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}