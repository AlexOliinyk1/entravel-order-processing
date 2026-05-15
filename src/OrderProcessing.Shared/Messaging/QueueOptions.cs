namespace OrderProcessing.Shared.Messaging;

public sealed class QueueOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";

    public string Exchange { get; set; } = "orders.exchange";
    public string Queue { get; set; } = "orders.created";
    public string RoutingKey { get; set; } = "order.created";

    public string DeadLetterExchange { get; set; } = "orders.dlx";
    public string DeadLetterQueue { get; set; } = "orders.dlq";

    public ushort PrefetchCount { get; set; } = 8;
}
