namespace OrderProcessing.Shared.Messaging;

public sealed record OrderCreatedMessage(Guid OrderId);
