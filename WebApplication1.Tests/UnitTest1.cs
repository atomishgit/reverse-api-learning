using System.Net;
using System.Net.Http.Json;
using WebApplication1.Services;

using FluentAssertions;
using WebApplication1.Models;

namespace WebApplication1.Tests;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

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

    private static HistoryResponse GetSnapshot(ReverseService service, int? limit = null, HistoryOrder order = HistoryOrder.Desc,
        HistoryStatus? status = null,  string? query = null, bool includeDeleted = false, int? minLength = null, int? maxLength = null, int? offset = null)
    {
        var options = new HistoryQueryOptions(
            limit,
            order,
            status,
            query,
            includeDeleted,
            minLength,
            maxLength,
            offset
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
        result.error.Code.Should().Be(ApiErrorCodes.TextTooLong);
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
        var entry = GetSnapshot(service, order: HistoryOrder.Desc, includeDeleted: false, query: null).Items.First();
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
        var entry = GetSnapshot(service, order: HistoryOrder.Desc, includeDeleted: true, query: null).Items.First();
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

        var snapshot = GetSnapshot(service, order: HistoryOrder.Asc, includeDeleted: false).Items;
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
        var entry = GetSnapshot(service, order: HistoryOrder.Desc, includeDeleted: true).Items.First();

        //var entry = service.GetHistorySnapshot(null, HistoryOrder.Desc, includeDeleted: true).First();
        var id = entry.Id;
        
        // Soft delete it
        var del = service.DeleteHistoryItem(id);
        
        // Assert Item was deleted
        del.Item1.Should().NotBeNull();
        
        // Ensure item is not visible in snapshot by default
        var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, includeDeleted: false).Items;

        //var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, includeDeleted: false);

        //var snapshot = service.GetHistorySnapshot(null, HistoryOrder.Desc, includeDeleted: false);
        snapshot.Should().NotContain(x => x.Id == id);
        
        // Restore it
        var restore = service.RestoreHistoryItem(id);
        restore.Item1.Should().NotBeNull();

        restore.Item1!.IsDeleted.Should().BeFalse();
        restore.Item1!.DeletedUTC.Should().BeNull();
        
        // Ensure item is visible in snapshot again
        var snapshot2 = GetSnapshot(service, order: HistoryOrder.Desc, includeDeleted: false).Items;

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
        var entry = GetSnapshot(service, order: HistoryOrder.Desc, includeDeleted: true).Items.First();

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
        var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, includeDeleted: true).Items;

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
        var entry = GetSnapshot(service, order: HistoryOrder.Desc, includeDeleted: true).Items.First();

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
        var entry = GetSnapshot(service, order: HistoryOrder.Desc, includeDeleted: true).Items.First();

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
        var entry = GetSnapshot(service, order: HistoryOrder.Desc, includeDeleted: true).Items.First();

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
        var entry = GetSnapshot(service, order: HistoryOrder.Desc, includeDeleted: true).Items.First();

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
        var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, includeDeleted: false).Items;

        snapshot.Count.Should().Be(3);
        
        //Delete the middle item
        var del = service.DeleteHistoryItem(snapshot[1].Id);
        del.Item1.Should().NotBeNull();
        
        // Get snapshot again with active status and ensure the middle item is not returned
         snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, includeDeleted: false).Items;

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
        var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, includeDeleted: false).Items;

        snapshot.Count.Should().Be(3);
        
        //Delete the middle item
        var del = service.DeleteHistoryItem(snapshot[1].Id);
        del.Item1.Should().NotBeNull();
        
        // Get snapshot again with deleted status and ensure the middle item is the only one returned
        snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Deleted, includeDeleted: false).Items;

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
        var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, includeDeleted: false).Items;

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
        snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.EverDeleted, includeDeleted: false).Items;

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
        var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, includeDeleted: false).Items;

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
        snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.NeverDeleted, includeDeleted: false).Items;

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
        var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, includeDeleted: false).Items;

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
        snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.NeverDeleted, includeDeleted: false).Items;

        snapshot.Count.Should().Be(2);
        snapshot.Should().Contain(x => x.Id == neverDeleted1.Id);
        snapshot.Should().Contain(x => x.Id == neverDeleted2.Id);
        snapshot.Should().OnlyContain(x => x.Id != restore1.Item1!.Id);
        snapshot.Should().OnlyContain(x => x.Id != restore2.Item1!.Id);
        snapshot.Should().OnlyContain(x => x.LastDeletedUTC == null);
        
        // Get snapshot again with Active and ensure the two restored entries are returned
        snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, includeDeleted: false).Items;

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
        var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, includeDeleted: false).Items;


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
        snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.EverDeleted, includeDeleted: false).Items;


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
        var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, query: "he", includeDeleted: false).Items;

        snapshot.Count.Should().Be(1);
        snapshot[0].Original.Should().Be("Hello");
        
        snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, query: "EL", includeDeleted: false).Items;

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
        var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, query: "OLL", includeDeleted: false).Items;

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
        var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, query: " ", includeDeleted: false).Items;

        snapshot.Count.Should().Be(3);
    }

    [Fact]
    public void ReverseService_GetSnapshot_StatusAndQueryCombineCorrectly()
    {
        var service = CreateService(out var clock);
        
        // Create 4 entries
        CreateEntries(service, "abcd", "efgh", "ijkl", "mnop");
        
        //Delete efgh entry
        var historyItemId = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, query: "ef", includeDeleted: false).Items.First().Id;
        var del = service.DeleteHistoryItem(historyItemId);
        
        // Grab the snapshot
        var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, query: "ef", includeDeleted: false);

        snapshot.Count.Should().Be(0);
        
        snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Deleted, query: "ef", includeDeleted: false);
        snapshot.Count.Should().Be(1);
    }

    [Fact]
    public void HistoryQueryFactory_BuildHistoryQuery_MinLengthGreaterThanMaxLengthReturnsError()
    {
        var service = CreateService(out var clock);
        
        CreateEntries(service, "abcd", "efgh", "ijkl", "mnop");
        
        // Build HistoryQuery
        var result = HistoryQueryFactory.BuildHistoryQuery(
            limit: null,
            order: "desc",
            status: null,
            query: null,
            includeDeleted: false,
            minLength: 5,
            maxLength: 3,
            offset: null
        );
        
        result.ok.Should().BeFalse();
        result.options.Should().BeNull();
        result.error.Code.Should().Be(ApiErrorCodes.MinLengthGreaterThanMaxLength);
    }

    [Fact]
    public void HistoryQueryFactory_BuildHistoryQuery_ZeroLengthIsValid()
    {
        var service = CreateService(out var clock);
        
        CreateEntries(service, "abc", "def", "ghi", "jkl");
        
        // Build HistoryQuery
        var result = HistoryQueryFactory.BuildHistoryQuery(
            limit: null,
            order: "desc",
            status: null,
            query: null,
            includeDeleted: false,
            minLength: 0,
            maxLength: 3,
            offset: null
        );
        
        result.ok.Should().BeTrue();
        result.options.Should().NotBeNull();
        result.error.Should().BeNull();
    }

    [Fact]
    public void HistoryQueueFactory_BuildHistoryQueue_MinAndMaxLengthAreInclusivelyValid()
    {
        var service = CreateService(out var clock);
        
        CreateEntries(service, "ab", "def", "ghijk", "lmnopq");
        
        // Build HistoryQuery
        var result = HistoryQueryFactory.BuildHistoryQuery(
            limit: null,
            order: "desc",
            status: null,
            query: null,
            includeDeleted: false,
            minLength: 3,
            maxLength: 5,
            offset: null
        );
        
        result.ok.Should().BeTrue();
        result.options.Should().NotBeNull();
        result.error.Should().BeNull();
        
        var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, minLength: 3, maxLength: 5).Items;
        snapshot.Count.Should().Be(2);
        snapshot.Should().Contain(x => x.Original == "def");
        snapshot.Should().Contain(x => x.Original == "ghijk");
    }

    [Fact]
    public void HistoryQueueFactory_BuildHistoryQueue_CombinedFiltersWorkProperly()
    {
        var service = CreateService(out var clock);
        
        CreateEntries(service, "ab", "def", "abcde", "lmnopq", "rstuv", "wxyz");
        
        var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, query: "ab", includeDeleted: false).Items;

        var itemToDeleteId1 = snapshot.First().Id;
        var itemToDeleteId2 = snapshot.Skip(1).First().Id;
        var deleted1 = service.DeleteHistoryItem(itemToDeleteId1);
        var deleted2 = service.DeleteHistoryItem(itemToDeleteId2);
        
        // Build HistoryQuery
        var result = HistoryQueryFactory.BuildHistoryQuery(
            limit: null,
            order: "desc",
            status: "deleted",
            query: "ab",
            includeDeleted: false,
            minLength: 3,
            maxLength: 5,
            offset: null
        );
        
        result.ok.Should().BeTrue();
        result.options.Should().NotBeNull();
        result.error.Should().BeNull();
        
        snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Deleted, query: "ab", minLength: 3, maxLength: 5).Items;
        snapshot.Count.Should().Be(1);
        snapshot.Should().Contain(x => x.Original == "abcde");
    }

    [Fact]
    public void ReverseService_GetHistorySummary_MixedFilterReturnsCorrectSummary()
    {
        var service = CreateService(out var clock);
        
        CreateEntries(service, "abc", "defg", "ghijk", "jklmnop", "qrstuvwxyz");
        
        // Grab the snapshot
        var snapshot = GetSnapshot(service, order: HistoryOrder.Desc, status: HistoryStatus.Active, includeDeleted: false).Items;
        
        // Delete the entry "ghijk"
        var deleted = service.DeleteHistoryItem(snapshot.First(x => x.Original == "ghijk").Id);
        
        // Delete item "abc"
        var deleted2 = service.DeleteHistoryItem(snapshot.First(x => x.Original == "abc").Id);
        
        // Restore the abc item 
        var restored = service.RestoreHistoryItem(deleted2.Item1!.Id);

        var queryOptions = HistoryQueryFactory.BuildHistoryQuery(null, null, null, null, true, null, null, null);

        var summary = service.GetHistorySummary(queryOptions.options);
        summary.Count.Should().Be(5);
        summary.ActiveCount.Should().Be(4);
        summary.DeletedCount.Should().Be(1);
        summary.EverDeletedCount.Should().Be(2);
        summary.NeverDeletedCount.Should().Be(3);
    }

    [Fact]
    public void ReverseService_GetHistorySummary_SummaryRespectsQueryFilters()
    {
        var service = CreateService(out var clock);
        
        CreateEntries(service, "abc", "defg", "ghijk", "abjklmnop", "qrstuvwxyz", "fauabak", "lollolab");
        
        
        // Build a query object for the string "ab"
        var queryOptions = HistoryQueryFactory.BuildHistoryQuery(null, null, null, "ab", true, null, null,  null);
        
        // Ensure summary only returns only info on the 4 matches
        var summary = service.GetHistorySummary(queryOptions.options);
        summary.Count.Should().Be(4);
        summary.ActiveCount.Should().Be(4);
        summary.DeletedCount.Should().Be(0);
        summary.EverDeletedCount.Should().Be(0);
        summary.NeverDeletedCount.Should().Be(4);
        summary.MinLength.Should().Be(3);
        summary.MaxLength.Should().Be(9);
    }

    [Fact]
    public void ReverseService_GetHistorySummary_SummaryRespectsLengthFilters()
    {
        var service = CreateService(out var clock);
        
        CreateEntries(service, "ab", "def", "ghijk", "abjklm");
        
        
        // Build a query object for minlength 3 and max 5
        var queryOptions = HistoryQueryFactory.BuildHistoryQuery(null, null, null, null, true, 3, 5, null);
        
        // Ensure summary only returns only info on the 2 matches
        var summary = service.GetHistorySummary(queryOptions.options);
        summary.Count.Should().Be(2);
        summary.ActiveCount.Should().Be(2);
        summary.MinLength.Should().Be(3);
        summary.MaxLength.Should().Be(5);
        summary.AverageLength.Should().Be(4);
    }

    [Fact]
    public void ReverseService_GetHistorySummary_EmptyFilteredSetReturnsCorrectSummary()
    {
        var service = CreateService(out var clock);
        
        CreateEntries(service, "abc", "defg", "ghijk", "abjklmnop", "qrstuvwxyz", "fauabak", "lollolab");
        
        
        // Build a query object for the string "zzz"
        var queryOptions = HistoryQueryFactory.BuildHistoryQuery(null, null, null, "zzz", true, null, null, null);
        
        // Ensure summary returns a count of 0, 0 in all lifecycle counts and null length and date fields
        var summary = service.GetHistorySummary(queryOptions.options);
        summary.Count.Should().Be(0);
        summary.ActiveCount.Should().Be(0);
        summary.DeletedCount.Should().Be(0);
        summary.EverDeletedCount.Should().Be(0);
        summary.NeverDeletedCount.Should().Be(0);
        summary.MinLength.Should().BeNull();
        summary.MaxLength.Should().BeNull();
        summary.AverageLength.Should().BeNull();
        summary.OldestCreatedUtc.Should().BeNull();
        summary.NewestCreatedUtc.Should().BeNull();
    }

    [Fact]
    public void HistoryQueryFactory_BuildHistoryQuery_ReturnsStructuredErrorForInvalidRange()
    {
        var result = HistoryQueryFactory.BuildHistoryQuery(
            limit: 1000,
            order: "desc",
            status: "deleted",
            query: "ab",
            includeDeleted: false,
            minLength: 10,
            maxLength: 5,
            offset: null
        );

        result.ok.Should().BeFalse();
        result.options.Should().BeNull();
        result.error.Should().NotBeNull();
        result.error.Error.Should().Be("validation_failed");
        result.error.Code.Should().Be(ApiErrorCodes.MinLengthGreaterThanMaxLength);
    }
    
    [Fact]
    public void HistoryQueryFactory_BuildHistoryQuery_ReturnsStructuredErrorForInvalidOrder()
    {
        var result = HistoryQueryFactory.BuildHistoryQuery(
            limit: 1000,
            order: "ass",
            status: "deleted",
            query: "ab",
            includeDeleted: false,
            minLength: 3,
            maxLength: 5,
            offset: null
        );

        result.ok.Should().BeFalse();
        result.options.Should().BeNull();
        result.error.Should().NotBeNull();
        result.error.Error.Should().Be("validation_failed");
        result.error.Code.Should().Be(ApiErrorCodes.InvalidOrder);
    }

    [Fact]
    public void ReverseService_ReverseAndStore_RejectsTooLongTextWithStructuredError()
    {
        var service = CreateService(out var clock);
        
        var result = service.ReverseAndStore("This is a very long string that is over 100 characters long.This is a very long string that is over 100 characters long.");
        
        result.ok.Should().BeFalse();
        result.error.Should().NotBeNull();
        result.error.Error.Should().Be("validation_failed");
        result.error.Code.Should().Be(ApiErrorCodes.TextTooLong);
    }

    [Fact]
    public void ReverseService_GetHistorySnapshot_NegativeOffsetReturnsStructuredError()
    {
        var service = CreateService(out var clock);
        
        CreateEntries(service, "abc", "def", "ghi", "jkl");
        
        var result = HistoryQueryFactory.BuildHistoryQuery(
            limit: 1000,
            order: "asc",
            status: "deleted",
            query: "ab",
            includeDeleted: false,
            minLength: 3,
            maxLength: 5,
            offset: -1
        );

        result.ok.Should().BeFalse();
        result.error.Should().NotBeNull();
        result.error.Code.Should().Be(ApiErrorCodes.OffsetNegative);
    }

    [Fact]
    public void ReverseService_GetHistorySnapshot_OffsetSkipsCorrectNumberOfOrderedResults()
    {
        var service = CreateService(out var clock);
        
        CreateEntries(service, "abc", "def", "ghi", "jkl");
        
        var snapshot = GetSnapshot(service, order: HistoryOrder.Asc, status: HistoryStatus.Active, includeDeleted: false).Items;
        
        snapshot.Count.Should().Be(4);
        
        var snapshot2 = GetSnapshot(service, order: HistoryOrder.Asc, status: HistoryStatus.Active, includeDeleted: false, offset: 1).Items;
        snapshot2.Count.Should().Be(3);
        snapshot2[0].Should().Be(snapshot[1]);
    }

    [Fact]
    public void ReverseService_GetHistorySnapshot_OffsetBeyondFilteredCountReturnsEmptyPage()
    {
        var service = CreateService(out var clock);
        
        CreateEntries(service, "abc", "def", "ghi", "jkl");
        
        var snapshot = GetSnapshot(service, order: HistoryOrder.Asc, status: HistoryStatus.Active, includeDeleted: false).Items;
        
        snapshot.Count.Should().Be(4);
        
        var snapshot2 = GetSnapshot(service, order: HistoryOrder.Asc, status: HistoryStatus.Active, includeDeleted: false, offset: 100).Items;

        snapshot2.Should().NotBeNull();
        snapshot2.Count.Should().Be(0); 
        
    }

    [Fact]
    public void ReverseService_GetHistorySnapshot_MetadataReflectsFilteredTotalInsteadOfReturnedCount()
    {
        var service = CreateService(out var clock);
        
        CreateEntries(service, "abc", "abdef", "abghi", "abjkl", "akka", "abababbba");
        
        var snapshotResult = GetSnapshot(service, order: HistoryOrder.Asc, status: HistoryStatus.Active, query: "ab", includeDeleted: false, offset: 2, limit: 2);
        snapshotResult.Items.Count.Should().Be(2);
        snapshotResult.Total.Should().Be(5);
        snapshotResult.Count.Should().Be(2);
        snapshotResult.HasMore.Should().BeTrue();
        snapshotResult.Offset.Should().Be(2);
        snapshotResult.Limit.Should().Be(2);
    }
    
    [Fact]
    public async Task HTTP_ReverseService_GetHistory_NegativeOffsetReturnsStructured400()
    {
        var factory = new WebApplicationFactory<Program>();
        
        var client = factory.CreateClient();
        
        var response = client.GetAsync("/history?offset=-1").Result;
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        
        error.Should().NotBeNull();
        error!.Error.Should().Be("validation_failed");
        error.Code.Should().Be(ApiErrorCodes.OffsetNegative);
    }


    [Fact]
    public async Task HTTP_ReverseService_GetHistory_PagedHistoryReturnsCorrectMetadata()
    {
        var factory = new WebApplicationFactory<Program>();
        
        var client = factory.CreateClient();
        
        // Post a few entries via /reverse
        var response = client.PostAsJsonAsync("/reverse?", new ReverseRequest { Text = "abc" }).Result;
        response = client.PostAsJsonAsync("/reverse?", new ReverseRequest { Text = "def" }).Result;
        response = client.PostAsJsonAsync("/reverse?", new ReverseRequest { Text = "ghi" }).Result;
        response = client.PostAsJsonAsync("/reverse?", new ReverseRequest { Text = "jkl" }).Result;
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        response = client.GetAsync("/history?limit=2&offset=1").Result;
        var historyResponse = await response.Content.ReadFromJsonAsync<HistoryResponse>();
        
        historyResponse.Should().NotBeNull();
        historyResponse.Total.Should().Be(4);
        historyResponse.Count.Should().Be(2);
        historyResponse.HasMore.Should().BeTrue();
        historyResponse.Offset.Should().Be(1);
        historyResponse.Limit.Should().Be(2);
        historyResponse.Items.Count.Should().Be(historyResponse.Count);
    }

    [Fact]
    public async Task HTTP_ReverseService_PostReverse_NoTextReturnsStructured400()
    {
        var factory = new WebApplicationFactory<Program>();
        
        var client = factory.CreateClient();
        
        var response = client.PostAsJsonAsync("/reverse?", new ReverseRequest()).Result;
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        
        var error = await response.Content.ReadFromJsonAsync<ApiError>();

        error.Should().NotBeNull();
        error!.Error.Should().Be("validation_failed");
        error.Code.Should().Be(ApiErrorCodes.TextRequired);
    }

    [Fact]
    public async Task HTTP_ReverseService_GetHistoryItem_MissingItemIdReturnsStructured404()
    {
        var factory = new WebApplicationFactory<Program>();
        
        var client = factory.CreateClient();
        
        var missingId = Guid.NewGuid();
        var response = client.GetAsync($"/history/{missingId}").Result;
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        
        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        error.Should().NotBeNull();
        error!.Error.Should().Be("not_found");
        error.Code.Should().Be(ApiErrorCodes.HistoryItemNotFound);
    }
}
