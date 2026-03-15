namespace WebApplication1.Models;

public class HistoryQueryFactory
{
    public static (bool ok, HistoryQueryOptions? options, ApiError? error) 
        BuildHistoryQuery(int? limit, string? order, string? status, string? query, bool? includeDeleted, int? minLength, int? maxLength)
    {
        //================
        //Validation Begin
        //================
        
        
        //order must be "asc" or "desc"
        order ??= "desc";
    
        // var historyOrder is HistoryOrder.Asc if order = asc, HistoryOrder.Desc if order = desc, otherwise return false
        if (!TryParseOrder(order, out var historyOrder))
            return (false, null, new ApiError("validation_failed",  ApiErrorCodes.InvalidOrder, "Order should be either 'asc' or 'desc'."));
        
        // status should be an allowed value, instantiates a parsedStatus value if validated
        if (!TryParseStatus(status, out var parsedStatus))
            return (false, null, new ApiError("validation_failed",  ApiErrorCodes.InvalidStatus, "Status should be an allowed value."));
        
        // if provided, limit should be greater than 0
        if (limit != null && limit <= 0)
            return (false, null, new ApiError("validation_failed",  ApiErrorCodes.LimitMustBePositive, "Limit should be greater than 0."));

       
        //if provided, minLength should be greater than or equal to 0
        if (minLength is < 0)
            return (false, null, new ApiError("validation_failed",  ApiErrorCodes.MinLengthNegative, "MinLength should be greater than or equal to 0 or empty."));
        
        // if provided, maxLength should be greater than or equal to 0
        if (maxLength is < 0)
            return (false, null, new ApiError("validation_failed",  ApiErrorCodes.MaxLengthNegative, "MaxLength should be greater than or equal."));
        
        // if both minLength and maxLength are provided, min length should be less than or equal to max length
        if (minLength != null && maxLength != null && minLength > maxLength)
            return (false, null, new ApiError("validation_failed",  ApiErrorCodes.MinLLengthGreaterThanMaxLength, "MinLength should be less than or equal to MaxLength"));
        
        // If includeDeleted is null, default to false
        var includeDeletedValue = includeDeleted ?? false;
        
        //================
        //Validation End
        //================
        
        return (true, new HistoryQueryOptions(limit, historyOrder, parsedStatus, query, includeDeletedValue, minLength, maxLength), null);
    }

    private static bool TryParseStatus(string? status, out HistoryStatus? parsedStatus)
    {
        parsedStatus = null;
        
        if (string.IsNullOrWhiteSpace(status))
            return true;
        
        if(!Enum.TryParse<HistoryStatus>(status, ignoreCase: true, out var parseResult) ||
                !Enum.IsDefined(typeof(HistoryStatus), parseResult))
            return false;
        
        parsedStatus = parseResult;
        return true;
    }

    private static bool TryParseOrder(string? order, out HistoryOrder parsedOrder)
    {
        parsedOrder = HistoryOrder.Desc;

        if (string.IsNullOrWhiteSpace(order))
            return true;

        if (!Enum.TryParse<HistoryOrder>(order, ignoreCase: true, out var orderResult) ||
                !Enum.IsDefined(typeof(HistoryOrder), orderResult))
            return false;

        parsedOrder = orderResult;
        return true;
    }
}