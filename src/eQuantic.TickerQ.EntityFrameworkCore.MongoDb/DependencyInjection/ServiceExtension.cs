using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using eQuantic.TickerQ.EntityFrameworkCore.MongoDb.Infrastructure;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models.Ticker;

namespace eQuantic.TickerQ.EntityFrameworkCore.MongoDb.DependencyInjection
{
    public static class ServiceExtension
    {
        /// <summary>
        /// Add MongoDB operational store for TickerQ with custom DbContext
        /// </summary>
        public static TickerOptionsBuilder AddMongoDbOperationalStore<TContext>(
            this TickerOptionsBuilder tickerConfiguration,
            Action<MongoDbOptions> configureMongoOptions,
            Action<MongoDbOptionBuilder> optionsBuilderAction = null)
            where TContext : DbContext
        {
            if (tickerConfiguration == null)
                throw new ArgumentNullException(nameof(tickerConfiguration));

            if (configureMongoOptions == null)
                throw new ArgumentNullException(nameof(configureMongoOptions));

            var mongoOptions = new MongoDbOptions();
            configureMongoOptions(mongoOptions);

            var mongoDbOptionBuilder = new MongoDbOptionBuilder();
            optionsBuilderAction?.Invoke(mongoDbOptionBuilder);

            // CRUCIAL: Access internal property via InternalsVisibleTo
            tickerConfiguration.ExternalProviderConfigServiceAction = (services) =>
            {
                // Register MongoDbOptions as singleton
                services.AddSingleton(mongoOptions);

                // Register DbContext for MongoDB
                services.AddDbContext<TContext>((serviceProvider, options) =>
                {
                    ConfigureMongoDbContext(options, mongoOptions);
                });

                // Register the persistence provider
                services.AddScoped(typeof(ITickerPersistenceProvider<,>), typeof(MongoDbTickerPersistenceProvider<,>));
            };

            UseApplicationService(tickerConfiguration, mongoDbOptionBuilder);

            return tickerConfiguration;
        }

        /// <summary>
        /// Add MongoDB operational store for TickerQ with default MongoDbTickerContext
        /// </summary>
        public static TickerOptionsBuilder AddMongoDbOperationalStore(
            this TickerOptionsBuilder tickerConfiguration,
            Action<MongoDbOptions> configureMongoOptions,
            Action<MongoDbOptionBuilder> optionsBuilderAction = null)
        {
            return AddMongoDbOperationalStore<MongoDbTickerContext>(
                tickerConfiguration,
                configureMongoOptions,
                optionsBuilderAction);
        }

        private static void ConfigureMongoDbContext(DbContextOptionsBuilder options, MongoDbOptions mongoOptions)
        {
            if (string.IsNullOrEmpty(mongoOptions.ConnectionString))
            {
                throw new InvalidOperationException(
                    "MongoDbOptions must have ConnectionString configured.");
            }

            // Configure MongoDB using MongoDB.EntityFrameworkCore provider
            options.UseMongoDB(
                mongoOptions.ConnectionString,
                mongoOptions.DatabaseName);
        }

        private static void UseApplicationService(TickerOptionsBuilder tickerConfiguration, MongoDbOptionBuilder options)
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

                // Get the public managers for seeders
                var timeTickerManager = scope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTicker>>();
                var cronTickerManager = scope.ServiceProvider.GetRequiredService<ICronTickerManager<CronTicker>>();

                options.TimeSeeder?.Invoke(timeTickerManager).GetAwaiter().GetResult();
                options.CronSeeder?.Invoke(cronTickerManager).GetAwaiter().GetResult();

                // Cancel missed tickers if configured
                var termination = options.CancelMissedTickersOnReset
                    ? ReleaseAcquiredTermination.CancelExpired
                    : ReleaseAcquiredTermination.ToIdle;

                internalTickerManager.ReleaseAllAcquiredResources(termination).GetAwaiter().GetResult();
            };
        }
    }
}
