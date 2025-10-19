using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using eQuantic.TickerQ.EntityFrameworkCore.CosmosDb.Infrastructure;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Enums;

namespace eQuantic.TickerQ.EntityFrameworkCore.CosmosDb.DependencyInjection
{
    public static class ServiceExtension
    {
        /// <summary>
        /// Add Cosmos DB operational store for TickerQ with custom DbContext
        /// </summary>
        public static TickerOptionsBuilder AddCosmosDbOperationalStore<TContext>(
            this TickerOptionsBuilder tickerConfiguration,
            Action<CosmosDbOptions> configureCosmosOptions,
            Action<CosmosDbOptionBuilder> optionsBuilderAction = null)
            where TContext : DbContext
        {
            if (tickerConfiguration == null)
                throw new ArgumentNullException(nameof(tickerConfiguration));

            if (configureCosmosOptions == null)
                throw new ArgumentNullException(nameof(configureCosmosOptions));

            var cosmosOptions = new CosmosDbOptions();
            configureCosmosOptions(cosmosOptions);

            var cosmosDbOptionBuilder = new CosmosDbOptionBuilder();
            optionsBuilderAction?.Invoke(cosmosDbOptionBuilder);

            // CRUCIAL: Access internal property via InternalsVisibleTo
            tickerConfiguration.ExternalProviderConfigServiceAction = (services) =>
            {
                // Register CosmosDbOptions as singleton
                services.AddSingleton(cosmosOptions);

                // Register DbContext for Cosmos DB
                services.AddDbContext<TContext>((serviceProvider, options) =>
                {
                    ConfigureCosmosDbContext(options, cosmosOptions);
                });

                // Register the persistence provider
                services.AddScoped(typeof(ITickerPersistenceProvider<,>), typeof(CosmosDbTickerPersistenceProvider<,>));
            };

            UseApplicationService(tickerConfiguration, cosmosDbOptionBuilder);

            return tickerConfiguration;
        }

        /// <summary>
        /// Add Cosmos DB operational store for TickerQ with default CosmosDbTickerContext
        /// </summary>
        public static TickerOptionsBuilder AddCosmosDbOperationalStore(
            this TickerOptionsBuilder tickerConfiguration,
            Action<CosmosDbOptions> configureCosmosOptions,
            Action<CosmosDbOptionBuilder> optionsBuilderAction = null)
        {
            return AddCosmosDbOperationalStore<CosmosDbTickerContext>(
                tickerConfiguration,
                configureCosmosOptions,
                optionsBuilderAction);
        }

        private static void ConfigureCosmosDbContext(DbContextOptionsBuilder options, CosmosDbOptions cosmosOptions)
        {
            if (!string.IsNullOrEmpty(cosmosOptions.ConnectionString))
            {
                options.UseCosmos(
                    cosmosOptions.ConnectionString,
                    cosmosOptions.DatabaseName);
            }
            else if (!string.IsNullOrEmpty(cosmosOptions.AccountEndpoint) && !string.IsNullOrEmpty(cosmosOptions.AccountKey))
            {
                options.UseCosmos(
                    cosmosOptions.AccountEndpoint,
                    cosmosOptions.AccountKey,
                    cosmosOptions.DatabaseName);
            }
            else
            {
                throw new InvalidOperationException(
                    "CosmosDbOptions must have either ConnectionString or both AccountEndpoint and AccountKey configured.");
            }

            // Configure region if specified
            if (!string.IsNullOrEmpty(cosmosOptions.Region))
            {
                options.UseCosmos(
                    cosmosOptions.ConnectionString ?? cosmosOptions.AccountEndpoint!,
                    cosmosOptions.AccountKey ?? string.Empty,
                    cosmosOptions.DatabaseName,
                    cosmosDbOptions =>
                    {
                        cosmosDbOptions.Region(cosmosOptions.Region);
                    });
            }
        }

        private static void UseApplicationService(TickerOptionsBuilder tickerConfiguration, CosmosDbOptionBuilder options)
        {
            tickerConfiguration.ExternalProviderConfigApplicationAction = (serviceProvider) =>
            {
                using var scope = serviceProvider.CreateScope();

                var internalTickerManager = scope.ServiceProvider.GetRequiredService<IInternalTickerManager>();

                var functionsToSeed = TickerFunctionProvider.TickerFunctions
                    .Where(x => !string.IsNullOrEmpty(x.Value.cronExpression))
                    .Select(x => (x.Key, x.Value.cronExpression)).ToArray();

                if (!options.IgnoreSeedMemoryCronTickersInternal)
                    internalTickerManager.SyncWithDbMemoryCronTickers(functionsToSeed).GetAwaiter().GetResult();

                options.TimeSeeder?.Invoke(internalTickerManager).GetAwaiter().GetResult();
                options.CronSeeder?.Invoke(internalTickerManager).GetAwaiter().GetResult();

                // Cancel missed tickers if configured
                var termination = options.CancelMissedTickersOnReset
                    ? ReleaseAcquiredTermination.CancelExpired
                    : ReleaseAcquiredTermination.ToIdle;

                internalTickerManager.ReleaseAllAcquiredResources(termination).GetAwaiter().GetResult();
            };
        }
    }
}
