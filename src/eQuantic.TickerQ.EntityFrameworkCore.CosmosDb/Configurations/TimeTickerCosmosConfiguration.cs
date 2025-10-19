using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.EntityFrameworkCore.Entities;

namespace eQuantic.TickerQ.EntityFrameworkCore.CosmosDb.Configurations
{
    public class TimeTickerCosmosConfiguration : IEntityTypeConfiguration<TimeTickerEntity>
    {
        public void Configure(EntityTypeBuilder<TimeTickerEntity> builder)
        {
            // Cosmos DB specific configuration
            builder.ToContainer("TimeTickers");

            // Partition key for optimal distribution
            builder.HasPartitionKey(t => t.Id);

            // Primary key
            builder.HasKey(t => t.Id);

            // No discriminator needed
            builder.HasNoDiscriminator();

            // Map Id to 'id' (Cosmos DB convention)
            builder.Property(t => t.Id)
                .ToJsonProperty("id");

            // Map LockHolder to _etag (Cosmos DB concurrency token)
            builder.Property(t => t.LockHolder)
                .ToJsonProperty("_etag")
                .IsETagConcurrency()
                .IsRequired(false);

            // Ignore navigation properties (not supported in Cosmos DB the same way)
            builder.Ignore(t => t.ParentJob);
            builder.Ignore(t => t.ChildJobs);
        }
    }
}
