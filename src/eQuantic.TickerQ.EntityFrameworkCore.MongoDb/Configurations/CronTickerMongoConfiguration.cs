using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.EntityFrameworkCore.Entities;

namespace eQuantic.TickerQ.EntityFrameworkCore.MongoDb.Configurations
{
    public class CronTickerMongoConfiguration : IEntityTypeConfiguration<CronTickerEntity>
    {
        public void Configure(EntityTypeBuilder<CronTickerEntity> builder)
        {
            // MongoDB specific configuration
            builder.ToTable("CronTickers");

            // Primary key
            builder.HasKey(c => c.Id);
        }
    }
}
