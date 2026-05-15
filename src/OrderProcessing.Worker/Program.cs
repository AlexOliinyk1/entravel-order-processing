using Microsoft.EntityFrameworkCore;
using OrderProcessing.Shared.Data;
using OrderProcessing.Shared.Messaging;
using OrderProcessing.Worker.Hosting;
using OrderProcessing.Worker.Processing;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(builder.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter());
});

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");

builder.Services.AddDbContext<OrderDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.Configure<QueueOptions>(builder.Configuration.GetSection(QueueOptions.SectionName));

builder.Services.AddScoped<IOrderProcessor, OrderProcessor>();
builder.Services.AddHostedService<OrderConsumerHostedService>();

var host = builder.Build();

await DatabaseInitializer.InitializeAsync(host.Services, seedInventory: false);

await host.RunAsync();
