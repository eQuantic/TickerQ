using System;

namespace eQuantic.TickerQ.EntityFrameworkCore.CosmosDb.Infrastructure
{
    public class CosmosDbOptions
    {
        public string ConnectionString { get; set; } = string.Empty;
        public string DatabaseName { get; set; } = "TickerQ";
        public string? AccountEndpoint { get; set; }
        public string? AccountKey { get; set; }
        public string? Region { get; set; }
        public bool EnableContentResponseOnWrite { get; set; } = false;
        public int MaxRetryAttempts { get; set; } = 3;
        public TimeSpan MaxRetryWaitTime { get; set; } = TimeSpan.FromSeconds(30);
    }
}
