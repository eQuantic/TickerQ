using Microsoft.EntityFrameworkCore;
using TickerQ.EntityFrameworkCore.Entities;
using eQuantic.TickerQ.EntityFrameworkCore.MongoDb.Configurations;

namespace eQuantic.TickerQ.EntityFrameworkCore.MongoDb.Infrastructure
{
    public class MongoDbTickerContext : DbContext
    {
        private readonly MongoDbOptions? _mongoDbOptions;

        public MongoDbTickerContext(
            DbContextOptions options,
            MongoDbOptions mongoDbOptions)
            : base(options)
        {
            _mongoDbOptions = mongoDbOptions;
        }

        // Constructor for derived classes
        protected MongoDbTickerContext(DbContextOptions options) : base(options)
        {
            _mongoDbOptions = null;
        }

        public DbSet<TimeTickerEntity> TimeTickers { get; set; } = null!;
        public DbSet<CronTickerEntity> CronTickers { get; set; } = null!;
        public DbSet<CronTickerOccurrenceEntity<CronTickerEntity>> CronTickerOccurrences { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Apply MongoDB specific configurations
            ApplyTickerQConfigurations(modelBuilder);
        }

        /// <summary>
        /// Apply TickerQ MongoDB configurations. Override this in derived classes to customize.
        /// </summary>
        protected virtual void ApplyTickerQConfigurations(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new TimeTickerMongoConfiguration());
            modelBuilder.ApplyConfiguration(new CronTickerMongoConfiguration());
            modelBuilder.ApplyConfiguration(new CronTickerOccurrenceMongoConfiguration());
        }
    }
}
