using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.EntityFrameworkCore.Entities;

namespace eQuantic.TickerQ.EntityFrameworkCore.MongoDb.Configurations
{
    public class CronTickerOccurrenceMongoConfiguration : IEntityTypeConfiguration<CronTickerOccurrenceEntity<CronTickerEntity>>
    {
        public void Configure(EntityTypeBuilder<CronTickerOccurrenceEntity<CronTickerEntity>> builder)
        {
            // MongoDB specific configuration
            builder.ToTable("CronTickerOccurrences");

            // Primary key
            builder.HasKey(o => o.Id);

            // Concurrency token
            builder.Property(o => o.LockHolder)
                .IsConcurrencyToken()
                .IsRequired(false);

            // Ignore navigation property (MongoDB uses document references, not joins)
            // We'll load CronTicker separately with a second query when needed
            builder.Ignore(o => o.CronTicker);
        }
    }
}
