namespace WebApplication1.Models;

public record ApiError(
            string Error,   
            string Code,
            string Message);