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

            // Concurrency token (ETag in Cosmos DB)
            builder.Property(o => o.LockHolder)
                .IsConcurrencyToken()
                .IsRequired(false);

            // Owned navigation for CronTicker (embedded document in Cosmos DB)
            builder.OwnsOne(o => o.CronTicker, cronTicker =>
            {
                cronTicker.Property(c => c.Id).ToJsonProperty("cronTickerId");
                cronTicker.Property(c => c.Expression).ToJsonProperty("expression");
                cronTicker.Property(c => c.Function).ToJsonProperty("function");
                cronTicker.Property(c => c.Request).ToJsonProperty("request");
                cronTicker.Property(c => c.Retries).ToJsonProperty("retries");
                cronTicker.Property(c => c.RetryIntervals).ToJsonProperty("retryIntervals");
                cronTicker.Property(c => c.Description).ToJsonProperty("description");
                cronTicker.Property(c => c.CreatedAt).ToJsonProperty("createdAt");
                cronTicker.Property(c => c.UpdatedAt).ToJsonProperty("updatedAt");
                cronTicker.Property(c => c.InitIdentifier).ToJsonProperty("initIdentifier");
            });
        }
    }
}
