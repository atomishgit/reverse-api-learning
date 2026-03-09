namespace WebApplication1.Services;

public sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; }
}