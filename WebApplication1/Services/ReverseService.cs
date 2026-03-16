using WebApplication1.Models;

namespace WebApplication1.Services;

public class ReverseService(
    IClock clock)
{
    private readonly List<HistoryEntry> _history = new();
    
    private readonly object _historyLock = new();

    public (bool ok, string? reversed, ApiError? error) ReverseAndStore(string text)
    {
        // If character count is greater than 100, return error message
        if (text.Length > 100)
            return (false, null, new ApiError("validation_failed", ApiErrorCodes.TextTooLong, "Maximum length is 100 characters."));
        
        var chars = text.ToCharArray();
        
        Array.Reverse(chars);
        var reversed = new string(chars);
        
        lock (_historyLock)
        {
            //_history.Add($"'{text}' reversed to '{reversed}'");
            _history.Add(new HistoryEntry(Guid.NewGuid(),text, reversed, clock.UtcNow));
        }
        
        return (true, reversed, null);
    }
    
    public HistoryResponse GetHistorySnapshot(HistoryQueryOptions options)
    {
        List<HistoryEntry> snapshot;
        
        lock (_historyLock)
        {
            snapshot = new List<HistoryEntry>(_history);
        }
        
        var filteredSnapshot = BuildFilteredHistory(snapshot, options);
        int filteredCount = filteredSnapshot.Count;
        var paginatedSnapshot = ApplyOrderingAndPaging(filteredSnapshot, options.Order, options.Limit, options.Offset);
        
        bool hasMore = ((options.Offset ?? 0) + paginatedSnapshot.Count) < filteredSnapshot.Count;
        return new HistoryResponse(paginatedSnapshot, filteredCount, paginatedSnapshot.Count, options.Offset ?? 0, options.Limit, hasMore);
    }

    public HistorySummary GetHistorySummary(HistoryQueryOptions options)
    {
        List<HistoryEntry> snapshot;
        
        lock (_historyLock)
        {
            snapshot = new List<HistoryEntry>(_history);
        }

        var filtered = BuildFilteredHistory(snapshot, options);
        
        // Calculate summary statistics
        var summaryCount = filtered.Count;
        var activeCount = filtered.Count(x => !x.IsDeleted);
        var deletedCount = filtered.Count(x => x.IsDeleted);
        var everDeletedCount = filtered.Count(x => x.LastDeletedUTC != null);
        var neverDeletedCount = filtered.Count(x => x.LastDeletedUTC == null);
        
        
        int? minLength = summaryCount > 0 ? filtered.Min(x => x.Length) : null;
        int? maxLength = summaryCount > 0 ? filtered.Max(x => x.Length) : null;
        decimal? averageLength = summaryCount > 0 ? (decimal)Math.Round(filtered.Average(x => x.Length), 2) : null;
        DateTime? oldestCreated = summaryCount > 0 ? filtered.Min(x => x.CreatedUTC) : null;
        DateTime? newestCreated = summaryCount > 0 ? filtered.Max(x => x.CreatedUTC) : null;
        
        return new HistorySummary(summaryCount, activeCount, deletedCount, everDeletedCount, 
            neverDeletedCount, minLength, maxLength, averageLength, oldestCreated, newestCreated);
    }

    public HistoryEntry? GetHistoryItem(Guid id, bool includeDeleted = false)
    {
        lock (_historyLock)
        {
            var entry = _history.Find(x => x.Id == id);
            
            if (entry is null || (entry.IsDeleted && !includeDeleted))
                return null;
                
            return entry;
        }
    }
    public (int removed, int remaining) ClearActiveHistoryItemsOlderThan(TimeSpan age)
    {
        var cutoff = clock.UtcNow - age;
        
        lock (_historyLock)
        {
            var removed = _history.RemoveAll(x => x.CreatedUTC < cutoff && !x.IsDeleted);
            return (removed, _history.Count);
        }
    }

    public (HistoryEntry?, bool) DeleteHistoryItem(Guid id)
    {
        lock (_historyLock)
        {
            var index = _history.FindIndex(x => x.Id == id);

            if (index == -1)
                return (null, false);
            
            var entry = _history[index];
            var alreadyDeleted = entry.IsDeleted;
            
            if (alreadyDeleted)
                return (entry, true);
            
            var deleted = entry with {IsDeleted = true, DeletedUTC = clock.UtcNow, LastDeletedUTC = clock.UtcNow};
            
            // Remove the old deleted history entry and add the updated one
            _history[index] = deleted;

            return (deleted, false);
        }
    }

    public (HistoryEntry? entry, bool alreadyActive) RestoreHistoryItem(Guid id)
    {
        lock (_historyLock)
        {
            var index = _history.FindIndex(x => x.Id == id);

            if (index == -1)
                return (null, false);
            
            var entry = _history[index];

            if (!entry.IsDeleted)
                return (entry, alreadyActive: true);

            
            _history[index] = entry with { IsDeleted = false, DeletedUTC = null };
            return (_history[index], false);

        }
    }

    private static List<HistoryEntry> ApplyHistoryFilter(List<HistoryEntry> snapshot, HistoryStatus? status, bool includeDeleted = false)
    {
        return status switch
        {
            HistoryStatus.Active => snapshot.Where(x => !x.IsDeleted).ToList(),
            HistoryStatus.Deleted => snapshot.Where(x => x.IsDeleted).ToList(),
            HistoryStatus.EverDeleted => snapshot.Where(x => x.LastDeletedUTC != null).ToList(),
            HistoryStatus.NeverDeleted => snapshot.Where(x => x.LastDeletedUTC == null).ToList(),
            null => includeDeleted ? snapshot : snapshot.Where(x => !x.IsDeleted).ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
    }

    private static List<HistoryEntry> ApplyOrderingAndPaging(List<HistoryEntry> snapshot, HistoryOrder order, int? limit, int? offset)
    {
        List<HistoryEntry> ordered = order == HistoryOrder.Asc
            ? snapshot.OrderBy(x => x.CreatedUTC).ThenBy(x => x.Id).ToList()
            : snapshot.OrderByDescending(x => x.CreatedUTC).ThenByDescending(x => x.Id).ToList();

        // Return the snapshot, limited to the first N items after skipping X offset if offset is not null
        if (offset.HasValue)
        {
            if (offset.Value >= ordered.Count)
                ordered =  new List<HistoryEntry>();
            else
                ordered = ordered.Skip(offset.Value).ToList();
            
        }

        return limit.HasValue ? ordered.Take(limit.Value).ToList() : ordered.ToList();
    }

    private static List<HistoryEntry> ApplyQueryFilter(List<HistoryEntry> snapshot, string? query)
    {
        if (!string.IsNullOrWhiteSpace(query))
            return snapshot.Where(x => x.Original.Contains(query, StringComparison.OrdinalIgnoreCase) || x.Reversed.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        
        return snapshot;
    }

    private static List<HistoryEntry> ApplyLengthFilter(List<HistoryEntry> snapshot, int? minLength, int? maxLength)
    {
        if (minLength.HasValue && maxLength.HasValue)
        {
            // return only the items whose original text is between min and max lenght, inclusive
            return snapshot.Where(x => x.Length >= minLength && x.Length <= maxLength).ToList();
        }
        else if (minLength.HasValue)
        {
            // return only the items whose original text is greater than or equal to min length
            return snapshot.Where(x => x.Length >= minLength).ToList();
        }
        else if (maxLength.HasValue)
        {
            // return only the items whose original text is less than or equal to max length
            return snapshot.Where(x => x.Length <= maxLength).ToList();
        }
        else
        {
            // return all items
            return snapshot;
        }
    }

    private List<HistoryEntry> BuildFilteredHistory(List<HistoryEntry> historySnapsot, HistoryQueryOptions options)
    {
        // Apply status filter
        var filtered = ApplyHistoryFilter(historySnapsot, options.Status, options.IncludeDeleted);
        
        // Apply string query
         var queried = ApplyQueryFilter(filtered, options.Query);
        
         // Apply length filter
         return ApplyLengthFilter(queried, options.MinLength, options.MaxLength);
    }
}