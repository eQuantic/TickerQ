namespace eQuantic.TickerQ.EntityFrameworkCore.MongoDb.Infrastructure
{
    public class MongoDbOptions
    {
        /// <summary>
        /// MongoDB connection string (e.g., "mongodb://localhost:27017" or "mongodb+srv://...")
        /// </summary>
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// MongoDB database name (default: "TickerQ")
        /// </summary>
        public string DatabaseName { get; set; } = "TickerQ";
    }
}
