using Microsoft.EntityFrameworkCore;
using TickerQ.EntityFrameworkCore.Entities;
using eQuantic.TickerQ.EntityFrameworkCore.CosmosDb.Configurations;

namespace eQuantic.TickerQ.EntityFrameworkCore.CosmosDb.Infrastructure
{
    public class CosmosDbTickerContext : DbContext
    {
        private readonly CosmosDbOptions _cosmosOptions;

        public CosmosDbTickerContext(
            DbContextOptions<CosmosDbTickerContext> options,
            CosmosDbOptions cosmosOptions)
            : base(options)
        {
            _cosmosOptions = cosmosOptions;
        }

        public DbSet<TimeTickerEntity> TimeTickers { get; set; } = null!;
        public DbSet<CronTickerEntity> CronTickers { get; set; } = null!;
        public DbSet<CronTickerOccurrenceEntity<CronTickerEntity>> CronTickerOccurrences { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Apply Cosmos DB specific configurations
            modelBuilder.ApplyConfiguration(new TimeTickerCosmosConfiguration());
            modelBuilder.ApplyConfiguration(new CronTickerCosmosConfiguration());
            modelBuilder.ApplyConfiguration(new CronTickerOccurrenceCosmosConfiguration());
        }
    }
}
