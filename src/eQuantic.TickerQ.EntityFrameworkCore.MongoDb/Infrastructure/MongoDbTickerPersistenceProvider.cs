using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.EntityFrameworkCore.Infrastructure;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Extensions;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models.Ticker;

namespace eQuantic.TickerQ.EntityFrameworkCore.MongoDb.Infrastructure
{
    /// <summary>
    /// MongoDB implementation of the TickerQ persistence provider.
    ///
    /// IMPORTANT: MongoDB Limitations:
    /// - No traditional ACID transactions across documents (multi-document transactions available since MongoDB 4.0)
    /// - Optimistic concurrency via version tokens (LockHolder property)
    ///
    /// This implementation uses queries compatible with MongoDB's capabilities via MongoDB.EntityFrameworkCore provider.
    /// </summary>
    internal class MongoDbTickerPersistenceProvider<TTimeTicker, TCronTicker> :
        BasePersistenceProvider<MongoDbTickerContext>,
        ITickerPersistenceProvider<TTimeTicker, TCronTicker>
        where TTimeTicker : TimeTicker, new()
        where TCronTicker : CronTicker, new()
    {
        private readonly ITickerClock _clock;
        private readonly ILogger<MongoDbTickerPersistenceProvider<TTimeTicker, TCronTicker>> _logger;

        public MongoDbTickerPersistenceProvider(
            MongoDbTickerContext dbContext,
            ITickerClock clock,
            ILogger<MongoDbTickerPersistenceProvider<TTimeTicker, TCronTicker>> logger) : base(dbContext)
        {
            _clock = clock;
            _logger = logger;
        }

        #region Time Ticker Operations

        public async Task<TTimeTicker> GetTimeTickerById(Guid id, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var timeTickerContext = GetDbSet<TimeTickerEntity>();

            var query = optionsValue.Tracking
                ? timeTickerContext
                : timeTickerContext.AsNoTracking();

            var timeTicker = await query
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                .ConfigureAwait(false);

            return timeTicker?.ToTimeTicker<TTimeTicker>();
        }

        public async Task<TTimeTicker[]> GetTimeTickersByIds(Guid[] ids, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var timeTickerContext = GetDbSet<TimeTickerEntity>();

            var query = optionsValue.Tracking
                ? timeTickerContext
                : timeTickerContext.AsNoTracking();

            var timeTickers = await query
                .Where(x => ids.Contains(x.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return timeTickers.Select(x => x.ToTimeTicker<TTimeTicker>()).ToArray();
        }

        public async Task<TTimeTicker[]> GetNextTimeTickers(string lockHolder, DateTime roundedMinDate,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            // MongoDB: Using optimistic concurrency via version token (LockHolder as concurrency token)
            var timeTickerContext = GetDbSet<TimeTickerEntity>();

            try
            {
                // Query available tickers
                var availableTickers = await timeTickerContext
                    .Where(x =>
                        ((x.LockHolder == null && x.Status == TickerStatus.Idle) ||
                         (x.LockHolder == lockHolder && x.Status == TickerStatus.Queued)) &&
                        x.ExecutionTime >= roundedMinDate.AddSeconds(-2) &&
                        x.ExecutionTime < roundedMinDate.AddSeconds(1))
                    .OrderBy(x => x.ExecutionTime)
                    .Take(100)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (!availableTickers.Any())
                {
                    return Array.Empty<TTimeTicker>();
                }

                // Update tickers with lock
                var lockTime = _clock.UtcNow;
                var successfulTickers = new List<TimeTickerEntity>();

                foreach (var ticker in availableTickers)
                {
                    // Lock idle tickers or steal queued ones from other nodes, but never re-lock own queued ones
                    if ((ticker.LockHolder == null && ticker.Status == TickerStatus.Idle) ||
                        (ticker.LockHolder != lockHolder && ticker.Status == TickerStatus.Queued))
                    {
                        ticker.Status = TickerStatus.Queued;
                        ticker.LockHolder = lockHolder;
                        ticker.LockedAt = lockTime;
                        successfulTickers.Add(ticker);
                    }
                }

                if (successfulTickers.Any())
                {
                    await DbContext.SaveChangesAsync(cancellationToken);
                    var result = successfulTickers.Select(x => x.ToTimeTicker<TTimeTicker>()).ToArray();
                    DetachAll<TimeTickerEntity>();
                    return result;
                }

                return Array.Empty<TTimeTicker>();
            }
            catch (DbUpdateConcurrencyException)
            {
                // Concurrency conflict - another instance locked these tickers
                DetachAll<TimeTickerEntity>();
                return Array.Empty<TTimeTicker>();
            }
        }

        public async Task<TTimeTicker[]> GetLockedTimeTickers(string lockHolder, TickerStatus[] tickerStatuses,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var timeTickerContext = GetDbSet<TimeTickerEntity>();

            var query = optionsValue.Tracking
                ? timeTickerContext
                : timeTickerContext.AsNoTracking();

            var timeTickers = await query
                .Where(x => tickerStatuses.Contains(x.Status))
                .Where(x => x.LockHolder == lockHolder)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            return timeTickers.Select(x => x.ToTimeTicker<TTimeTicker>()).ToArray();
        }

        public async Task<TTimeTicker[]> GetTimedOutTimeTickers(DateTime now,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var timeTickerContext = GetDbSet<TimeTickerEntity>();

            var query = optionsValue.Tracking
                ? timeTickerContext
                : timeTickerContext.AsNoTracking();

            var timeTickers = await query
                .Where(x =>
                    (x.Status == TickerStatus.Idle && x.ExecutionTime.AddSeconds(1) < now) ||
                    (x.Status == TickerStatus.Queued && x.ExecutionTime.AddSeconds(3) < now))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            return timeTickers.Select(x => x.ToTimeTicker<TTimeTicker>()).ToArray();
        }

        public async Task<TTimeTicker[]> GetAllTimeTickers(Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var timeTickerContext = GetDbSet<TimeTickerEntity>();

            var query = optionsValue.Tracking
                ? timeTickerContext
                : timeTickerContext.AsNoTracking();

            var timeTickers = await query
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return timeTickers.Select(x => x.ToTimeTicker<TTimeTicker>()).ToArray();
        }

        public async Task<TTimeTicker[]> GetAllLockedTimeTickers(Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var timeTickerContext = GetDbSet<TimeTickerEntity>();

            var query = optionsValue.Tracking
                ? timeTickerContext
                : timeTickerContext.AsNoTracking();

            var timeTickers = await query
                .Where(x => x.LockHolder != null)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return timeTickers.Select(x => x.ToTimeTicker<TTimeTicker>()).ToArray();
        }

        public async Task<TTimeTicker[]> GetTimeTickersWithin(DateTime startDate, DateTime endDate,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var timeTickerContext = GetDbSet<TimeTickerEntity>();

            var query = optionsValue.Tracking
                ? timeTickerContext
                : timeTickerContext.AsNoTracking();

            var timeTickers = await query
                .Where(x => x.ExecutionTime.Date >= startDate && x.ExecutionTime.Date <= endDate)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return timeTickers.Select(x => x.ToTimeTicker<TTimeTicker>()).ToArray();
        }

        public async Task InsertTimeTickers(IEnumerable<TTimeTicker> tickers,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var timeTickerContext = GetDbSet<TimeTickerEntity>();
            await timeTickerContext.AddRangeAsync(tickers.Select(x => x.ToTimeTickerEntity()), cancellationToken);

            await SaveAndDetachAsync<TimeTickerEntity>(cancellationToken).ConfigureAwait(false);
        }

        public async Task UpdateTimeTickers(IEnumerable<TTimeTicker> tickers,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var entities = tickers.Select(x => x.ToTimeTickerEntity());

            UpsertRange(entities, x => x.Id);

            await SaveAndDetachAsync<TimeTickerEntity>(cancellationToken).ConfigureAwait(false);
        }

        public async Task RemoveTimeTickers(IEnumerable<TTimeTicker> tickers,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var entities = tickers.Select(x => x.ToTimeTickerEntity());

            DeleteRange(entities, x => x.Id);

            await SaveAndDetachAsync<TimeTickerEntity>(cancellationToken).ConfigureAwait(false);
        }

        public async Task<TTimeTicker[]> GetChildTickersByParentId(Guid parentTickerId,
            CancellationToken cancellationToken = default)
        {
            var timeTickerContext = GetDbSet<TimeTickerEntity>();

            var childTickers = await timeTickerContext
                .AsNoTracking()
                .Where(x => x.BatchParent == parentTickerId)
                .ToListAsync(cancellationToken: cancellationToken);

            return childTickers.Select(x => x.ToTimeTicker<TTimeTicker>()).ToArray();
        }

        public async Task<byte[]> GetTimeTickerRequest(Guid tickerId, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var timeTickerContext = GetDbSet<TimeTickerEntity>();

            var query = optionsValue.Tracking
                ? timeTickerContext
                : timeTickerContext.AsNoTracking();

            var timeTicker = await query
                .FirstOrDefaultAsync(x => x.Id == tickerId, cancellationToken)
                .ConfigureAwait(false);

            return timeTicker?.Request;
        }

        public async Task<DateTime?> GetEarliestTimeTickerTime(DateTime now, TickerStatus[] tickerStatuses,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var timeTickerContext = GetDbSet<TimeTickerEntity>();

            var query = optionsValue.Tracking
                ? timeTickerContext
                : timeTickerContext.AsNoTracking();

            var earliestTime = await query
                .Where(x => tickerStatuses.Contains(x.Status) && x.ExecutionTime >= now)
                .OrderBy(x => x.ExecutionTime)
                .Select(x => (DateTime?)x.ExecutionTime)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return earliestTime;
        }

        #endregion

        #region Cron Ticker Operations

        public async Task<TCronTicker> GetCronTickerById(Guid id, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerContext = GetDbSet<CronTickerEntity>();

            var query = optionsValue.Tracking
                ? cronTickerContext
                : cronTickerContext.AsNoTracking();

            var cronTicker = await query
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                .ConfigureAwait(false);

            return cronTicker?.ToCronTicker<TCronTicker>();
        }

        public async Task<TCronTicker[]> GetCronTickersByIds(Guid[] ids, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerContext = GetDbSet<CronTickerEntity>();

            var query = optionsValue.Tracking
                ? cronTickerContext
                : cronTickerContext.AsNoTracking();

            var cronTickers = await query
                .Where(x => ids.Contains(x.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickers.Select(x => x.ToCronTicker<TCronTicker>()).ToArray();
        }

        public async Task<TCronTicker[]> GetNextCronTickers(string[] expressions,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerContext = GetDbSet<CronTickerEntity>();

            var query = optionsValue.Tracking
                ? cronTickerContext
                : cronTickerContext.AsNoTracking();

            var cronTickers = await query
                .Where(x => expressions.Contains(x.Expression))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickers.Select(x => x.ToCronTicker<TCronTicker>()).ToArray();
        }

        public async Task<TCronTicker[]> GetAllExistingInitializedCronTickers(
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerContext = GetDbSet<CronTickerEntity>();

            var query = optionsValue.Tracking
                ? cronTickerContext
                : cronTickerContext.AsNoTracking();

            var cronTickers = await query
                .Where(x => x.InitIdentifier != null)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickers.Select(x => x.ToCronTicker<TCronTicker>()).ToArray();
        }

        public async Task<TCronTicker[]> GetAllCronTickers(Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerContext = GetDbSet<CronTickerEntity>();

            var query = optionsValue.Tracking
                ? cronTickerContext
                : cronTickerContext.AsNoTracking();

            var cronTickers = await query
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickers.Select(x => x.ToCronTicker<TCronTicker>()).ToArray();
        }

        public async Task<Tuple<Guid, string>[]> GetAllCronTickerExpressions(
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerContext = GetDbSet<CronTickerEntity>();

            var query = optionsValue.Tracking
                ? cronTickerContext
                : cronTickerContext.AsNoTracking();

            var expressions = await query
                .Select(x => new Tuple<Guid, string>(x.Id, x.Expression))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            return expressions;
        }

        public async Task InsertCronTickers(IEnumerable<TCronTicker> tickers,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var cronTickerContext = GetDbSet<CronTickerEntity>();
            var entities = tickers.Select(x => x.ToCronTickerEntity());

            await cronTickerContext.AddRangeAsync(entities, cancellationToken);

            await SaveAndDetachAsync<CronTickerEntity>(cancellationToken).ConfigureAwait(false);
        }

        public async Task UpdateCronTickers(IEnumerable<TCronTicker> tickers,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var entities = tickers.Select(x => x.ToCronTickerEntity());

            UpsertRange(entities, x => x.Id);

            await SaveAndDetachAsync<CronTickerEntity>(cancellationToken).ConfigureAwait(false);
        }

        public async Task RemoveCronTickers(IEnumerable<TCronTicker> tickers,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var entities = tickers.Select(x => x.ToCronTickerEntity());

            DeleteRange(entities, x => x.Id);

            await SaveAndDetachAsync<CronTickerEntity>(cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region Cron Ticker Occurrence Operations

        public async Task<CronTickerOccurrence<TCronTicker>> GetCronTickerOccurrenceById(Guid id,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var occurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? occurrenceContext
                : occurrenceContext.AsNoTracking();

            var occurrence = await query
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                .ConfigureAwait(false);

            return occurrence?.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurrencesByIds(Guid[] ids,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var occurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? occurrenceContext
                : occurrenceContext.AsNoTracking();

            var occurrences = await query
                .Where(x => ids.Contains(x.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return occurrences.Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurrencesByCronTickerIds(Guid[] ids,
            DateTime roundedMinDate, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var occurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? occurrenceContext
                : occurrenceContext.AsNoTracking();

            var occurrences = await query
                .Where(x => ids.Contains(x.CronTickerId) &&
                           x.ExecutionTime >= roundedMinDate.AddSeconds(-2) &&
                           x.ExecutionTime < roundedMinDate.AddSeconds(1))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return occurrences.Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetNextCronTickerOccurrences(DateTime nextOccurrence,
            string lockHolder, int batchSize, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var occurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            try
            {
                var availableOccurrences = await occurrenceContext
                    .Where(x =>
                        ((x.LockHolder == null && x.Status == TickerStatus.Idle) ||
                         (x.LockHolder == lockHolder && x.Status == TickerStatus.Queued)) &&
                        x.ExecutionTime >= nextOccurrence.AddSeconds(-2) &&
                        x.ExecutionTime < nextOccurrence.AddSeconds(1))
                    .OrderBy(x => x.ExecutionTime)
                    .Take(batchSize)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (!availableOccurrences.Any())
                {
                    return Array.Empty<CronTickerOccurrence<TCronTicker>>();
                }

                var lockTime = _clock.UtcNow;
                var successfulOccurrences = new List<CronTickerOccurrenceEntity<CronTickerEntity>>();

                foreach (var occurrence in availableOccurrences)
                {
                    // Lock idle occurrences or steal queued ones from other nodes, but never re-lock own queued ones
                    if ((occurrence.LockHolder == null && occurrence.Status == TickerStatus.Idle) ||
                        (occurrence.LockHolder != lockHolder && occurrence.Status == TickerStatus.Queued))
                    {
                        occurrence.Status = TickerStatus.Queued;
                        occurrence.LockHolder = lockHolder;
                        occurrence.LockedAt = lockTime;
                        successfulOccurrences.Add(occurrence);
                    }
                }

                if (successfulOccurrences.Any())
                {
                    await DbContext.SaveChangesAsync(cancellationToken);
                    var result = successfulOccurrences.Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
                    DetachAll<CronTickerOccurrenceEntity<CronTickerEntity>>();
                    return result;
                }

                return Array.Empty<CronTickerOccurrence<TCronTicker>>();
            }
            catch (DbUpdateConcurrencyException)
            {
                DetachAll<CronTickerOccurrenceEntity<CronTickerEntity>>();
                return Array.Empty<CronTickerOccurrence<TCronTicker>>();
            }
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetLockedCronTickerOccurrences(string lockHolder,
            TickerStatus[] tickerStatuses, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var occurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? occurrenceContext
                : occurrenceContext.AsNoTracking();

            var occurrences = await query
                .Where(x => tickerStatuses.Contains(x.Status))
                .Where(x => x.LockHolder == lockHolder)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            return occurrences.Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetExistingCronTickerOccurrences(
            Guid[] cronTickerIds, DateTime[] occurrenceTimes, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var occurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? occurrenceContext
                : occurrenceContext.AsNoTracking();

            var occurrences = await query
                .Where(x => cronTickerIds.Contains(x.CronTickerId) &&
                           occurrenceTimes.Contains(x.ExecutionTime))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return occurrences.Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetTimedOutCronTickerOccurrences(DateTime now,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var occurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? occurrenceContext
                : occurrenceContext.AsNoTracking();

            var occurrences = await query
                .Where(x =>
                    (x.Status == TickerStatus.Idle && x.ExecutionTime.AddSeconds(1) < now) ||
                    (x.Status == TickerStatus.Queued && x.ExecutionTime.AddSeconds(3) < now))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            return occurrences.Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetQueuedNextCronOccurrences(Guid tickerId,
            DateTime now, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var occurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? occurrenceContext
                : occurrenceContext.AsNoTracking();

            var occurrences = await query
                .Where(x => x.CronTickerId == tickerId &&
                           x.Status == TickerStatus.Queued &&
                           x.ExecutionTime >= now)
                .OrderBy(x => x.ExecutionTime)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return occurrences.Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetCronOccurrencesByCronTickerIdAndStatusFlag(
            Guid cronTickerId, bool isCompleted, DateTime startDate, DateTime endDate,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var occurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? occurrenceContext
                : occurrenceContext.AsNoTracking();

            var statuses = isCompleted
                ? new[] { TickerStatus.Done, TickerStatus.Failed }
                : new[] { TickerStatus.Idle, TickerStatus.Queued, TickerStatus.Inprogress, TickerStatus.Cancelled };

            var occurrences = await query
                .Where(x => x.CronTickerId == cronTickerId &&
                           statuses.Contains(x.Status) &&
                           x.ExecutionTime >= startDate &&
                           x.ExecutionTime <= endDate)
                .OrderBy(x => x.ExecutionTime)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return occurrences.Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetAllCronTickerOccurrences(
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var occurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? occurrenceContext
                : occurrenceContext.AsNoTracking();

            var occurrences = await query
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return occurrences.Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetAllLockedCronTickerOccurrences(
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var occurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? occurrenceContext
                : occurrenceContext.AsNoTracking();

            var occurrences = await query
                .Where(x => x.LockHolder != null)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return occurrences.Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurrencesByCronTickerId(Guid cronTickerId,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var occurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? occurrenceContext
                : occurrenceContext.AsNoTracking();

            var occurrences = await query
                .Where(x => x.CronTickerId == cronTickerId)
                .OrderBy(x => x.ExecutionTime)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return occurrences.Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurrencesWithin(DateTime startDate,
            DateTime endDate, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var occurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? occurrenceContext
                : occurrenceContext.AsNoTracking();

            var occurrences = await query
                .Where(x => x.ExecutionTime.Date >= startDate && x.ExecutionTime.Date <= endDate)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return occurrences.Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurrencesByCronTickerIdWithin(
            Guid cronTickerId, DateTime startDate, DateTime endDate, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var occurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? occurrenceContext
                : occurrenceContext.AsNoTracking();

            var occurrences = await query
                .Where(x => x.CronTickerId == cronTickerId &&
                           x.ExecutionTime.Date >= startDate &&
                           x.ExecutionTime.Date <= endDate)
                .OrderBy(x => x.ExecutionTime)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return occurrences.Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetPastCronTickerOccurrencesByCronTickerId(
            Guid cronTickerId, DateTime now, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var occurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? occurrenceContext
                : occurrenceContext.AsNoTracking();

            var occurrences = await query
                .Where(x => x.CronTickerId == cronTickerId && x.ExecutionTime < now)
                .OrderByDescending(x => x.ExecutionTime)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return occurrences.Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetTodayCronTickerOccurrencesByCronTickerId(
            Guid cronTickerId, DateTime today, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var occurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? occurrenceContext
                : occurrenceContext.AsNoTracking();

            var occurrences = await query
                .Where(x => x.CronTickerId == cronTickerId && x.ExecutionTime.Date == today.Date)
                .OrderBy(x => x.ExecutionTime)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return occurrences.Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetFutureCronTickerOccurrencesByCronTickerId(
            Guid cronTickerId, DateTime now, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var occurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? occurrenceContext
                : occurrenceContext.AsNoTracking();

            var occurrences = await query
                .Where(x => x.CronTickerId == cronTickerId && x.ExecutionTime > now)
                .OrderBy(x => x.ExecutionTime)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return occurrences.Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<byte[]> GetCronTickerRequestViaOccurrence(Guid tickerId,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            // Query 1: Get the occurrence
            var occurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();
            var occurrenceQuery = optionsValue.Tracking
                ? occurrenceContext
                : occurrenceContext.AsNoTracking();

            var occurrence = await occurrenceQuery
                .FirstOrDefaultAsync(x => x.Id == tickerId, cancellationToken)
                .ConfigureAwait(false);

            if (occurrence == null)
                return null;

            // Query 2: Get the CronTicker using CronTickerId
            var cronTickerContext = GetDbSet<CronTickerEntity>();
            var cronTickerQuery = optionsValue.Tracking
                ? cronTickerContext
                : cronTickerContext.AsNoTracking();

            var cronTicker = await cronTickerQuery
                .FirstOrDefaultAsync(x => x.Id == occurrence.CronTickerId, cancellationToken)
                .ConfigureAwait(false);

            return cronTicker?.Request;
        }

        public async Task<DateTime> GetEarliestCronTickerOccurrenceById(Guid id, TickerStatus[] tickerStatuses,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var occurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? occurrenceContext
                : occurrenceContext.AsNoTracking();

            var earliestTime = await query
                .Where(x => x.CronTickerId == id && tickerStatuses.Contains(x.Status))
                .OrderBy(x => x.ExecutionTime)
                .Select(x => x.ExecutionTime)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return earliestTime;
        }

        public async Task<IList<Guid>> InsertCronTickerOccurrences(
            IEnumerable<CronTickerOccurrence<TCronTicker>> occurrences,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var occurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var entities = occurrences.Select(x => x.ToCronTickerOccurrenceEntity<TCronTicker, CronTickerOccurrence<TCronTicker>>()).ToList();
            await occurrenceContext.AddRangeAsync(entities, cancellationToken);

            await SaveAndDetachAsync<CronTickerOccurrenceEntity<CronTickerEntity>>(cancellationToken)
                .ConfigureAwait(false);

            return entities.Select(x => x.Id).ToList();
        }

        public async Task UpdateCronTickerOccurrences(
            IEnumerable<CronTickerOccurrence<TCronTicker>> occurrences,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var entities = occurrences.Select(x => x.ToCronTickerOccurrenceEntity<TCronTicker, CronTickerOccurrence<TCronTicker>>());

            UpsertRange(entities, x => x.Id);

            await SaveAndDetachAsync<CronTickerOccurrenceEntity<CronTickerEntity>>(cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task RemoveCronTickerOccurrences(
            IEnumerable<CronTickerOccurrence<TCronTicker>> occurrences,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var entities = occurrences.Select(x => x.ToCronTickerOccurrenceEntity<TCronTicker, CronTickerOccurrence<TCronTicker>>());

            DeleteRange(entities, x => x.Id);

            await SaveAndDetachAsync<CronTickerOccurrenceEntity<CronTickerEntity>>(cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurrencesByCronTickerIds(
            Guid[] ids,
            int? takeLimit,
            Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();
            var now = _clock.UtcNow;

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            query = query
                .Where(x => ids.Contains(x.CronTickerId))
                .Where(x => x.ExecutionTime >= now)
                .Where(x => x.Status != TickerStatus.Done && x.Status != TickerStatus.DueDone &&
                           x.Status != TickerStatus.Cancelled && x.Status != TickerStatus.Failed)
                .OrderBy(x => x.ExecutionTime);

            var cronTickerOccurrences = takeLimit.HasValue
                ? await query.Take(takeLimit.Value).ToListAsync(cancellationToken).ConfigureAwait(false)
                : await query.ToListAsync(cancellationToken).ConfigureAwait(false);

            return cronTickerOccurrences
                .Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>())
                .ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetNextCronTickerOccurrences(
            DateTime nextOccurrence,
            string lockHolder,
            Guid[] cronTickerIds,
            Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            try
            {
                // MongoDB: Optimistic locking with concurrency tokens
                var availableOccurrences = await cronTickerOccurrenceContext
                    .Where(x =>
                        cronTickerIds.Contains(x.CronTickerId) &&
                        ((x.LockHolder == null && x.Status == TickerStatus.Idle) ||
                         (x.LockHolder == lockHolder && x.Status == TickerStatus.Queued)) &&
                        x.ExecutionTime == nextOccurrence)
                    .OrderBy(x => x.ExecutionTime)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (!availableOccurrences.Any())
                    return Array.Empty<CronTickerOccurrence<TCronTicker>>();

                var lockTime = _clock.UtcNow;
                var successfulOccurrences = new List<CronTickerOccurrenceEntity<CronTickerEntity>>();

                foreach (var occurrence in availableOccurrences)
                {
                    // Lock idle occurrences or steal queued ones from other nodes, but never re-lock own queued ones
                    if ((occurrence.LockHolder == null && occurrence.Status == TickerStatus.Idle) ||
                        (occurrence.LockHolder != lockHolder && occurrence.Status == TickerStatus.Queued))
                    {
                        occurrence.Status = TickerStatus.Queued;
                        occurrence.LockHolder = lockHolder;
                        occurrence.LockedAt = lockTime;
                        successfulOccurrences.Add(occurrence);
                    }
                }

                if (successfulOccurrences.Any())
                {
                    await DbContext.SaveChangesAsync(cancellationToken);
                    var result = successfulOccurrences
                        .Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>())
                        .ToArray();
                    DetachAll<CronTickerOccurrenceEntity<CronTickerEntity>>();
                    return result;
                }

                return Array.Empty<CronTickerOccurrence<TCronTicker>>();
            }
            catch (DbUpdateConcurrencyException)
            {
                DetachAll<CronTickerOccurrenceEntity<CronTickerEntity>>();
                return Array.Empty<CronTickerOccurrence<TCronTicker>>();
            }
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetExistingCronTickerOccurrences(
            Guid[] cronTickerIds,
            Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();
            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var occurrences = await cronTickerOccurrenceContext
                .Where(x => cronTickerIds.Contains(x.CronTickerId) &&
                            x.Status != TickerStatus.Done &&
                            x.Status != TickerStatus.DueDone &&
                            x.Status != TickerStatus.Failed &&
                            x.Status != TickerStatus.Cancelled)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return occurrences
                .Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>())
                .ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetQueuedNextCronOccurrences(
            Guid tickerId,
            Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();
            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var nextCronOccurrences = await cronTickerOccurrenceContext
                .Where(x => x.CronTickerId == tickerId)
                .Where(x => x.Status == TickerStatus.Queued)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            return nextCronOccurrences
                .Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>())
                .ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetCronOccurrencesByCronTickerIdAndStatusFlag(
            Guid tickerId,
            TickerStatus[] tickerStatuses,
            Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();
            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var nextCronOccurrences = await cronTickerOccurrenceContext
                .Where(x => x.CronTickerId == tickerId)
                .Where(x => tickerStatuses.Contains(x.Status))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            return nextCronOccurrences
                .Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>())
                .ToArray();
        }

        #endregion
    }
}
