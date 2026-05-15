using OrderProcessing.Shared.Domain;

namespace OrderProcessing.Shared.Contracts;

public sealed record SubmitOrderRequest(
    string CustomerId,
    IReadOnlyCollection<SubmitOrderItemRequest> Items,
    decimal TotalAmount);

public sealed record SubmitOrderItemRequest(string Sku, int Quantity);

public sealed record SubmitOrderResponse(Guid OrderId, string Status);

public sealed record OrderItemResponse(string Sku, int Quantity, decimal UnitPrice);

public sealed record OrderResponse(
    Guid Id,
    string CustomerId,
    decimal RequestedTotalAmount,
    decimal? FinalTotalAmount,
    decimal? DiscountAmount,
    OrderStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt,
    string? FailureReason,
    IReadOnlyCollection<OrderItemResponse> Items);

public sealed record InventoryItemResponse(string Sku, string Name, int Quantity, decimal UnitPrice);
