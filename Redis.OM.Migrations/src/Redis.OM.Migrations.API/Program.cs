using Redis.OM;
using Redis.OM.Contracts;
using Redis.OM.Migrations.API;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSingleton<IRedisConnectionProvider>(new RedisConnectionProvider("redis://localhost:6379"));
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

app.MapPost("/user/index", async (IRedisConnectionProvider provider) =>
{
    await provider.Connection.CreateIndexAsync(typeof(User));
});

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

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}