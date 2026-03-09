namespace WebApplication1.Services;

public interface IClock
{
    DateTime UtcNow { get; }
}