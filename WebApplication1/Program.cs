using WebApplication1.Models;
using WebApplication1.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IClock, SystemClock>();

// Register ReverseService as singleton
builder.Services.AddSingleton<ReverseService>();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapGet("/ping", () => Results.Ok(new
{
    status = "ok",
    utc =DateTime.UtcNow
}));

app.MapGet("/history", (int? limit, string? order, string? status, string? query, int? minLength, int? maxLength, bool? includeDeleted, ReverseService service) =>
{
    // Setup query
    var queryOutput = HistoryQueryFactory.BuildHistoryQuery(limit, order, status, query, includeDeleted, minLength, maxLength);

    if (queryOutput is { ok: true, options: not null })
    {
        List<HistoryEntry> history = service.GetHistorySnapshot(queryOutput.options);

        return Results.Ok(new
        {
            count = history.Count,
            items = history
        });
    }
    else
    {
        return Results.BadRequest(new { error = "validation_failed", message = queryOutput.message });
    }
});

app.MapGet("/history/{id:guid}", (Guid id, bool? includeDeleted, ReverseService service) =>
{
    var entry = service.GetHistoryItem(id, includeDeleted ?? false);

    return entry is null ? Results.NotFound() : Results.Ok(entry);
});

app.MapPost("/reverse", async (ReverseRequest request, ReverseService service) =>
{
    //validate text
    if (request.Text is null)
    {
        // Return a structured 400 payload.
        return Results.BadRequest(new
        {
            error = "validation_failed",
            message = "Text is required."
        });
    }
    
    // ====== Real async shape ======
    //
    // This is intentionally small:
    // We insert a tiny await so you get an endpoint that genuinely awaits something.
    // In real apps this would be:
    // - database call
    // - HTTP call
    // - file IO
    //
    // Today it’s just training wheels to make async "real" without scope creep.
    await Task.Yield();
    
    // Reverse via service
    var result = service.ReverseAndStore(request.Text);
    
    return result.ok ? Results.Ok(new { original = request.Text, reversed = result.reversed}) : 
        Results.BadRequest(new { error = "validation_failed", message = result.message });
});

app.MapDelete("/history", (int? olderThanMinutes, ReverseService service) =>
{
    // Ensure olderThanMinutes is not null and is greater than zero, otherwise 400
    if (!olderThanMinutes.HasValue)
        return Results.BadRequest(new { error = "validation_failed", message = "OlderThanMinutes is required." });
    
    if (olderThanMinutes <= 0)
        return Results.BadRequest(new { error = "validation_failed", message = "OlderThanMinutes must be greater than 0." });
    
    // Convert minutes to TimeSpan
    var age = TimeSpan.FromMinutes(olderThanMinutes.Value);
    
    // Clear history items older than age
    var (removed, remaining) = service.ClearActiveHistoryItemsOlderThan(age);

    return Results.Ok(new { removed = removed, remaining = remaining });
});

app.MapDelete("/history/{id:guid}", (Guid id, ReverseService service) =>
{
    // Find the entry with the given id
    var result = service.DeleteHistoryItem(id);
    
    var entry = result.Item1;
    var alreadyDeleted = result.Item2;
    
    if (entry is null)
        return Results.NotFound();
    
    return Results.Ok(new {isDeleted = entry.IsDeleted, deletedUTC = entry.DeletedUTC, alreadyDeleted});
});

app.MapPost("/history/{id:guid}/restore", (Guid id, ReverseService service) =>
{
    var result = service.RestoreHistoryItem(id);
    
    var entry = result.Item1;
    var alreadyActive = result.Item2;
    
    if (entry is null)
        return Results.NotFound();
    
    return Results.Ok(new {isDeleted = entry.IsDeleted, deletedUTC = entry.DeletedUTC, alreadyActive});
});

app.Run();