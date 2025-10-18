using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using eQuantic.TickerQ.EntityFrameworkCore.CosmosDb.Infrastructure;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;

namespace eQuantic.TickerQ.EntityFrameworkCore.CosmosDb.DependencyInjection
{
    public static class ServiceExtension
    {
        /// <summary>
        /// Add Cosmos DB operational store for TickerQ
        /// </summary>
        public static TickerOptionsBuilder AddCosmosDbOperationalStore(
            this TickerOptionsBuilder tickerConfiguration,
            Action<CosmosDbOptions> configureCosmosOptions)
        {
            if (tickerConfiguration == null)
                throw new ArgumentNullException(nameof(tickerConfiguration));

            if (configureCosmosOptions == null)
                throw new ArgumentNullException(nameof(configureCosmosOptions));

            var cosmosOptions = new CosmosDbOptions();
            configureCosmosOptions(cosmosOptions);

            // CRUCIAL: Access internal property via InternalsVisibleTo
            tickerConfiguration.ExternalProviderConfigServiceAction = (services) =>
            {
                // Register CosmosDbOptions as singleton
                services.AddSingleton(cosmosOptions);

                // Register DbContext for Cosmos DB
                services.AddDbContext<CosmosDbTickerContext>((serviceProvider, options) =>
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
                });

                // Register the persistence provider
                services.AddScoped(typeof(ITickerPersistenceProvider<,>), typeof(CosmosDbTickerPersistenceProvider<,>));
            };

            return tickerConfiguration;
        }
    }
}
