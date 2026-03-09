namespace WebApplication1.Models;

public record HistoryEntry(
    Guid Id, 
    string Original, 
    string Reversed, 
    int Length, 
    DateTime CreatedUTC,
    bool IsDeleted = false,
    DateTime? DeletedUTC = null,
    DateTime? LastDeletedUTC = null);