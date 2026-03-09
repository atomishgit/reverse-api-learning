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
}
