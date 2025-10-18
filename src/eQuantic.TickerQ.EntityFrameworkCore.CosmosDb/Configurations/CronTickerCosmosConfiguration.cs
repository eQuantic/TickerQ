using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.EntityFrameworkCore.Entities;

namespace eQuantic.TickerQ.EntityFrameworkCore.CosmosDb.Configurations
{
    public class CronTickerCosmosConfiguration : IEntityTypeConfiguration<CronTickerEntity>
    {
        public void Configure(EntityTypeBuilder<CronTickerEntity> builder)
        {
            // Cosmos DB specific configuration
            builder.ToContainer("CronTickers");

            // Partition key for optimal distribution
            builder.HasPartitionKey(c => c.Id);

            // Primary key
            builder.HasKey(c => c.Id);

            // No discriminator needed
            builder.HasNoDiscriminator();

            // Map Id to 'id' (Cosmos DB convention)
            builder.Property(c => c.Id)
                .ToJsonProperty("id");
        }
    }
}
