namespace OrderProcessing.Shared.Messaging;

public interface IOrderQueuePublisher
{
    void Publish(OrderCreatedMessage message);
}
