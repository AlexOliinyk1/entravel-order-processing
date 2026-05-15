namespace OrderProcessing.Shared.Domain;

public sealed class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string CustomerId { get; set; }
    public decimal RequestedTotalAmount { get; set; }
    public decimal? FinalTotalAmount { get; set; }
    public decimal? DiscountAmount { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? FailureReason { get; set; }
    public List<OrderItem> Items { get; set; } = [];
}
