using WebApplication1.Services;

using FluentAssertions;
using WebApplication1.Models;

namespace WebApplication1.Tests;

public class UnitTest1
{
    private static ReverseService CreateService(out FakeClock clock)
    {
        clock = new FakeClock{UtcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)};
        return new ReverseService(clock);
    }

    private static void CreateEntries(ReverseService service, params string[] entries)
    {
        foreach (var entry in entries)
        {
            var result = service.ReverseAndStore(entry);
            result.ok.Should().BeTrue();
        }
    }

    private static List<HistoryEntry> GetSnapshot(ReverseService service, int? limit = null, HistoryOrder order = HistoryOrder.Desc,
        HistoryStatus? status = null,  string? query = null, bool includeDeleted = false, int? minLength = null, int? maxLength = null)
    {
        var options = new HistoryQueryOptions(
            limit,
            order,
            status,
            query,
            includeDeleted,
            minLength,
            maxLength
        );

        return service.GetHistorySnapshot(options);
    }
    
    [Fact]
    public void ReverseService_ReverseAndStore_EnsureCharactersLessThan100()
    {
        ReverseService service = CreateService(out var clock);
        
        var result = service.ReverseAndStore(
            "12345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901");
        
        result.ok.Should().BeFalse();
        result.message.Should().Be("Maximum length is 100 characters.");
    }

    [Fact]
    public void ReverseService_ReverseAndStore_EnsureStringIsReversed()
    {
        ReverseService service = CreateService(out var clock);
        var result = service.ReverseAndStore("Hello, World!");
        
        result.ok.Should().BeTrue();
        result.reversed.Should().Be("!dlroW ,olleH");
    }
    
    [Fact]
    public void ReverseService_GetHistoryItem_DeletedEntry_HiddenByDefault()
    {
        ReverseService service = CreateService(out var clock);

        // Arrange: create entry
        var reverse = service.ReverseAndStore("abc");
        reverse.ok.Should().BeTrue();

        
        // Grab the id from history
        var entry = GetSnapshot(service, order: HistoryOrder.Desc, includeDeleted: false, query: null).First();
        //var entry = service.GetHistorySnapshot(null, HistoryOrder.Desc, query: null, includeDeleted: true).First();
        var id = entry.Id;

        // Soft delete it
        var del = service.DeleteHistoryItem(id);
        del.Item1.Should().NotBeNull();

        // Act: retrieve without includeDeleted
        var hidden = service.GetHistoryItem(id, includeDeleted: false);

        // Assert
        hidden.Should().BeNull();
    }

    [Fact]
    public void ReverseService_DeleteHistoryItem_DeletedEntry_TimeIsCorrect()
    {
        ReverseService service = CreateService(out var clock);
        
        // Arrange: create entry
        var reverse = service.ReverseAndStore("abc");
        reverse.ok.Should().BeTrue();
        
        // Grab the id from history
        var entry = GetSnapshot(service, order: HistoryOrder.Desc, includeDeleted: true, query: null).First();
        //var entry = service.GetHistorySnapshot(null, HistoryOrder.Desc,query: null, includeDeleted: true).First();
        var id = entry.Id;
        
        // Soft delete it
        var del = service.DeleteHistoryItem(id);
        del.Item1.Should().NotBeNull();
        
        del.Item1.DeletedUTC.Should().Be(clock.UtcNow);
    }
    
    [Fact]
    public void Snapshot_WithEqualTimestamps_IsDeterministic()
    {
        
        ReverseService service = CreateService(out var clock);
        CreateEntries(service, "a", "b");

        var snapshot = GetSnapshot(service, order: HistoryOrder.Asc, includeDeleted: false);
        //var snapshot = service.GetHistorySnapshot(null, HistoryOrder.Asc, includeDeleted: false);

        snapshot[0].CreatedUTC.Should().Be(clock.UtcNow);
        snapshot[1].CreatedUTC.Should().Be(clock.UtcNow);
        snapshot.Should().BeInAscendingOrder(x => x.Id);
    }

    [Fact]
    public void ReverseService_RestoredEntry_IsVisibleAgainInSnapshotByDefault()
    {
        ReverseService service = CreateService(out var clock);
        
        // Arrange: create entry
        var reverse = service.ReverseAndStore("abc");
        reverse.ok.Should().BeTrue();
        
        // Grab the id from history
        var entry = GetSnapshot(service, order: HistoryOrder.Desc, includeDeleted: true).First();

        //var entry = service.GetHistorySnapshot(null, HistoryOrder.Desc, includeDeleted: true).First();
        var id = entry.Id;
        
        // Soft delete it
        var del = service.DeleteHistoryItem(id);
        
        // Assert Item was deleted
        del.Item1.Should().NotBeNull();
        
        // Ensure item is not visible in snapshot by default
        var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, includeDeleted: false);

        //var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, includeDeleted: false);

        //var snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, includeDeleted: false);
        snapshot.Should().NotContain(x => x.Id == id);
        
        // Restore it
        var restore = service.RestoreHistoryItem(id);
        restore.Item1.Should().NotBeNull();

        restore.Item1!.IsDeleted.Should().BeFalse();
        restore.Item1!.DeletedUTC.Should().BeNull();
        
        // Ensure item is visible in snapshot again
        var snapshot2 = GetSnapshot(service, order: HistoryOrder.Desc, includeDeleted: false);

        //var snapshot2 = service.GetHistorySnapshot(null, HistoryOrder.Desc, includeDeleted: false);
        snapshot2.Should().Contain(x => x.Id == id);
    }

    [Fact]
    public void ReverseService_ClearHistory_IgnoresDeletedEntries()
    {
        ReverseService service = CreateService(out var clock);
        
        // Create entry
        var reverse = service.ReverseAndStore("abc");
        reverse.ok.Should().BeTrue();
        
        // Grab the id from history
        var entry = GetSnapshot(service, order: HistoryOrder.Desc, includeDeleted: true).First();

        //var entry = service.GetHistorySnapshot(null, HistoryOrder.Desc, includeDeleted: true).First();
        var id = entry.Id;
        
        // Soft delete it
        var del = service.DeleteHistoryItem(id);
        
        clock.UtcNow = clock.UtcNow.AddDays(1);
        
        // Clear entries older than a minute
        var cleared = service.ClearActiveHistoryItemsOlderThan(new TimeSpan(0, 1, 0));
        
        // Assert that the remaining items in the History is still 1
        cleared.remaining.Should().Be(1);
        cleared.removed.Should().Be(0);
        
        // Get snapshot again and ensure deleted item is still there
        var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, includeDeleted: true);

        //var snapshot  = service.GetHistorySnapshot(null, HistoryOrder.Desc, includeDeleted: true);
        
        snapshot.Should().ContainSingle(x => x.Id == id);
        snapshot.Single(x => x.Id == id).IsDeleted.Should().BeTrue();
        
        // Ensure deleted item is contained in the snapshot
        entry.Should().NotBeNull();
    }

    [Fact]
    public void ReverseService_RestoreEntry_IgnoresActiveEntries()
    {
        ReverseService service = CreateService(out var clock);
        
        // Create entry
        var reverse = service.ReverseAndStore("abc");
        reverse.ok.Should().BeTrue();
        
        // Grab the id from history
        var entry = GetSnapshot(service, order: HistoryOrder.Desc, includeDeleted: true).First();

            //var entry = service.GetHistorySnapshot(null, HistoryOrder.Desc, includeDeleted: true).First();
        var id = entry.Id;
        
        // Restore the item
        var ignoredRestore = service.RestoreHistoryItem(id);
        
        ignoredRestore.Item1.Should().NotBeNull();
        ignoredRestore.Item2.Should().BeTrue();
    }
    
    [Fact]
    public void Reverse_Service_DeleteEntry_ActiveEntrySetsLastDeletedUTC()
    {
        ReverseService service = CreateService(out var clock);
        
        // Create entry
        var reverse = service.ReverseAndStore("abc");
        reverse.ok.Should().BeTrue();
        
        // Grab the id from history
        var entry = GetSnapshot(service, order: HistoryOrder.Desc, includeDeleted: true).First();

        //var entry = service.GetHistorySnapshot(null, HistoryOrder.Desc, includeDeleted: true).First();
        var id = entry.Id;
        
        // Delete the item
        var del = service.DeleteHistoryItem(id);
        
        del.Item1.Should().NotBeNull();
        del.Item1.LastDeletedUTC.Should().Be(clock.UtcNow);
        del.Item1.IsDeleted.Should().BeTrue();
        del.Item1.DeletedUTC.Should().Be(clock.UtcNow);
    }

    [Fact]
    public void ReverseService_RestoreEntry_LastDeletedUTCStillEqualsOriginalDeletedUTC()
    {
        ReverseService service = CreateService(out var clock);
        
        // Create entry
        var reverse = service.ReverseAndStore("abc");
        reverse.ok.Should().BeTrue();
        
        // Grab the id from history
        var entry = GetSnapshot(service, order: HistoryOrder.Desc, includeDeleted: true).First();

        //var entry = service.GetHistorySnapshot(null, HistoryOrder.Desc, includeDeleted: true).First();
        var id = entry.Id;
        
        // Delete the item
        var del = service.DeleteHistoryItem(id);
        
        del.Item1.Should().NotBeNull();
        
        // Restore the item
        var restore = service.RestoreHistoryItem(id);
        
        restore.Item1.Should().NotBeNull();
        restore.Item1.IsDeleted.Should().BeFalse();
        restore.Item1.DeletedUTC.Should().BeNull();
        restore.Item1.LastDeletedUTC.Should().Be(del.Item1.DeletedUTC);
    }

    [Fact]
    public void ReverseService_DeleteEntry_AlreadyDeletedEntryDoesNotChangeTimestamps()
    {
        ReverseService service = CreateService(out var clock);
        
        // Create entry
        var reverse = service.ReverseAndStore("abc");
        reverse.ok.Should().BeTrue();
        
        // Grab the id from history
        var entry = GetSnapshot(service, order: HistoryOrder.Desc, includeDeleted: true).First();

        // var entry = service.GetHistorySnapshot(null, HistoryOrder.Desc, includeDeleted: true).First();
        var id = entry.Id;
        
        // Delete the item
        var del = service.DeleteHistoryItem(id);
        
        del.Item1.Should().NotBeNull();
        
        clock.UtcNow = clock.UtcNow.AddMinutes(10);
        // Delete the item again
        var del2 = service.DeleteHistoryItem(id);
        
        del2.Item1.Should().NotBeNull();
        del2.Item1.LastDeletedUTC.Should().Be(del.Item1.LastDeletedUTC);
        del2.Item1.DeletedUTC.Should().Be(del.Item1.DeletedUTC);
    }

    [Fact]
    public void ReverseService_GetSnapshot_ActiveStatusReturnsOnlyActiveEntries()
    {
        ReverseService service = CreateService(out var clock);
        
        // Create 3 entries
       CreateEntries(service, "abc", "def", "ghi");
        
        // Grab the snapshot
        var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, includeDeleted: false);

        // var snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.Active, includeDeleted: false);
        snapshot.Count.Should().Be(3);
        
        //Delete the middle item
        var del = service.DeleteHistoryItem(snapshot[1].Id);
        del.Item1.Should().NotBeNull();
        
        // Get snapshot again with active status and ensure the middle item is not returned
         snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, includeDeleted: false);

        // snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.Active, includeDeleted: false);
        snapshot.Count.Should().Be(2);
        snapshot.Should().NotContain(x => x.Id == del.Item1.Id);
    }
    
    [Fact]
    public void ReverseService_GetSnapshot_DeletedStatusReturnsOnlyDeletedEntries()
    {
        ReverseService service = CreateService(out var clock);
        
        // Create 3 entries
        CreateEntries(service, "abc", "def", "ghi");
        
        // Grab the snapshot
        var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, includeDeleted: false);

        // var snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.Active, includeDeleted: false);
        snapshot.Count.Should().Be(3);
        
        //Delete the middle item
        var del = service.DeleteHistoryItem(snapshot[1].Id);
        del.Item1.Should().NotBeNull();
        
        // Get snapshot again with deleted status and ensure the middle item is the only one returned
        snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Deleted, includeDeleted: false);

        // snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.Deleted, includeDeleted: false);
        snapshot.Count.Should().Be(1);
        snapshot.Should().Contain(x => x.Id == del.Item1.Id);
    }
    
    [Fact]
    public void ReverseService_GetSnapshot_EverDeletedStatusReturnsOnlyEntriesThatHaveBeenDeleted()
    {
        ReverseService service = CreateService(out var clock);
        
        // Create 4 entries
        CreateEntries(service, "abc", "def", "ghi", "jkl");
        
        // Grab the snapshot
        var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, includeDeleted: false);

        // var snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.Active, includeDeleted: false);
        snapshot.Count.Should().Be(4);
        
        //Delete the 2nd and 4th items
        var del1 = service.DeleteHistoryItem(snapshot[1].Id);
        del1.Item1.Should().NotBeNull();
        var del2 = service.DeleteHistoryItem(snapshot[3].Id);
        del2.Item1.Should().NotBeNull();
        
        // Restore the 2 deleted items
        var restore1 = service.RestoreHistoryItem(del1.Item1!.Id);
        restore1.Item1.Should().NotBeNull();
        restore1.Item2.Should().BeFalse();
        var restore2 = service.RestoreHistoryItem(del2.Item1!.Id);
        restore2.Item1.Should().NotBeNull();
        restore2.Item2.Should().BeFalse();
        
        // Get snapshot again with EverDeleted status and ensure only the previously deleted 2nd and 4th items are inthe list
        snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.EverDeleted, includeDeleted: false);

        // snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.EverDeleted, includeDeleted: false);
        snapshot.Count.Should().Be(2);
        snapshot.Should().Contain(x => x.Id == del1.Item1.Id);
        snapshot.Should().Contain(x => x.Id == del2.Item1.Id);
    }

    [Fact]
    public void ReverseService_GetSnapshot_NeverDeletedStatusReturnsOnlyEntriesThatHaveNotBeenDeleted()
    {
        ReverseService service = CreateService(out var clock);
        
        // Create 4 entries
        CreateEntries(service, "abc", "def", "ghi", "jkl");
        
        // Grab the snapshot
        var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, includeDeleted: false);

        // var snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.Active, includeDeleted: false);
        snapshot.Count.Should().Be(4);
        
        var neverDeleted1 = snapshot[0];
        var neverDeleted2 = snapshot[2];
        
        //Delete the 2nd and 4th items
        var del1 = service.DeleteHistoryItem(snapshot[1].Id);
        del1.Item1.Should().NotBeNull();
        var del2 = service.DeleteHistoryItem(snapshot[3].Id);
        del2.Item1.Should().NotBeNull();
        
        // Restore the 2 deleted items
        var restore1 = service.RestoreHistoryItem(del1.Item1!.Id);
        restore1.Item1.Should().NotBeNull();
        restore1.Item2.Should().BeFalse();
        var restore2 = service.RestoreHistoryItem(del2.Item1!.Id);
        restore2.Item1.Should().NotBeNull();
        restore2.Item2.Should().BeFalse();
        
        // Get snapshot again with NeverDeleted status and ensure restored items are not returned
        snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.NeverDeleted, includeDeleted: false);

        // snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.NeverDeleted, includeDeleted: false);
        snapshot.Count.Should().Be(2);
        snapshot.Should().Contain(x => x.Id == neverDeleted1.Id);
        snapshot.Should().Contain(x => x.Id == neverDeleted2.Id);
        snapshot.Should().NotContain(x => x.Id == restore1.Item1!.Id);
        snapshot.Should().NotContain(x => x.Id == restore2.Item1!.Id);
        snapshot.Should().NotContain(x => x.LastDeletedUTC != null);
    }
    
    [Fact]
    public void ReverseService_GetSnapshot_RestoredEntriesReturnWhenActiveButNotNeverDeleted()
    {
        var service = CreateService(out var clock);
        
        // Create 4 entries
        CreateEntries(service, "abc", "def", "ghi", "jkl");
        
        // Grab the snapshot
        var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, includeDeleted: false);

        // var snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.Active, includeDeleted: false);
        snapshot.Count.Should().Be(4);
        
        var neverDeleted1 = snapshot[0];
        var neverDeleted2 = snapshot[2];
        
        //Delete the 2nd and 4th items
        var del1 = service.DeleteHistoryItem(snapshot[1].Id);
        del1.Item1.Should().NotBeNull();
        var del2 = service.DeleteHistoryItem(snapshot[3].Id);
        del2.Item1.Should().NotBeNull();
        
        // Restore the 2 deleted items
        var restore1 = service.RestoreHistoryItem(del1.Item1!.Id);
        restore1.Item1.Should().NotBeNull();
        restore1.Item2.Should().BeFalse();
        var restore2 = service.RestoreHistoryItem(del2.Item1!.Id);
        restore2.Item1.Should().NotBeNull();
        restore2.Item2.Should().BeFalse();
        
        // Get snapshot again with NeverDeleted status and ensure the 2 restored items are not returend
        snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.NeverDeleted, includeDeleted: false);

        // snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.NeverDeleted, includeDeleted: false);
        snapshot.Count.Should().Be(2);
        snapshot.Should().Contain(x => x.Id == neverDeleted1.Id);
        snapshot.Should().Contain(x => x.Id == neverDeleted2.Id);
        snapshot.Should().OnlyContain(x => x.Id != restore1.Item1!.Id);
        snapshot.Should().OnlyContain(x => x.Id != restore2.Item1!.Id);
        snapshot.Should().OnlyContain(x => x.LastDeletedUTC == null);
        
        // Get snapshot again with Active and ensure the two restored entries are returned
        snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, includeDeleted: false);

        // snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.Active, includeDeleted: false);
        snapshot.Count.Should().Be(4);
        snapshot.Should().Contain(x => x.Id == restore1.Item1.Id);
        snapshot.Should().Contain(x => x.Id == restore2.Item1.Id);
        snapshot.Should().OnlyContain(x => x.IsDeleted == false);
    }
    
    [Fact]
    public void ReverseService_GetSnapshot_EverDeletedStatusReturnsBothCurrentlyDeletedAndRestoredEntries()
    {
        var service = CreateService(out var clock);
        
        // Create 4 entries
        CreateEntries(service, "abc", "def", "ghi", "jkl");

        
        // Grab the snapshot
        var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, includeDeleted: false);

        // var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, includeDeleted: false);

        //var snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.Active, includeDeleted: false);
        snapshot.Count.Should().Be(4);
        
        var neverDeleted1 = snapshot[0];
        var neverDeleted2 = snapshot[2];
        
        //Delete the 2nd and 4th items
        var del1 = service.DeleteHistoryItem(snapshot[1].Id);
        del1.Item1.Should().NotBeNull();
        var del2 = service.DeleteHistoryItem(snapshot[3].Id);
        del2.Item1.Should().NotBeNull();
        
        // Restore one item
        var restore1 = service.RestoreHistoryItem(del1.Item1!.Id);
        restore1.Item1.Should().NotBeNull();
        restore1.Item2.Should().BeFalse();
        
        // Get snapshot again with EverDeleted should return both the deleted item and the restored item
        snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.EverDeleted, includeDeleted: false);

       // var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.EverDeleted, includeDeleted: false);

       // snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.EverDeleted, includeDeleted: false);
        snapshot.Count.Should().Be(2);
        snapshot.Should().Contain(x => x.Id == del2.Item1.Id);
        snapshot.Should().Contain(x => x.Id == restore1.Item1.Id);
        snapshot.Should().OnlyContain(x => x.LastDeletedUTC != null);
    }

    [Fact]
    public void ReverseService_GetSnapshot_QueryMatchesOriginalCaseInsensitively()
    {
        var service = CreateService(out var clock);
        
        // Create  entries
        CreateEntries(service, "Hello");
        
        // Grab the snapshot
        var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, query: "he", includeDeleted: false);

        // var snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.Active, "he", includeDeleted: false);
        snapshot.Count.Should().Be(1);
        snapshot[0].Original.Should().Be("Hello");
        
        snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, query: "EL", includeDeleted: false);

        // snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.Active, "EL", includeDeleted: false);
        snapshot.Count.Should().Be(1);
        snapshot[0].Original.Should().Be("Hello");
    }
    
    [Fact]
    public void ReverseService_GetSnapshot_QueryMatchesReversedCaseInsensitively()
    {
        var service = CreateService(out var clock);
        
        // Create  entries
        CreateEntries(service, "Hello");
        
        // Grab the snapshot
        var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, query: "OLL", includeDeleted: false);

        // var snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.Active, "OLL", includeDeleted: false);
        snapshot.Count.Should().Be(1);
        snapshot[0].Reversed.Should().Be("olleH");
    }

    [Fact]
    public void ReverseService_GetSnapshot_BlankOrWhitespaceQueryDoesNotFilter()
    {
        var service = CreateService(out var clock);
        
        // Create 3 entries
        CreateEntries(service, "abcd", "efgh", "ijkl");
        
        // Grab the snapshot
        var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, query: " ", includeDeleted: false);

        // var snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.Active, "  ", includeDeleted: false);
        snapshot.Count.Should().Be(3);
    }

    [Fact]
    public void ReverseService_GetSnapshot_StatusAndQueryCombineCorrectly()
    {
        var service = CreateService(out var clock);
        
        // Create 4 entries
        CreateEntries(service, "abcd", "efgh", "ijkl", "mnop");
        
        //Delete efgh entry
        var hisotyItemID = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, query: "ef", includeDeleted: false).First().Id;
        //var historyItemId = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.Active, "ef", includeDeleted: false).First().Id;
        var del = service.DeleteHistoryItem(hisotyItemID);
        
        // Grab the snapshot
        var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, query: "ef", includeDeleted: false);

        // var snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.Active, "ef", includeDeleted: false);
        snapshot.Count.Should().Be(0);
        
        snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Deleted, query: "ef", includeDeleted: false);
        //snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.Deleted, "ef", includeDeleted: false);
        snapshot.Count.Should().Be(1);
    }
    
}
