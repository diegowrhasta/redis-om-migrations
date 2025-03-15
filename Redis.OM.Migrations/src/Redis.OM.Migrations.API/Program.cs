using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Redis.OM;
using Redis.OM.Contracts;
using Redis.OM.Migrations.API;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddScoped<IEncryptionService, EncryptionService>();

// Configurations
builder.Services.Configure<ApiSettings>(builder.Configuration.GetSection(nameof(ApiSettings)));

// Redis Configurations
builder.Services.AddSingleton(ConnectionMultiplexer.Connect("localhost"));
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

app.MapPost("/snapshot", async (ConnectionMultiplexer muxer) =>
{
    var db = muxer.GetDatabase();
    var result = await db.ExecuteAsync("BGSAVE");

    return Results.Ok(result);
});

app.MapGet("/aes-key", () =>
{
    var byteKey = RandomNumberGenerator.GetBytes(32);

    return Results.Ok(new
    {
        Key = Convert.ToBase64String(byteKey)
    });
});

app.MapPost("/encrypt/text", async (IEncryptionService service, string message) =>
{
    var messageBytes = Encoding.UTF8.GetBytes(message);

    var result = await service.EncryptAsync(messageBytes, out var iv, out var tag);

    return Results.Ok(
        new EncryptedTextPayload(
            Convert.ToBase64String(result),
            Convert.ToBase64String(iv),
            Convert.ToBase64String(tag)));
});

app.MapPost("/decrypt/text", async (IEncryptionService service, EncryptedTextPayload payload) =>
{
    var messageBytes = Convert.FromBase64String(payload.Base64EncodedEncryptedText);
    var ivBytes = Convert.FromBase64String(payload.Iv);
    var tagBytes = Convert.FromBase64String(payload.Tag);

    var result = await service.DecryptAsync(messageBytes, ivBytes, tagBytes);

    return Results.Ok(new
    {
        Base64DecryptedText = Encoding.UTF8.GetString(result)
    });
});

app.MapPost("/encrypt/package/text", async (IEncryptionService service, string message, CancellationToken token) =>
{
    var messageBytes = Encoding.UTF8.GetBytes(message);

    var result = await service.EncryptAsPackageAsync(messageBytes, token);

    return Results.Ok(new
    {
        Base64EncryptedPackage = Convert.ToBase64String(result)
    });
});

app.MapPost("/decrypt/package/text",
    async (IEncryptionService service, EncryptedTextPackagePayload payload, CancellationToken token) =>
    {
        var messageBytes = Convert.FromBase64String(payload.Base64EncryptedPackage);

        var result = await service.DecryptPackageAsync(messageBytes, token);

        return Results.Ok(new
        {
            Message = Encoding.UTF8.GetString(result)
        });
    });

app.MapPost("/encrypt/file", async (IEncryptionService service, CancellationToken token) =>
{
    var directory = Directory.GetCurrentDirectory();
    var dumpFilePath = Path.Combine(directory, "redis-data", "dump.rdb");

    await using var inputFileStream = new FileStream(dumpFilePath, FileMode.Open, FileAccess.Read);

    var encryptedFilePath = Path.Combine(directory, "redis-data", Constants.EncryptedFileName);
    await using var outputFileStream = new FileStream(encryptedFilePath, FileMode.OpenOrCreate, FileAccess.Write);

    var readBytes = new Memory<byte>(new byte[inputFileStream.Length]);
    await inputFileStream.ReadExactlyAsync(readBytes, token);
    var result = await service.EncryptAsync(readBytes.ToArray(), out var iv, out var tag);

    await outputFileStream.WriteAsync(result.AsMemory(0, result.Length), token);

    return Results.Ok(
        new EncryptedTextPayload(
            Convert.ToBase64String(result),
            Convert.ToBase64String(iv),
            Convert.ToBase64String(tag)));
});

