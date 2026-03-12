namespace WebApplication1.Models;

public class HistoryQueryFactory
{
    public static (bool ok, HistoryQueryOptions? options, string? message) 
        BuildHistoryQuery(int? limit, HistoryOrder? order, string? status, string? query, bool? includeDeleted, int? minLength, int? maxLength)
    {
        //================
        //Validation Begin
        //================
        
        // order should be Asc or Desc
        if (order != HistoryOrder.Asc && order != HistoryOrder.Desc)
            return (false, null, "Order should be asc or desc");
        
        // status should be an allowed value, instantiates a parsedStatus value if validated
        if (!TryParseStatus(status, out var parsedStatus))
            return (false, null, "Status should be an allowed value");
        
        // if provided, limit should be greater than 0
        if (limit != null && limit <= 0)
            return (false, null, "Limit should be greater than 0 or empty.");
        
        //if provided, minLength should be greater than or equal to 0
        if (minLength is < 0)
            return (false, null, "MinLength should be greater than or equal to 0 or empty.");
        
        // if provided, maxLength should be greater than or equal to 0
        if (maxLength is < 0)
            return (false, null, "MaxLength should be greater than or equal to 0 or empty.");
        
        // if both minLength and maxLength are provided, min length should be less than or equal to max length
        if (minLength != null && maxLength != null && minLength > maxLength)
            return (false, null, "MinLength should be less than or equal to MaxLength");
        
        //================
        //Validation End
        //================
        
        return (true, new HistoryQueryOptions(limit, order, parsedStatus, query, includeDeleted, minLength, maxLength), "Query built successfully");
    }

    private static bool TryParseStatus(string? status, out HistoryStatus? parsedStatus)
    {
        parsedStatus = null;
        
        if (string.IsNullOrWhiteSpace(status))
            return true;
        
        if(!Enum.TryParse<HistoryStatus>(status, ignoreCase: true, out var parseResult))
            return false;
        
        if (!Enum.IsDefined(typeof(HistoryStatus), parseResult))
            return false;
        
        parsedStatus = parseResult;
        return true;
    }
}