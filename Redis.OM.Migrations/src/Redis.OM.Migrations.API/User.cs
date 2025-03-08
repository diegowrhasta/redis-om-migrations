using Redis.OM.Modeling;

namespace Redis.OM.Migrations.API;

[Document(StorageType = StorageType.Json, Prefixes = ["User"], IndexName = "users")]
public class User
{
    [RedisIdField]
    [Indexed]
    public string? Id { get; set; }

    [Indexed]
    public string? Name { get; set; }
    
    [Indexed]
    public string? Address { get; set; }
}