app.MapPost("/decrypt/file",
    async (IEncryptionService service, EncryptedTextPayload payload, CancellationToken token) =>
    {
        var directory = Directory.GetCurrentDirectory();
        var encryptedFilePath = Path.Combine(directory, "redis-data", Constants.EncryptedFileName);
        var decryptedFilePath = Path.Combine(directory, "redis-data", Constants.DecryptedFileName);
        var iv = Convert.FromBase64String(payload.Iv);
        var tag = Convert.FromBase64String(payload.Tag);

        await using var inputFileStream = new FileStream(encryptedFilePath, FileMode.Open, FileAccess.Read);
        await using var outputFileStream = new FileStream(decryptedFilePath, FileMode.OpenOrCreate, FileAccess.Write);

        var readBytes = new Memory<byte>(new byte[inputFileStream.Length]);
        await inputFileStream.ReadExactlyAsync(readBytes, token);
        var result = await service.DecryptAsync(readBytes.ToArray(), iv, tag);

        await outputFileStream.WriteAsync(result.AsMemory(0, result.Length), token);

        return Results.Ok(new
        {
            Message = "File decrypted successfully"
        });
    });

app.MapPost("/encrypt/package/file", async (IEncryptionService service, CancellationToken token) =>
{
    var directory = Directory.GetCurrentDirectory();
    var dumpFilePath = Path.Combine(directory, "redis-data", "dump.rdb");

    await using var inputFileStream = new FileStream(dumpFilePath, FileMode.Open, FileAccess.Read);

    var encryptedFilePath = Path.Combine(directory, "redis-data", Constants.EncryptedFileName);
    await using var outputFileStream = new FileStream(encryptedFilePath, FileMode.OpenOrCreate, FileAccess.Write);

    var readBytes = new Memory<byte>(new byte[inputFileStream.Length]);
    await inputFileStream.ReadExactlyAsync(readBytes, token);
    var result = await service.EncryptAsPackageAsync(readBytes.ToArray(), token);

    await outputFileStream.WriteAsync(result.AsMemory(0, result.Length), token);

    return Results.Ok(new
    {
        Message = "File encrypted as package successfully"
    });
});

app.MapPost("/decrypt/package/file", async (IEncryptionService service, CancellationToken token) =>
{
    var directory = Directory.GetCurrentDirectory();
    var encryptedFilePath = Path.Combine(directory, "redis-data", Constants.EncryptedFileName);
    var decryptedFilePath = Path.Combine(directory, "redis-data", Constants.DecryptedFileName);

    await using var inputFileStream = new FileStream(encryptedFilePath, FileMode.Open, FileAccess.Read);
    await using var outputFileStream = new FileStream(decryptedFilePath, FileMode.OpenOrCreate, FileAccess.Write);

    var readBytes = new Memory<byte>(new byte[inputFileStream.Length]);
    await inputFileStream.ReadExactlyAsync(readBytes, token);
    var result = await service.DecryptPackageAsync(readBytes.ToArray(), token);

    await outputFileStream.WriteAsync(result.AsMemory(0, result.Length), token);

    return Results.Ok(new
    {
        Message = "File decrypted successfully"
    });
});

app.MapPost("/encrypt/file/chunk", async (IEncryptionService service, CancellationToken token) =>
{
    var directory = Directory.GetCurrentDirectory();
    var dumpFilePath = Path.Combine(directory, "redis-data", "dump.rdb");

    await using var inputFileStream = new FileStream(dumpFilePath, FileMode.Open, FileAccess.Read);

    var encryptedFilePath = Path.Combine(directory, "redis-data", Constants.EncryptedFileName);
    await using var outputFileStream = new FileStream(encryptedFilePath, FileMode.OpenOrCreate, FileAccess.Write);

    var buffer = new byte[Constants.ChunkSize];
    var ivAndTagList = new List<EncryptedTextPayload>();

    while (true)
    {
        var readBytes = await inputFileStream.ReadAsync(buffer.AsMemory(0, buffer.Length), token);

        if (readBytes == 0)
        {
            break;
        }

        if (readBytes < Constants.ChunkSize)
        {
            Array.Resize(ref buffer, readBytes);
        }

        var result = await service.EncryptAsync(buffer, out var iv, out var tag);

        await outputFileStream.WriteAsync(result.AsMemory(0, result.Length), token);
        ivAndTagList.Add(new EncryptedTextPayload(
            Base64EncodedEncryptedText: string.Empty,
            Convert.ToBase64String(iv),
            Convert.ToBase64String(tag))
        );
    }

    return Results.Ok(new
    {
        Parameters = ivAndTagList,
    });
});

