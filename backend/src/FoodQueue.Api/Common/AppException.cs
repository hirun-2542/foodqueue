namespace FoodQueue.Api.Common;

/// <summary>ข้อผิดพลาดทางธุรกิจที่แปลงเป็น ProblemDetails พร้อม field `code` คงที่และข้อความไทย</summary>
public sealed class AppException(string code, int status, string message) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

public static class ErrorCodes
{
    public const string SessionNotFound = "SESSION_NOT_FOUND";
    public const string SessionClosed = "SESSION_CLOSED";
    public const string SessionExpired = "SESSION_EXPIRED";
    public const string TableOccupied = "TABLE_OCCUPIED";
    public const string ItemNotFound = "ITEM_NOT_FOUND";
    public const string ItemUnavailable = "ITEM_UNAVAILABLE";
    public const string ItemNotInPackage = "ITEM_NOT_IN_PACKAGE";
    public const string QtyLimitExceeded = "QTY_LIMIT_EXCEEDED";
    public const string OrderItemsLimitExceeded = "ORDER_ITEMS_LIMIT_EXCEEDED";
    public const string EmptyOrder = "EMPTY_ORDER";
    public const string InvalidStatusTransition = "INVALID_STATUS_TRANSITION";
    public const string OrderNotEditable = "ORDER_NOT_EDITABLE";
    public const string OrdersInProgress = "ORDERS_IN_PROGRESS";
    public const string NegativeSubtotal = "NEGATIVE_SUBTOTAL";
    public const string SessionHasOrders = "SESSION_HAS_ORDERS";
    public const string AlreadyPaid = "ALREADY_PAID";
    public const string InsufficientAmount = "INSUFFICIENT_AMOUNT";
    public const string ValidationFailed = "VALIDATION_FAILED";

    private static readonly Dictionary<string, int> Statuses = new()
    {
        [SessionNotFound] = 404,
        [SessionClosed] = 409,
        [SessionExpired] = 409,
        [TableOccupied] = 409,
        [ItemNotFound] = 404,
        [ItemUnavailable] = 409,
        [ItemNotInPackage] = 422,
        [QtyLimitExceeded] = 422,
        [OrderItemsLimitExceeded] = 422,
        [EmptyOrder] = 422,
        [InvalidStatusTransition] = 409,
        [OrderNotEditable] = 409,
        [OrdersInProgress] = 409,
        [NegativeSubtotal] = 422,
        [SessionHasOrders] = 409,
        [AlreadyPaid] = 409,
        [InsufficientAmount] = 422,
        [ValidationFailed] = 400,
    };

    public static int StatusFor(string code) => Statuses.TryGetValue(code, out var s) ? s : 400;
}
