namespace WebApplication1.Models;

public record HistorySummary(
    int Count,
    int ActiveCount,
    int DeletedCount,
    int EverDeletedCount,
    int NeverDeletedCount,
    int? MinLength,
    int? MaxLength,
    decimal? AverageLength,
    DateTime? OldestCreatedUtc,
    DateTime? NewestCreatedUtc);