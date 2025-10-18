# eQuantic.TickerQ.EntityFrameworkCore.CosmosDb

[![NuGet](https://img.shields.io/nuget/v/eQuantic.TickerQ.EntityFrameworkCore.CosmosDb.svg)](https://www.nuget.org/packages/eQuantic.TickerQ.EntityFrameworkCore.CosmosDb)
[![NuGet](https://img.shields.io/nuget/dt/eQuantic.TickerQ.EntityFrameworkCore.CosmosDb.svg)](https://www.nuget.org/packages/eQuantic.TickerQ.EntityFrameworkCore.CosmosDb)
[![Publish](https://github.com/equantic/TickerQ/actions/workflows/publish-cosmosdb.yml/badge.svg)](https://github.com/equantic/TickerQ/actions/workflows/publish-cosmosdb.yml)
[![.NET 8+](https://img.shields.io/badge/.NET-8.0+-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

> **Note**: This is a community-maintained provider for TickerQ.
> This package is maintained by eQuantic and is not an official TickerQ package.
>
> - **Fork source**: https://github.com/equantic/TickerQ
> - **Original project**: https://github.com/arcenox/TickerQ

Azure Cosmos DB provider for TickerQ - A lightweight, developer-friendly library for queuing and executing cron and time-based jobs in the background.

## Overview

This provider enables TickerQ to use Azure Cosmos DB as the persistence layer for storing and managing scheduled jobs.

## Installation

### From NuGet (Recommended)

```bash
dotnet add package eQuantic.TickerQ.EntityFrameworkCore.CosmosDb
```

### From Source

Since this provider requires access to internal members of TickerQ (via `InternalsVisibleTo`), if building from source you need the forked version:

```bash
# Clone the repository
git clone https://github.com/equantic/TickerQ.git
cd TickerQ

# Build the CosmosDb provider
dotnet build src/eQuantic.TickerQ.EntityFrameworkCore.CosmosDb/eQuantic.TickerQ.EntityFrameworkCore.CosmosDb.csproj --configuration Release
```

## Usage

Add the Cosmos DB provider to your application:

```csharp
using eQuantic.TickerQ.EntityFrameworkCore.CosmosDb.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add TickerQ with Cosmos DB persistence
builder.Services.AddTickerQ(options =>
{
    options.AddCosmosDbOperationalStore(cosmosOptions =>
    {
        // Option 1: Using connection string
        cosmosOptions.ConnectionString = "AccountEndpoint=https://...;AccountKey=...";
        cosmosOptions.DatabaseName = "TickerQ";

        // Option 2: Using endpoint and key separately
        cosmosOptions.AccountEndpoint = "https://your-account.documents.azure.com:443/";
        cosmosOptions.AccountKey = "your-account-key";
        cosmosOptions.DatabaseName = "TickerQ";

        // Optional: Configure region
        cosmosOptions.Region = "East US";

        // Optional: Performance tuning
        cosmosOptions.MaxRetryAttempts = 3;
        cosmosOptions.MaxRetryWaitTime = TimeSpan.FromSeconds(30);
        cosmosOptions.EnableContentResponseOnWrite = false;
    });
});

var app = builder.Build();
app.Run();
```

## Configuration Options

| Property | Description | Default |
|----------|-------------|---------|
| `ConnectionString` | Complete Cosmos DB connection string | - |
| `AccountEndpoint` | Cosmos DB account endpoint URL | - |
| `AccountKey` | Cosmos DB account key | - |
| `DatabaseName` | Database name to use for TickerQ data | "TickerQ" |
| `Region` | Preferred Azure region | null |
| `MaxRetryAttempts` | Maximum number of retry attempts for failed requests | 3 |
| `MaxRetryWaitTime` | Maximum time to wait between retries | 30 seconds |
| `EnableContentResponseOnWrite` | Return full document content on write operations | false |

## Technical Details

### Why This Package Uses a Fork

The original TickerQ library uses internal members (`ExternalProviderConfigServiceAction` and `ExternalProviderConfigApplicationAction`) in `TickerOptionsBuilder` to allow external providers to configure services. These internal members are necessary for implementing persistence providers but are not publicly accessible.

Our fork (https://github.com/equantic/TickerQ) adds `InternalsVisibleTo` attributes to expose these APIs to this package:
- `TickerQ` → `eQuantic.TickerQ.EntityFrameworkCore.CosmosDb`
- `TickerQ.Utilities` → `eQuantic.TickerQ.EntityFrameworkCore.CosmosDb`
- `TickerQ.EntityFrameworkCore` → `eQuantic.TickerQ.EntityFrameworkCore.CosmosDb`

This allows the CosmosDb provider to access the required internal APIs without exposing them publicly in the main library.

### Cosmos DB Collections

The provider creates the following containers in your Cosmos DB database:

- **TimeTickers**: Stores one-time scheduled jobs
- **CronTickers**: Stores recurring cron-based jobs
- **CronTickerOccurrences**: Stores individual occurrences of cron jobs

All containers use the document `id` as the partition key for optimal distribution.

### Differences from Relational Provider

Cosmos DB is a NoSQL database with different characteristics than SQL databases:

- **No foreign keys**: Navigation properties are not mapped
- **No table inheritance**: Each entity type gets its own container
- **JSON storage**: Complex types are stored as JSON within documents
- **Partition keys**: Each container uses `id` as the partition key
- **No joins**: Data must be denormalized for optimal queries
- **No transactions across documents**: Unlike SQL, Cosmos DB doesn't support traditional ACID transactions across multiple documents
- **Optimistic concurrency**: Uses ETags (via `LockHolder` property) for concurrency control instead of pessimistic locking

### Cosmos DB Query Limitations

Be aware of these Cosmos DB limitations when using this provider:

- ❌ **No `GROUP BY`**: Cosmos DB doesn't support GroupBy operations
- ❌ **No complex joins**: Cross-container queries are not supported
- ❌ **No distributed transactions**: Each document operation is atomic, but multi-document transactions are limited
- ⚠️ **Limited `DISTINCT`**: Only works on primitive types
- ⚠️ **Query complexity**: Very complex queries may need to be executed client-side
- ✅ **Fully supported**: `WHERE`, `ORDER BY`, `TOP`/`TAKE`, `SELECT`, `COUNT`, `SUM`, `MIN`, `MAX`, `AVG`

The current implementation avoids these limitations and works seamlessly with Cosmos DB's capabilities.

## Project Structure

```
eQuantic.TickerQ.EntityFrameworkCore.CosmosDb/
├── Configurations/
│   ├── CronTickerCosmosConfiguration.cs
│   ├── CronTickerOccurrenceCosmosConfiguration.cs
│   └── TimeTickerCosmosConfiguration.cs
├── DependencyInjection/
│   └── ServiceExtension.cs
├── Infrastructure/
│   ├── CosmosDbOptions.cs
│   └── CosmosDbTickerContext.cs
└── README.md
```

## Compatibility with Upstream

This package depends on a fork that maintains full compatibility with the upstream TickerQ repository. The only changes to the original code are:
1. Adding `<InternalsVisibleTo Include="eQuantic.TickerQ.EntityFrameworkCore.CosmosDb" />` to 3 project files
2. This CosmosDb provider project (not included in upstream solution)

We actively sync with upstream:
```bash
git remote add upstream https://github.com/arcenox/TickerQ.git
git fetch upstream
git merge upstream/main
```

## Contributing

Issues and pull requests are welcome at https://github.com/equantic/TickerQ

If you have suggestions for the TickerQ core library, please contribute directly to the upstream project: https://github.com/arcenox/TickerQ

## License

This provider follows the same MIT license as the main TickerQ library.

## Support

For issues specific to the Cosmos DB provider:
- GitHub Issues: https://github.com/equantic/TickerQ/issues

For general TickerQ questions:
- Original repository: https://github.com/arcenox/TickerQ
