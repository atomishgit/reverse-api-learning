namespace WebApplication1.Models;

public static class ApiErrorCodes
{
    //Validation Errors
    public const string TextRequired = "text_required";
    public const string TextTooLong = "text_too_long";
    public const string InvalidOrder = "invalid_order";
    public const string InvalidStatus = "invalid_status";
    public const string LimitMustBePositive = "limit_must_be_positive";
    public const string MinLengthNegative = "min_length_negative";
    public const string MaxLengthNegative = "max_length_negative";
    public const string MinLLengthGreaterThanMaxLength = "min_length_greater_than_max_length";
    public const string OlderThanRequired = "older_than_required";
    public const string OlderThanMustBePositive = "older_than_must_be_positive";
    
    // Not Found
    public const string HistoryItemNotFound = "history_item_not_found";
}