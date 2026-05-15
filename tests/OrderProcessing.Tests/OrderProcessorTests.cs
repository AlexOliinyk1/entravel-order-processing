using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OrderProcessing.Shared.Data;
using OrderProcessing.Shared.Domain;
using OrderProcessing.Worker.Processing;

namespace OrderProcessing.Tests;

public class OrderProcessorTests
{
    private static OrderDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new OrderDbContext(options);
    }

    private static async Task<Order> SeedAsync(OrderDbContext db, decimal requestedTotal, params (string Sku, string Name, int Stock, decimal Price, int OrderQty)[] items)
    {
        foreach (var (sku, name, stock, price, _) in items)
        {
            db.Inventory.Add(new InventoryItem { Sku = sku, Name = name, Quantity = stock, UnitPrice = price });
        }

        var order = new Order
        {
            CustomerId = "guest-test",
            RequestedTotalAmount = requestedTotal,
            Status = OrderStatus.Pending,
            Items = items
                .Select(i => new OrderItem { Sku = i.Sku, Quantity = i.OrderQty, UnitPrice = 0m })
                .ToList()
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    [Fact]
    public async Task HappyPath_NoDiscount_MarksProcessedAndDecrementsStock()
    {
        await using var db = CreateDb(nameof(HappyPath_NoDiscount_MarksProcessedAndDecrementsStock));
        var order = await SeedAsync(db, requestedTotal: 200m,
            ("SKU-A", "Item A", Stock: 50, Price: 200m, OrderQty: 1));
        var processor = new OrderProcessor(db, NullLogger<OrderProcessor>.Instance);

        await processor.ProcessAsync(order.Id, CancellationToken.None);

        var stored = await db.Orders.Include(o => o.Items).FirstAsync(o => o.Id == order.Id);
        stored.Status.Should().Be(OrderStatus.Processed);
        stored.FinalTotalAmount.Should().Be(200m);
        stored.DiscountAmount.Should().Be(0m);
        stored.ProcessedAt.Should().NotBeNull();
        stored.Items.Single().UnitPrice.Should().Be(200m);

        var stock = await db.Inventory.FirstAsync(i => i.Sku == "SKU-A");
        stock.Quantity.Should().Be(49);
    }

    [Fact]
    public async Task HappyPath_WithDiscount_AppliesTenPercentWhenSubtotalAbove500()
    {
        await using var db = CreateDb(nameof(HappyPath_WithDiscount_AppliesTenPercentWhenSubtotalAbove500));
        var order = await SeedAsync(db, requestedTotal: 540m,
            ("SKU-NYC", "NYC Suite", Stock: 30, Price: 540m, OrderQty: 1));
        var processor = new OrderProcessor(db, NullLogger<OrderProcessor>.Instance);

        await processor.ProcessAsync(order.Id, CancellationToken.None);

        var stored = await db.Orders.FirstAsync(o => o.Id == order.Id);
        stored.Status.Should().Be(OrderStatus.Processed);
        stored.DiscountAmount.Should().Be(54m);
        stored.FinalTotalAmount.Should().Be(486m);
    }

    [Fact]
    public async Task InsufficientStock_MarksFailedAndKeepsInventory()
    {
        await using var db = CreateDb(nameof(InsufficientStock_MarksFailedAndKeepsInventory));
        var order = await SeedAsync(db, requestedTotal: 1000m,
            ("SKU-LOW", "Low Stock", Stock: 1, Price: 100m, OrderQty: 5));
        var processor = new OrderProcessor(db, NullLogger<OrderProcessor>.Instance);

        await processor.ProcessAsync(order.Id, CancellationToken.None);

        var stored = await db.Orders.FirstAsync(o => o.Id == order.Id);
        stored.Status.Should().Be(OrderStatus.Failed);
        stored.FailureReason.Should().NotBeNullOrWhiteSpace();
        stored.FailureReason!.ToLowerInvariant().Should().Contain("stock");

        var stock = await db.Inventory.FirstAsync(i => i.Sku == "SKU-LOW");
        stock.Quantity.Should().Be(1);
    }

    [Fact]
    public async Task Idempotency_SecondProcessOnProcessedOrder_IsNoop()
    {
        await using var db = CreateDb(nameof(Idempotency_SecondProcessOnProcessedOrder_IsNoop));
        var order = await SeedAsync(db, requestedTotal: 200m,
            ("SKU-IDEM", "Idempotency Item", Stock: 50, Price: 200m, OrderQty: 1));
        var processor = new OrderProcessor(db, NullLogger<OrderProcessor>.Instance);

        await processor.ProcessAsync(order.Id, CancellationToken.None);
        var stockAfterFirst = (await db.Inventory.FirstAsync(i => i.Sku == "SKU-IDEM")).Quantity;
        var processedAtFirst = (await db.Orders.FirstAsync(o => o.Id == order.Id)).ProcessedAt;

        await processor.ProcessAsync(order.Id, CancellationToken.None);

        var stored = await db.Orders.FirstAsync(o => o.Id == order.Id);
        stored.Status.Should().Be(OrderStatus.Processed);
        stored.ProcessedAt.Should().Be(processedAtFirst);

        var stockAfterSecond = (await db.Inventory.FirstAsync(i => i.Sku == "SKU-IDEM")).Quantity;
        stockAfterSecond.Should().Be(stockAfterFirst);
        stockAfterFirst.Should().Be(49);
    }
}
