using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using WebApplication1.Models;

namespace WebApplication1.Tests;

public class IntegrationTests
{
    [Fact]
    public async Task HTTP_ReverseService_GetHistory_NegativeOffsetReturnsStructured400()
    {
        var factory = new WebApplicationFactory<Program>();
        
        var client = factory.CreateClient();
        
        var response = await client.GetAsync("/history?offset=-1");
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
        var response = await client.PostAsJsonAsync("/reverse?", new ReverseRequest { Text = "abc" });
        response = await client.PostAsJsonAsync("/reverse?", new ReverseRequest { Text = "def" });
        response = await client.PostAsJsonAsync("/reverse?", new ReverseRequest { Text = "ghi" });
        response = await client.PostAsJsonAsync("/reverse?", new ReverseRequest { Text = "jkl" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        response = await client.GetAsync("/history?limit=2&offset=1");
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
        
        var response = await client.PostAsJsonAsync("/reverse?", new ReverseRequest());
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
        var response = await client.GetAsync($"/history/{missingId}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        
        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        error.Should().NotBeNull();
        error!.Error.Should().Be("not_found");
        error.Code.Should().Be(ApiErrorCodes.HistoryItemNotFound);
    }
}