namespace WebApplication1.Models;

public record HistoryResponse(
    List<HistoryEntry> Items,
    int Total,
    int Count,
    int Offset,
    int? Limit,
    bool HasMore);