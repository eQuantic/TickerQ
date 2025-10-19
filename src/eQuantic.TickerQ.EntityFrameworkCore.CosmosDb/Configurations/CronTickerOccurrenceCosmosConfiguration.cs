using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.EntityFrameworkCore.Entities;

namespace eQuantic.TickerQ.EntityFrameworkCore.CosmosDb.Configurations
{
    public class CronTickerOccurrenceCosmosConfiguration : IEntityTypeConfiguration<CronTickerOccurrenceEntity<CronTickerEntity>>
    {
        public void Configure(EntityTypeBuilder<CronTickerOccurrenceEntity<CronTickerEntity>> builder)
        {
            // Cosmos DB specific configuration
            builder.ToContainer("CronTickerOccurrences");

            // Partition key for optimal distribution (by CronTickerId for better query performance)
            builder.HasPartitionKey(o => o.CronTickerId);

            // Primary key
            builder.HasKey(o => o.Id);

            // No discriminator needed
            builder.HasNoDiscriminator();

            // Map Id to 'id' (Cosmos DB convention)
            builder.Property(o => o.Id)
                .ToJsonProperty("id");

            // Map LockHolder to _etag (Cosmos DB concurrency token)
            builder.Property(o => o.LockHolder)
                .ToJsonProperty("_etag")
                .IsETagConcurrency()
                .IsRequired(false);

            // Map other properties
            builder.Property(o => o.CronTickerId)
                .ToJsonProperty("cronTickerId");

            builder.Property(o => o.ExecutionTime)
                .ToJsonProperty("executionTime");

            builder.Property(o => o.Status)
                .ToJsonProperty("status");

            // Ignore navigation property (Cosmos DB uses document references, not joins)
            builder.Ignore(o => o.CronTicker);
        }
    }
}
