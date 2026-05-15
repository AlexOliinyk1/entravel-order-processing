using RabbitMQ.Client;

namespace OrderProcessing.Shared.Messaging;

public static class RabbitMqConnectionFactory
{
    public static IConnection Create(QueueOptions options)
    {
        var factory = new ConnectionFactory
        {
            HostName = options.HostName,
            Port = options.Port,
            UserName = options.UserName,
            Password = options.Password,
            DispatchConsumersAsync = true,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            ClientProvidedName = "OrderProcessing"
        };

        return factory.CreateConnection();
    }
}
