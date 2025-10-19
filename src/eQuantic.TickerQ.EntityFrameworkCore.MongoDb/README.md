# eQuantic.TickerQ.EntityFrameworkCore.MongoDb

[![NuGet](https://img.shields.io/nuget/v/eQuantic.TickerQ.EntityFrameworkCore.MongoDb.svg)](https://www.nuget.org/packages/eQuantic.TickerQ.EntityFrameworkCore.MongoDb)
[![NuGet](https://img.shields.io/nuget/dt/eQuantic.TickerQ.EntityFrameworkCore.MongoDb.svg)](https://www.nuget.org/packages/eQuantic.TickerQ.EntityFrameworkCore.MongoDb)
[![.NET 8+](https://img.shields.io/badge/.NET-8.0+-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

> **Note**: This is a community-maintained provider for TickerQ.
> This package is maintained by eQuantic and is not an official TickerQ package.
>
> - **Fork source**: https://github.com/equantic/TickerQ
> - **Original project**: https://github.com/arcenox/TickerQ

MongoDB provider for TickerQ - A lightweight, developer-friendly library for queuing and executing cron and time-based jobs in the background.

## Overview

This provider enables TickerQ to use MongoDB as the persistence layer for storing and managing scheduled jobs.

### ✨ Features

- 🚀 **Multi-framework Support**: .NET 8.0 and .NET 9.0
- 🌐 **MongoDB**: Document-oriented NoSQL database
- 📦 **MongoDB EF Core Provider**: Uses official MongoDB.EntityFrameworkCore
- 🔒 **Optimistic Concurrency**: Version tokens for distributed locking
- 🎯 **Full TickerQ Compatibility**: Works seamlessly with TickerQ 2.5.3+
- ⚡ **High Performance**: Fast read/write operations with automatic indexing
- 🌍 **Flexible Deployment**: On-premises, cloud (MongoDB Atlas), or containerized
- 📈 **Horizontal Scaling**: Sharding support for large workloads
- 💾 **Document Model**: Native JSON storage with flexible schemas

### 📋 Requirements

| .NET Version | MongoDB EF Core Version | eQuantic.TickerQ Version |
|--------------|-------------------------|--------------------------|
| .NET 8.0     | 8.3.2                   | 2.5.3+                   |
| .NET 9.0     | 9.0.2                   | 2.5.3+                   |

> **Important**: This package depends on `eQuantic.TickerQ.*` packages (not the original `TickerQ.*`). These are eQuantic-maintained forks with `InternalsVisibleTo` configured to allow this provider to access internal APIs.

## Installation

### From NuGet (Recommended)

```bash
dotnet add package eQuantic.TickerQ.EntityFrameworkCore.MongoDb
```

### From Source

Since this provider requires access to internal members of TickerQ (via `InternalsVisibleTo`), if building from source you need the forked version:

```bash
# Clone the repository
git clone https://github.com/equantic/TickerQ.git
cd TickerQ

# Build the MongoDb provider
dotnet build src/eQuantic.TickerQ.EntityFrameworkCore.MongoDb/eQuantic.TickerQ.EntityFrameworkCore.MongoDb.csproj --configuration Release
```

## Usage

Add the MongoDB provider to your application:

```csharp
using eQuantic.TickerQ.EntityFrameworkCore.MongoDb.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add TickerQ with MongoDB persistence
builder.Services.AddTickerQ(options =>
{
    options.AddMongoDbOperationalStore(mongoOptions =>
    {
        // Local MongoDB
        mongoOptions.ConnectionString = "mongodb://localhost:27017";
        mongoOptions.DatabaseName = "TickerQ";

        // Or MongoDB Atlas (cloud)
        // mongoOptions.ConnectionString = "mongodb+srv://user:password@cluster.mongodb.net/?retryWrites=true&w=majority";
        // mongoOptions.DatabaseName = "TickerQ";
    });
});

var app = builder.Build();
app.Run();
```

### Advanced Configuration

The MongoDB provider supports the same advanced configuration options as the EF Core provider:

#### Seed Initial Tickers

```csharp
builder.Services.AddTickerQ(options =>
{
    options.AddMongoDbOperationalStore(
        mongoOptions =>
        {
            mongoOptions.ConnectionString = "mongodb://localhost:27017";
            mongoOptions.DatabaseName = "TickerQ";
        },
        optionsBuilder =>
        {
            // Seed initial tickers (time-based and cron-based)
            optionsBuilder.UseTickerSeeder(
                async timeTicker =>
                {
                    await timeTicker.AddAsync(new TimeTicker
                    {
                        Id = Guid.NewGuid(),
                        Function = "CleanupLogs",
                        ExecutionTime = DateTime.UtcNow.AddSeconds(5),
                    });
                },
                async cronTicker =>
                {
                    await cronTicker.AddAsync(new CronTicker
                    {
                        Id = Guid.NewGuid(),
                        Expression = "0 0 * * *", // every day at 00:00 UTC
                        Function = "CleanupLogs"
                    });
                });
        });
});
```

#### Cancel Missed Tickers on Startup

```csharp
builder.Services.AddTickerQ(options =>
{
    options.AddMongoDbOperationalStore(
        mongoOptions =>
        {
            mongoOptions.ConnectionString = "mongodb://localhost:27017";
            mongoOptions.DatabaseName = "TickerQ";
        },
        optionsBuilder =>
        {
            // Cancel any missed tickers tied to this node on application start
            optionsBuilder.CancelMissedTickersOnAppStart();
        });
});
```

#### Ignore Memory Cron Tickers

```csharp
builder.Services.AddTickerQ(options =>
{
    options.AddMongoDbOperationalStore(
        mongoOptions =>
        {
            mongoOptions.ConnectionString = "mongodb://localhost:27017";
            mongoOptions.DatabaseName = "TickerQ";
        },
        optionsBuilder =>
        {
            // Don't automatically seed memory-based cron tickers
            optionsBuilder.IgnoreSeedMemoryCronTickers();
        });
});
```

#### Using Custom DbContext

You can provide your own `DbContext` to customize MongoDB configuration:

```csharp
public class MyCustomMongoContext : MongoDbTickerContext
{
    public MyCustomMongoContext(DbContextOptions options, MongoDbOptions mongoOptions)
        : base(options, mongoOptions)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Add your custom MongoDB configurations here
        modelBuilder.Entity<TimeTickerEntity>()
            .ToCollection("CustomTimeTickers");
    }
}

// In Program.cs
builder.Services.AddTickerQ(options =>
{
    options.AddMongoDbOperationalStore<MyCustomMongoContext>(mongoOptions =>
    {
        mongoOptions.ConnectionString = "mongodb://localhost:27017";
        mongoOptions.DatabaseName = "TickerQ";
    });
});
```

## Configuration Options

| Property | Description | Default |
|----------|-------------|---------|
| `ConnectionString` | Complete MongoDB connection string | - |
| `DatabaseName` | Database name to use for TickerQ data | "TickerQ" |

## Technical Details

### Package Dependencies

This package depends on **eQuantic-maintained forks** of TickerQ libraries published under the `eQuantic.*` namespace:

| Package | Purpose |
|---------|---------|
| `eQuantic.TickerQ` | Core TickerQ library with InternalsVisibleTo |
| `eQuantic.TickerQ.Utilities` | TickerQ utilities with InternalsVisibleTo |
| `eQuantic.TickerQ.EntityFrameworkCore` | EF Core provider base with InternalsVisibleTo |

These packages are **functionally identical** to the original `TickerQ.*` packages but include `InternalsVisibleTo` attributes that allow this MongoDB provider to access internal APIs required for implementation.

### Why Forked Packages Are Needed

The original TickerQ library uses internal members (`ExternalProviderConfigServiceAction`, `ExternalProviderConfigApplicationAction`, and `BasePersistenceProvider`) to allow external providers to configure services and implement persistence. These internal members are necessary for implementing persistence providers but are not publicly accessible.

Our fork (https://github.com/equantic/TickerQ) adds `InternalsVisibleTo` attributes to expose these APIs:
```xml
<InternalsVisibleTo Include="eQuantic.TickerQ.EntityFrameworkCore.MongoDb" />
```

This allows the MongoDb provider to access the required internal APIs without exposing them publicly in the main library. The forked packages are published as `eQuantic.TickerQ.*` to avoid conflicts with the original packages.

### MongoDB Collections

The provider creates the following collections in your MongoDB database:

- **TimeTickers**: Stores one-time scheduled jobs
- **CronTickers**: Stores recurring cron-based jobs
- **CronTickerOccurrences**: Stores individual occurrences of cron jobs

### When to Use MongoDB vs SQL Server vs Cosmos DB

**✅ Use MongoDB when you need:**
- Document-oriented data model with flexible schemas
- Horizontal scaling with sharding
- High write throughput
- Local deployment or MongoDB Atlas cloud
- Lower cost than Cosmos DB for similar features
- Native JSON storage
- Geospatial queries or text search (advanced MongoDB features)

**✅ Use Cosmos DB when you need:**
- Global distribution across multiple Azure regions
- 99.999% availability SLA
- Multi-model API support (SQL, MongoDB, Cassandra, Gremlin, Table)
- Automatic and instant scalability
- Turnkey global distribution

**✅ Use SQL Server when you need:**
- Complex relational queries with JOINs and GROUP BY
- ACID transactions across multiple tables
- Existing SQL infrastructure
- Strong consistency guarantees
- Traditional relational data modeling

### Differences from Relational Provider

MongoDB is a NoSQL database with different characteristics than SQL databases:

- **No foreign keys**: Navigation properties are not mapped
- **Document model**: Each entity is stored as a JSON document
- **No joins**: Data can be denormalized or loaded with separate queries
- **Flexible schemas**: Collections don't enforce strict schemas
- **No transactions across documents** (by default): Unlike SQL, MongoDB doesn't support traditional ACID transactions across multiple documents (though multi-document transactions are available since MongoDB 4.0)
- **Optimistic concurrency**: Uses version tokens (via `LockHolder` property) for concurrency control

### MongoDB Query Capabilities

MongoDB via EF Core provider supports:

- ✅ **Fully supported**: `WHERE`, `ORDER BY`, `TAKE`/`SKIP`, `SELECT`, `COUNT`, `SUM`, `MIN`, `MAX`, `AVG`
- ⚠️ **Limited `JOIN`**: Cross-collection queries require separate queries
- ⚠️ **Query complexity**: Very complex queries may need to be executed client-side

The current implementation avoids limitations and works seamlessly with MongoDB's capabilities.

## Project Structure

```
eQuantic.TickerQ.EntityFrameworkCore.MongoDb/
├── Configurations/
│   ├── CronTickerMongoConfiguration.cs
│   ├── CronTickerOccurrenceMongoConfiguration.cs
│   └── TimeTickerMongoConfiguration.cs
├── DependencyInjection/
│   └── ServiceExtension.cs
├── Infrastructure/
│   ├── MongoDbOptions.cs
│   ├── MongoDbTickerContext.cs
│   └── MongoDbTickerPersistenceProvider.cs
├── MongoDbOptionBuilder.cs
└── README.md
```

## Framework Support

This package uses multi-targeting to support both .NET 8.0 and .NET 9.0 in a single NuGet package:

- **net8.0**: Uses MongoDB.EntityFrameworkCore 8.3.2
- **net9.0**: Uses MongoDB.EntityFrameworkCore 9.0.2

When you install this package, NuGet automatically selects the correct version based on your project's target framework.

## Compatibility with Upstream

This package depends on a fork that maintains full compatibility with the upstream TickerQ repository. The only changes to the original code are:
1. Adding `<InternalsVisibleTo Include="eQuantic.TickerQ.EntityFrameworkCore.MongoDb" />` to 3 project files
2. This MongoDb provider project (not included in upstream solution)

We actively sync with upstream:
```bash
git remote add upstream https://github.com/arcenox/TickerQ.git
git fetch upstream
git merge upstream/main
```

## Contributing

Issues and pull requests are welcome at https://github.com/equantic/TickerQ

If you have suggestions for the TickerQ core library, please contribute directly to the upstream project: https://github.com/arcenox/TickerQ

## Versioning

This package follows [Semantic Versioning](https://semver.org/):
- **Major version**: Breaking changes to public API
- **Minor version**: New features, backward compatible
- **Patch version**: Bug fixes, backward compatible

### Version History

- **1.0.0**: Initial release with .NET 8.0 and .NET 9.0 support, feature parity with CosmosDb provider

## License

This provider follows the same MIT license as the main TickerQ library.

## Support

For issues specific to the MongoDB provider:
- 🐛 **GitHub Issues**: https://github.com/equantic/TickerQ/issues
- 📦 **NuGet Package**: https://www.nuget.org/packages/eQuantic.TickerQ.EntityFrameworkCore.MongoDb

For general TickerQ questions:
- 📚 **Official Docs**: https://tickerq.net
- 💬 **Discord Community**: https://discord.gg/ZJemWvp9MK
- 🔧 **Original Repository**: https://github.com/arcenox/TickerQ
