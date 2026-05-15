namespace OrderProcessing.Worker.Processing;

public interface IOrderProcessor
{
    Task ProcessAsync(Guid orderId, CancellationToken cancellationToken);
}
