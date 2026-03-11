using WebApplication1.Services;

using FluentAssertions;
using WebApplication1.Models;

namespace WebApplication1.Tests;

public class UnitTest1
{
    [Fact]
    public void ReverseService_ReverseAndStore_EnsureCharactersLessThan100()
    {
        var clock = new FakeClock{UtcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)};
        ReverseService service = new ReverseService(clock);
        var result = service.ReverseAndStore(
            "12345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901");
        
        result.ok.Should().BeFalse();
        result.message.Should().Be("Maximum length is 100 characters.");
    }

    [Fact]
    public void ReverseService_ReverseAndStore_EnsureStringIsReversed()
    {
        var clock = new FakeClock{UtcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)};
        ReverseService service = new ReverseService(clock);
        var result = service.ReverseAndStore("Hello, World!");
        
        result.ok.Should().BeTrue();
        result.reversed.Should().Be("!dlroW ,olleH");
    }
    
    [Fact]
    public void ReverseService_GetHistoryItem_DeletedEntry_HiddenByDefault()
    {
        var clock = new FakeClock{UtcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)};
        var service = new ReverseService(clock);

        // Arrange: create entry
        var reverse = service.ReverseAndStore("abc");
        reverse.ok.Should().BeTrue();

        // Grab the id from history
        var entry = service.GetHistorySnapshot(null, HistoryOrder.Desc, includeDeleted: true).First();
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
        var testTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock{UtcNow = testTime};
        var service = new ReverseService(clock);
        
        // Arrange: create entry
        var reverse = service.ReverseAndStore("abc");
        reverse.ok.Should().BeTrue();
        
        // Grab the id from history
        var entry = service.GetHistorySnapshot(null, HistoryOrder.Desc, includeDeleted: true).First();
        var id = entry.Id;
        
        // Soft delete it
        var del = service.DeleteHistoryItem(id);
        del.Item1.Should().NotBeNull();
        
        del.Item1.DeletedUTC.Should().Be(testTime);
    }
    
    [Fact]
    public void Snapshot_WithEqualTimestamps_IsDeterministic()
    {
        
        var testTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock { UtcNow = testTime };
        var service = new ReverseService(clock);

        service.ReverseAndStore("a");
        service.ReverseAndStore("b");

        var snapshot = service.GetHistorySnapshot(null, HistoryOrder.Asc, includeDeleted: false);

        snapshot[0].CreatedUTC.Should().Be(testTime);
        snapshot[1].CreatedUTC.Should().Be(testTime);
        snapshot.Should().BeInAscendingOrder(x => x.Id);
    }

    [Fact]
    public void ReverseService_RestoredEntry_IsVisibleAgainInSnapshotByDefault()
    {
        var clock = new FakeClock{UtcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)};
        var service = new ReverseService(clock);
        
        // Arrange: create entry
        var reverse = service.ReverseAndStore("abc");
        reverse.ok.Should().BeTrue();
        
        // Grab the id from history
        var entry = service.GetHistorySnapshot(null, HistoryOrder.Desc, includeDeleted: true).First();
        var id = entry.Id;
        
        // Soft delete it
        var del = service.DeleteHistoryItem(id);
        
        // Assert Item was deleted
        del.Item1.Should().NotBeNull();
        
        // Ensure item is not visible in snapshot by default
        var snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, includeDeleted: false);
        snapshot.Should().NotContain(x => x.Id == id);
        
        // Restore it
        var restore = service.RestoreHistoryItem(id);
        restore.Item1.Should().NotBeNull();

        restore.Item1!.IsDeleted.Should().BeFalse();
        restore.Item1!.DeletedUTC.Should().BeNull();
        
        // Ensure item is visible in snapshot again
        var snapshot2 = service.GetHistorySnapshot(null, HistoryOrder.Desc, includeDeleted: false);
        snapshot2.Should().Contain(x => x.Id == id);
    }

    [Fact]
    public void ReverseService_ClearHistory_IgnoresDeletedEntries()
    {
        var clock = new FakeClock{UtcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)};
        var service = new ReverseService(clock);
        
        // Create entry
        var reverse = service.ReverseAndStore("abc");
        reverse.ok.Should().BeTrue();
        
        // Grab the id from history
        var entry = service.GetHistorySnapshot(null, HistoryOrder.Desc, includeDeleted: true).First();
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
        var snapshot  = service.GetHistorySnapshot(null, HistoryOrder.Desc, includeDeleted: true);
        
        snapshot.Should().ContainSingle(x => x.Id == id);
        snapshot.Single(x => x.Id == id).IsDeleted.Should().BeTrue();
        
        // Ensure deleted item is contained in the snapshot
        entry.Should().NotBeNull();
    }

    [Fact]
    public void ReverseService_RestoreEntry_IgnoresActiveEntries()
    {
        var clock = new FakeClock{UtcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)};
        var service = new ReverseService(clock);
        
        // Create entry
        var reverse = service.ReverseAndStore("abc");
        reverse.ok.Should().BeTrue();
        
        // Grab the id from history
        var entry = service.GetHistorySnapshot(null, HistoryOrder.Desc, includeDeleted: true).First();
        var id = entry.Id;
        
        // Restore the item
        var ignoredRestore = service.RestoreHistoryItem(id);
        
        ignoredRestore.Item1.Should().NotBeNull();
        ignoredRestore.Item2.Should().BeTrue();
    }
    
    [Fact]
    public void Reverse_Service_DeleteEntry_ActiveEntrySetsLastDeletedUTC()
    {
        var clock = new FakeClock{UtcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)};
        var service = new ReverseService(clock);
        
        // Create entry
        var reverse = service.ReverseAndStore("abc");
        reverse.ok.Should().BeTrue();
        
        // Grab the id from history
        var entry = service.GetHistorySnapshot(null, HistoryOrder.Desc, includeDeleted: true).First();
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
        var clock = new FakeClock{UtcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)};
        var service = new ReverseService(clock);
        
        // Create entry
        var reverse = service.ReverseAndStore("abc");
        reverse.ok.Should().BeTrue();
        
        // Grab the id from history
        var entry = service.GetHistorySnapshot(null, HistoryOrder.Desc, includeDeleted: true).First();
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
        var clock = new FakeClock{UtcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)};
        var service = new ReverseService(clock);
        
        // Create entry
        var reverse = service.ReverseAndStore("abc");
        reverse.ok.Should().BeTrue();
        
        // Grab the id from history
        var entry = service.GetHistorySnapshot(null, HistoryOrder.Desc, includeDeleted: true).First();
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
        var clock = new FakeClock{UtcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)};
        var service = new ReverseService(clock);
        
        // Create 3 entries
        var reverse = service.ReverseAndStore("abc");
        reverse.ok.Should().BeTrue();
        reverse = service.ReverseAndStore("def");
        reverse.ok.Should().BeTrue();
        reverse = service.ReverseAndStore("ghi");
        reverse.ok.Should().BeTrue();
        
        // Grab the snapshot
        var snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.Active, includeDeleted: false);
        snapshot.Count.Should().Be(3);
        
        //Delete the middle item
        var del = service.DeleteHistoryItem(snapshot[1].Id);
        del.Item1.Should().NotBeNull();
        
        // Get snapshot again with active status and ensure the middle item is not returned
        snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.Active, includeDeleted: false);
        snapshot.Count.Should().Be(2);
        snapshot.Should().NotContain(x => x.Id == del.Item1.Id);
    }
    
    [Fact]
    public void ReverseService_GetSnapshot_DeletedStatusReturnsOnlyDeletedEntries()
    {
        var clock = new FakeClock{UtcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)};
        var service = new ReverseService(clock);
        
        // Create 3 entries
        var reverse = service.ReverseAndStore("abc");
        reverse.ok.Should().BeTrue();
        reverse = service.ReverseAndStore("def");
        reverse.ok.Should().BeTrue();
        reverse = service.ReverseAndStore("ghi");
        reverse.ok.Should().BeTrue();
        
        // Grab the snapshot
        var snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.Active, includeDeleted: false);
        snapshot.Count.Should().Be(3);
        
        //Delete the middle item
        var del = service.DeleteHistoryItem(snapshot[1].Id);
        del.Item1.Should().NotBeNull();
        
        // Get snapshot again with deleted status and ensure the middle item is the only one returned
        snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.Deleted, includeDeleted: false);
        snapshot.Count.Should().Be(1);
        snapshot.Should().Contain(x => x.Id == del.Item1.Id);
    }
    
    [Fact]
    public void ReverseService_GetSnapshot_EverDeletedStatusReturnsOnlyEntriesThatHaveBeenDeleted()
    {
        var clock = new FakeClock{UtcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)};
        var service = new ReverseService(clock);
        
        // Create 4 entries
        var reverse = service.ReverseAndStore("abc");
        reverse.ok.Should().BeTrue();
        reverse = service.ReverseAndStore("def");
        reverse.ok.Should().BeTrue();
        reverse = service.ReverseAndStore("ghi");
        reverse.ok.Should().BeTrue();
        reverse = service.ReverseAndStore("jkl");
        reverse.ok.Should().BeTrue();
        
        // Grab the snapshot
        var snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.Active, includeDeleted: false);
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
        snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.EverDeleted, includeDeleted: false);
        snapshot.Count.Should().Be(2);
        snapshot.Should().Contain(x => x.Id == del1.Item1.Id);
        snapshot.Should().Contain(x => x.Id == del2.Item1.Id);
    }

    [Fact]
    public void ReverseService_GetSnapshot_NeverDeletedStatusReturnsOnlyEntriesThatHaveNotBeenDeleted()
    {
        var clock = new FakeClock{UtcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)};
        var service = new ReverseService(clock);
        
        // Create 4 entries
        var reverse = service.ReverseAndStore("abc");
        reverse.ok.Should().BeTrue();
        reverse = service.ReverseAndStore("def");
        reverse.ok.Should().BeTrue();
        reverse = service.ReverseAndStore("ghi");
        reverse.ok.Should().BeTrue();
        reverse = service.ReverseAndStore("jkl");
        reverse.ok.Should().BeTrue();
        
        // Grab the snapshot
        var snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.Active, includeDeleted: false);
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
        snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.NeverDeleted, includeDeleted: false);
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
        var clock = new FakeClock{UtcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)};
        var service = new ReverseService(clock);
        
        // Create 4 entries
        var reverse = service.ReverseAndStore("abc");
        reverse.ok.Should().BeTrue();
        reverse = service.ReverseAndStore("def");
        reverse.ok.Should().BeTrue();
        reverse = service.ReverseAndStore("ghi");
        reverse.ok.Should().BeTrue();
        reverse = service.ReverseAndStore("jkl");
        reverse.ok.Should().BeTrue();
        
        // Grab the snapshot
        var snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.Active, includeDeleted: false);
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
        snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.NeverDeleted, includeDeleted: false);
        snapshot.Count.Should().Be(2);
        snapshot.Should().Contain(x => x.Id == neverDeleted1.Id);
        snapshot.Should().Contain(x => x.Id == neverDeleted2.Id);
        snapshot.Should().NotContain(x => x.Id == restore1.Item1!.Id);
        snapshot.Should().NotContain(x => x.Id == restore2.Item1!.Id);
        snapshot.Should().NotContain(x => x.LastDeletedUTC != null);
        
        // Get snapshot again with Active and ensure the two restored entries are returned
        snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.Active, includeDeleted: false);
        snapshot.Count.Should().Be(4);
        snapshot.Should().Contain(x => x.Id == restore1.Item1.Id);
        snapshot.Should().Contain(x => x.Id == restore2.Item1.Id);
        snapshot.Should().NotContain(x => x.IsDeleted == true);
    }
    
    [Fact]
    public void ReverseService_GetSnapshot_EverDeletedStatusReturnsBothCurrentlyDeletedAndRestoredEntries()
    {
        var clock = new FakeClock{UtcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)};
        var service = new ReverseService(clock);
        
        // Create 4 entries
        var reverse = service.ReverseAndStore("abc");
        reverse.ok.Should().BeTrue();
        reverse = service.ReverseAndStore("def");
        reverse.ok.Should().BeTrue();
        reverse = service.ReverseAndStore("ghi");
        reverse.ok.Should().BeTrue();
        reverse = service.ReverseAndStore("jkl");
        reverse.ok.Should().BeTrue();
        
        // Grab the snapshot
        var snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.Active, includeDeleted: false);
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
        snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, HistoryStatus.EverDeleted, includeDeleted: false);
        snapshot.Count.Should().Be(2);
        snapshot.Should().Contain(x => x.Id == del2.Item1.Id);
        snapshot.Should().Contain(x => x.Id == restore1.Item1.Id);
        snapshot.Should().NotContain(x => x.LastDeletedUTC == null);
    }
}
