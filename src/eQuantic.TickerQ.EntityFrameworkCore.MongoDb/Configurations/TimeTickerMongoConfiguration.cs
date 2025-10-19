using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.EntityFrameworkCore.Entities;

namespace eQuantic.TickerQ.EntityFrameworkCore.MongoDb.Configurations
{
    public class TimeTickerMongoConfiguration : IEntityTypeConfiguration<TimeTickerEntity>
    {
        public void Configure(EntityTypeBuilder<TimeTickerEntity> builder)
        {
            // MongoDB specific configuration
            builder.ToTable("TimeTickers");

            // Primary key
            builder.HasKey(t => t.Id);

            // Concurrency token
            builder.Property(t => t.LockHolder)
                .IsConcurrencyToken()
                .IsRequired(false);

            // Ignore navigation properties (not supported in MongoDB the same way)
            builder.Ignore(t => t.ParentJob);
            builder.Ignore(t => t.ChildJobs);
        }
    }
}