app.MapPost("/decrypt/file/chunk", 
    async (IEncryptionService service, ChunkEncryptionParameters parameters, CancellationToken token) =>
{
    var directory = Directory.GetCurrentDirectory();
    var encryptedFilePath = Path.Combine(directory, "redis-data", Constants.EncryptedFileName);
    var decryptedFilePath = Path.Combine(directory, "redis-data", Constants.DecryptedFileName);

    await using var inputFileStream = new FileStream(encryptedFilePath, FileMode.Open, FileAccess.Read);
    await using var outputFileStream = new FileStream(decryptedFilePath, FileMode.OpenOrCreate, FileAccess.Write);

    var buffer = new byte[Constants.ChunkSize];

    foreach (var parameter in parameters.Parameters)
    {
        var readBytes = await inputFileStream.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
        
        if (readBytes < Constants.ChunkSize)
        {
            Array.Resize(ref buffer, readBytes);
        }
        
        var result = await service.DecryptAsync(
            buffer, Convert.FromBase64String(parameter.Iv), Convert.FromBase64String(parameter.Tag));
        
        await outputFileStream.WriteAsync(result.AsMemory(0, result.Length), token);
    }

    return Results.Ok(new
    {
        Message = "File decrypted successfully"
    });
});

app.MapPost("/encrypt/package/file/chunk", async (IEncryptionService service, CancellationToken token) =>
{
    var directory = Directory.GetCurrentDirectory();
    var dumpFilePath = Path.Combine(directory, "redis-data", "dump.rdb");

    await using var inputFileStream = new FileStream(dumpFilePath, FileMode.Open, FileAccess.Read);

    var encryptedFilePath = Path.Combine(directory, "redis-data", Constants.EncryptedFileName);
    await using var outputFileStream = new FileStream(encryptedFilePath, FileMode.OpenOrCreate, FileAccess.Write);

    const int actualDataSize = Constants.ChunkSize - Constants.IvSize - Constants.TagSize;
    var buffer = new byte[actualDataSize];

    while (true)
    {
        var readBytes = await inputFileStream.ReadAsync(buffer.AsMemory(0, buffer.Length), token);

        if (readBytes == 0)
        {
            break;
        }

        if (readBytes < actualDataSize)
        {
            Array.Resize(ref buffer, readBytes);
        }

        var result = await service.EncryptAsPackageAsync(buffer, token);

        await outputFileStream.WriteAsync(result.AsMemory(0, result.Length), token);
    }

    return Results.Ok(new
    {
        Message = "File packaged and encrypted in chunks",
    });
});

app.MapPost("/decrypt/package/file/chunk", async (IEncryptionService service, CancellationToken token) =>
{
    var directory = Directory.GetCurrentDirectory();
    var encryptedFilePath = Path.Combine(directory, "redis-data", Constants.EncryptedFileName);
    var decryptedFilePath = Path.Combine(directory, "redis-data", Constants.DecryptedFileName);

    await using var inputFileStream = new FileStream(encryptedFilePath, FileMode.Open, FileAccess.Read);
    await using var outputFileStream = new FileStream(decryptedFilePath, FileMode.OpenOrCreate, FileAccess.Write);

    var buffer = new byte[Constants.ChunkSize];

    while (true)
    {
        var readBytes = await inputFileStream.ReadAsync(buffer.AsMemory(0, buffer.Length), token);

        if (readBytes == 0)
        {
            break;
        }
        
        if (readBytes < Constants.ChunkSize)
        {
            Array.Resize(ref buffer, readBytes);
        }
        
        var result = await service.DecryptPackageAsync(buffer, token);
        
        await outputFileStream.WriteAsync(result.AsMemory(0, result.Length), token);
    }

    return Results.Ok(new
    {
        Message = "File decrypted successfully"
    });
});

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}