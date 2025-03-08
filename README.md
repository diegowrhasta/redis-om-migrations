# Redis OM Migration Prototype

This project leverages the `Redis.OM` Nuget in order to prototype an application 
that can have migrations applied to it as if working with EF Core (something that's 
specific to a relational data model).

## Redis OM

First of all, unlike the normal Redis instance, we need to work with one extra 
module in order to leverage indexing and querying (something that Redis.OM requires).

Redis is the base service for the key-value database that we can leverage, however,  
it has on top of it modules that can extend the functionality for different use cases, 
another version of Redis with these extra modules is called `Redis Stack`. These 
are all the extra modules it comes with:

- RedisJSON – Native JSON support with efficient storage and querying.
- RediSearch – Full-text search and secondary indexing.
- RedisTimeSeries – Optimized time-series data storage and querying.
- RedisGraph – Graph database functionality based on Cypher.
- RedisBloom – Probabilistic data structures like Bloom filters and HyperLogLog.

We will specifically leverage `RedisJSON` and `RediSearch`, however, as per the 
modern terms and conditions of Redis, we can't just choose to use a couple of the 
modules, we have to commit to the whole stack. And so we will simply run a Redis 
Stack instance on docker with:

```
docker run -d --name redis-stack -p 6379:6379 redis/redis-stack-server:latest
```

It's worth noting that each module is maintained on its own GitHub repository 
though:

- https://github.com/RediSearch/RediSearch
- https://github.com/RedisJSON/RedisJSON

But just by reading their `README` files, you will see how the only option they 
give to consuming the package (at least in Docker) is by running a `Redis Stack` 
container.

### Declaring a class

Since `Redis.OM` stands for _Object Mapping_ we will basically be abstracting 
into classes the structures we want to save into Redis as JSON files (this type 
is the recommended type, but you could also use a Redis Hash).

```csharp
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
```

We will leverage decorators on our classes to mark how they should be mapped into 
Redis. First of all the decorator to mark a class for mapping is `Document`. In 
here we can feed different parameters but the main three are:

- StorageType = Either JSON or Hash
- Prefixes = When `Redis.OM` inserts a new entry into a key, it will use _key paths,_ 
and being the previous paths something that follows whatever we set in a _prefix_. 
- IndexName = Indexes are managed internally in Redis so that we can query our documents 
efficiently plus with really extendable capabilities. An easy way to check the indexes 
we have registered is by going into the Redis instance and running through the cli: 
`redis-cli > FT._LIST`

## Project Notes

- A `CleanupService` has been implemented to drop all indexes (and in turn all documents) 
the moment the API is shutting down (Hosted Service).
- We have abstracted the need to manually create an instance of a `RedisConnectionProvider` 
that's specific to `Redis.OM` so that we get access to its different methods for 
object mapping purposes. _Under the hood it uses `StackExchange.Redis` which has the 
concept of a multiplexer_. Hence, we registered a `Singleton` for the provider so 
that the app makes use of that one connection:
```
builder.Services.AddSingleton<IRedisConnectionProvider>(new RedisConnectionProvider("redis://localhost:6379"));
```
But it's worth noting that due to the constraints that the `RedisConnectionProvider` 
requires for a connection string parameter, we are declaring it in this specific 
way (unlike the typical `<Interface,Implementation>` manner).