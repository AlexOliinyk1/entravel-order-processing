using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderProcessing.Shared.Messaging;
using OrderProcessing.Worker.Processing;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderProcessing.Worker.Hosting;

public sealed class OrderConsumerHostedService(
    IOptions<QueueOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<OrderConsumerHostedService> logger) : IHostedService
{
    private readonly QueueOptions _options = options.Value;
    private IConnection? _connection;
    private IModel? _channel;
    private string? _consumerTag;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        const int maxAttempts = 10;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                _connection = RabbitMqConnectionFactory.Create(_options);
                break;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(attempt * 2, 10));
                logger.LogWarning(ex,
                    "RabbitMQ not ready (attempt {Attempt}/{Max}). Retrying in {Delay}s.",
                    attempt, maxAttempts, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
            }
        }

        _channel = _connection!.CreateModel();
        RabbitMqTopology.Declare(_channel, _options);
        _channel.BasicQos(prefetchSize: 0, prefetchCount: _options.PrefetchCount, global: false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += OnMessageAsync;

        _consumerTag = _channel.BasicConsume(queue: _options.Queue, autoAck: false, consumer: consumer);

        logger.LogInformation(
            "Consumer started on {Queue} (prefetch={Prefetch}, consumerTag={Tag})",
            _options.Queue, _options.PrefetchCount, _consumerTag);
    }

    private async Task OnMessageAsync(object sender, BasicDeliverEventArgs args)
    {
        if (_channel is null) return;

        OrderCreatedMessage? message = null;
        try
        {
            var body = Encoding.UTF8.GetString(args.Body.Span);
            message = JsonSerializer.Deserialize<OrderCreatedMessage>(body);

            if (message is null || message.OrderId == Guid.Empty)
            {
                logger.LogError("Invalid OrderCreatedMessage payload: {Payload}", body);
                _channel.BasicNack(args.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            using var scope = scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IOrderProcessor>();
            await processor.ProcessAsync(message.OrderId, CancellationToken.None);

            _channel.BasicAck(args.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to process order {OrderId}, sending to DLQ.",
                message?.OrderId);
            _channel.BasicNack(args.DeliveryTag, multiple: false, requeue: false);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_consumerTag is not null && _channel is { IsOpen: true })
            {
                _channel.BasicCancel(_consumerTag);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error cancelling consumer.");
        }

        try { _channel?.Close(); } catch { /* ignore */ }
        try { _connection?.Close(); } catch { /* ignore */ }

        _channel?.Dispose();
        _connection?.Dispose();

        logger.LogInformation("Consumer stopped.");
        return Task.CompletedTask;
    }
}
