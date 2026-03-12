namespace WebApplication1.Models;

public record HistoryQueryOptions(
    int? Limit,
    HistoryOrder Order,
    HistoryStatus? Status,
    string? Query,
    bool IncludeDeleted,
    int? MinLength,
    int? MaxLength);