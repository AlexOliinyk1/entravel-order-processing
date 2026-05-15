using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace OrderProcessing.Shared.Messaging;

public sealed class RabbitMqOrderQueuePublisher : IOrderQueuePublisher, IDisposable
{
    private readonly ILogger<RabbitMqOrderQueuePublisher> _logger;
    private readonly QueueOptions _options;
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly object _publishLock = new();

    public RabbitMqOrderQueuePublisher(IOptions<QueueOptions> options, ILogger<RabbitMqOrderQueuePublisher> logger)
    {
        _logger = logger;
        _options = options.Value;
        _connection = RabbitMqConnectionFactory.Create(_options);
        _channel = _connection.CreateModel();
        RabbitMqTopology.Declare(_channel, _options);
    }

    public void Publish(OrderCreatedMessage message)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message);

        lock (_publishLock)
        {
            var properties = _channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.ContentType = "application/json";
            properties.MessageId = message.OrderId.ToString();

            _channel.BasicPublish(
                exchange: _options.Exchange,
                routingKey: _options.RoutingKey,
                basicProperties: properties,
                body: body);
        }

        _logger.LogInformation(
            "Published order {OrderId} to {Exchange}/{RoutingKey}",
            message.OrderId, _options.Exchange, _options.RoutingKey);
    }

    public void Dispose()
    {
        try { _channel.Close(); } catch { /* ignore */ }
        try { _connection.Close(); } catch { /* ignore */ }
        _channel.Dispose();
        _connection.Dispose();
    }
}
