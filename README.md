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

So as an example, we have the `POST /user/` endpoint, that will insert a pre-defined 
user onto Redis, and the way to access its record would be through `User:<ULID>`. 
The record type should be of `JSON` type, plus, by querying indexes, we should 
also be seeing `1) users` listed.

## Introducing migrations as a concept

At its core, we can't be enforcing structure on a document data model; it's not 
designed for that, hence we won't go as far as having things akin to _ALTER TABLE_, 
_CREATE TABLE_. But what we can is set out all the **indexes** that will be needed 
for all our documents, plus seeding with some pre-defined dataset if needed.
(And we will attempt at designing and implementing a concise and clean solution, 
leveraging the concept of a _migration document_ something that in EF Core's case 
is analog to the `__EFMigrationsHistory` table). The code behind this migration 
metadata key is at `RedisMigrator.cs`.

We will keep a key under `SchemaMigration:Version` with a value that follows the 
format `{yyyy}{MM}{dd}`. In case the redis service has been touched by our client, 
said value should be present, if it's not, we will immediately create the record. 
If we find that the key exists and according to the latest version in code vs 
the value on the database we will also skip creation of indexes and seeding, if 
the version varies we will run index creation plus seeding.

_NOTE:_ Index creation is idempotent, if the index is already there it won't 
do anything.

**IMPORTANT:** By analyzing the code, you can see how we are leveraging both 
_async programming_ plus _parallelization_. Specially on the `CleanupService.cs` 
side in which, even though we do not have an `async` variation for `DropIndexAndAssociatedRecords` 
we can easily parallelize the sync calls by wrapping them under a `Task.Run()`. Hence, 
we won't be blocking the main thread and will dispatch all the calls in different 
threads.

The code is aimed at using best practices enforced by Redis themselves, which is 
batching operations instead of sending multiple requests over the network.

## Keeping Dev Packages

A concept that one could easily borrow from `Node` projects is the concept of 
`devDependencies`, which in short is a way to mark certain packages to not be 
part of the final bundle of a project when building it for prod. In our case, 
that can be extended to something like `Bogus`. Which is a fake data generator, 
unless there's a compelling case to have it available on prod, we can mark it 
as something that won't be copied to the _publish_ bundle by stating:

```
<PackageReference Include="Bogus" Version="35.6.2" PrivateAssets="all"/>
```

## Persisting Redis

We have a couple of ways of persisting data with Redis and go beyond the scope 
of it being a Database _in-memory-only_.

**RDB (Redis Database):** This type of persistence performs point-in-time snapshots 
of your dataset at specified intervals.

**AOF (Append Only File):** This logs every write operation received by the server. 
These operations can then be replayed again at server startup, reconstructing the 
original dataset. Commands are logged in the same format as the Redis protocol itself.

[Reference](https://redis.io/docs/latest/operate/oss_and_stack/management/persistence/)

You can use one or the other, and even in other instances you could combine both 
approaches, their key differences are performance vs resilience in case of critical 
failures in a system; RDB depending on its configurations it could lose a lot of data 
in comparison to the most aggressive configuration in AOF that can be one second.

### RDB

For writing a file in snapshots, you can simply configure redis with these parameters
(as an example):

```
save 60 1000
```

_Note:_ This specific configuration tells Redis to dump the dataset to disk every 
60 seconds if at least 1000 keys changed.

This process _forks_ redis, which is at an OS level duplicating the process, but in 
a parent-child manner, this child process only cares about generating the new snapshot 
up until that point, once it's done it replaces a previous snapshot file if present.

### AOF

Snapshotting might not be as resilient in case of really important data and disasters 
striking the system, hence AOF is a bit more aggressive in its saving nature, you 
can configure it to not be as aggressive, but it's based around the idea of logging 
every transaction to a file, you can simply configure a `appendonly yes` to get 
it working.

It has optimizations to optimize the operations when trying to re-build the dataset, 
but it's worth noting that it also manages the concept of one base file that's actually 
a snapshot (RDB), and subsequent _child_ files being incremental in nature for 
reconstructing the dataset when restarting the service.

**IMPORTANT:** In either approach, having state saved to a file, a good way to 
keep it protected would be by encryption methods.

### Project Persistence

The project will focus on `RDB`, plus encryption of the generated file following 
OWASP standards.

First of all, we have to configure the service itself to start saving our file, and 
so we need to know a few key concepts to leverage this very fact:

Linux:
```
docker run -d --name redis-stack -p 6379:6379 -v ./redis-data:/data -v ./redis.conf:/etc/redis/redis.conf redis/redis-stack-server:latest 
```

Windows
```
docker run -d --name redis-stack -p 6379:6379 -v .\redis-data:/data -v .\redis.conf:/etc/redis/redis.conf redis/redis-stack-server:latest 
```

- Redis saves all of its data and files at `/data` in the container. And so, if 
we want to persist it we can mount that path to our host machine. In our case it's 
recommended to run the docker run command at the API project's path so that we have
all the data at hand.
- There are different ways to configure the persistence behavior, the most straightforward 
way is by also mounting a `redis.conf` file, that we have also at the root of the 
API project:

```
save 900 1
save 300 10
save 60 10000
appendonly no
dir /data
dbfilename dump.rdb
```

- `--save 900 1` → Saves every 900 seconds (if at least 1 key changes).
- `--save 300 10` → Saves every 300 seconds (if at least 10 keys change).
- `--save 60 10000` → Saves every 60 seconds (if at least 10,000 keys change).
- `--appendonly no` → Disables AOF (only RDB will be used).
- `dir /data` specifies that the Redis directory will be under `/data`
- `dbfilename dump.rdb` specifies that whenever a snapshot is generated, it will 
be under the `dump.rdb` name (at /data)

An endpoint under a `POST /snapshot` is also available so that we manually trigger 
Redis to create the snapshot. It's recommended to use `BGSAVE` instead of `SAVE` 
since the former will run in a separate child process and won't block other 
operations on the main process.

- [SAVE command](https://redis.io/docs/latest/commands/save/)
- [BGSAVE command](https://redis.io/docs/latest/commands/bgsave/)

If we have configured everything correctly, we should be seeing under a `redis-data` 
folder on our host machine (at the API project root) the `dump.rdb` file. Even if 
we stop the redis container, and then get it back on, we should have pre-seeded 
data persisted there (unless we configure the API to delete records).

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