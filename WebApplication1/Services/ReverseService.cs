using WebApplication1.Models;

namespace WebApplication1.Services;

public class ReverseService(
    IClock clock)
{
    private readonly List<HistoryEntry> _history = new();
    
    private readonly object _historyLock = new();

    public (bool ok, string? reversed, string? message) ReverseAndStore(string text)
    {
        // If character count is greater than 100, return error message
        if (text.Length > 100)
            return (false, null, "Maximum length is 100 characters.");
        
        var chars = text.ToCharArray();
        
        Array.Reverse(chars);
        var reversed = new string(chars);
        
        lock (_historyLock)
        {
            //_history.Add($"'{text}' reversed to '{reversed}'");
            _history.Add(new HistoryEntry(Guid.NewGuid(),text, reversed, clock.UtcNow));
        }
        
        return (true, reversed, "Success.");
    }
    
    public List<HistoryEntry> GetHistorySnapshot(HistoryQueryOptions options)
    {
        List<HistoryEntry> snapshot;
        
        lock (_historyLock)
        {
            snapshot = new List<HistoryEntry>(_history);
        }
        
        // Apply status filter
        var filtered = ApplyHistoryFilter(snapshot, options.Status, options.IncludeDeleted);
        
        // Apply string query
         var queried = ApplyQueryFilter(filtered, options.Query);
        
         // Apply length filter
         queried = ApplyLengthFilter(queried, options.MinLength, options.MaxLength);
         
         // Return filtered and queried list after applying ordering and limit constraints
        return ApplyOrderingAndLimit(queried, options.Order, options.Limit);
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

    private static List<HistoryEntry> ApplyOrderingAndLimit(List<HistoryEntry> snapshot, HistoryOrder order, int? limit)
    {
        var ordered = order == HistoryOrder.Asc
            ? snapshot.OrderBy(x => x.CreatedUTC).ThenBy(x => x.Id)
            : snapshot.OrderByDescending(x => x.CreatedUTC).ThenByDescending(x => x.Id);

        // Return the snapshot, limited to the first N (limit) items
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
}