using RabbitMQ.Client;

namespace OrderProcessing.Shared.Messaging;

public static class RabbitMqTopology
{
    public static void Declare(IModel channel, QueueOptions options)
    {
        channel.ExchangeDeclare(
            exchange: options.DeadLetterExchange,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false);

        channel.QueueDeclare(
            queue: options.DeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        channel.QueueBind(
            queue: options.DeadLetterQueue,
            exchange: options.DeadLetterExchange,
            routingKey: options.RoutingKey);

        channel.ExchangeDeclare(
            exchange: options.Exchange,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false);

        var queueArgs = new Dictionary<string, object>
        {
            ["x-dead-letter-exchange"] = options.DeadLetterExchange,
            ["x-dead-letter-routing-key"] = options.RoutingKey
        };

        channel.QueueDeclare(
            queue: options.Queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArgs);

        channel.QueueBind(
            queue: options.Queue,
            exchange: options.Exchange,
            routingKey: options.RoutingKey);
    }
}
