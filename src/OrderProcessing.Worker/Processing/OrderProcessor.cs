using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderProcessing.Shared.Data;
using OrderProcessing.Shared.Domain;

namespace OrderProcessing.Worker.Processing;

public sealed class OrderProcessor(OrderDbContext db, ILogger<OrderProcessor> logger) : IOrderProcessor
{
    private const decimal DiscountThreshold = 500m;
    private const decimal DiscountRate = 0.10m;

    private static long _processedCount;
    private static long _failedCount;

    public async Task ProcessAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null)
        {
            logger.LogWarning("Order {OrderId} not found, skipping.", orderId);
            return;
        }

        if (order.Status != OrderStatus.Pending)
        {
            logger.LogInformation(
                "Order {OrderId} already in terminal status {Status}, skipping (idempotency).",
                orderId, order.Status);
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);

        try
        {
            var skus = order.Items.Select(i => i.Sku).Distinct().ToArray();
            var inventoryBySku = await db.Inventory
                .Where(i => skus.Contains(i.Sku))
                .ToDictionaryAsync(i => i.Sku, CancellationToken.None);

            decimal expectedTotal = 0m;

            foreach (var item in order.Items)
            {
                if (!inventoryBySku.TryGetValue(item.Sku, out var inventory))
                {
                    await FailAsync(order, $"Unknown SKU: {item.Sku}", transaction, stopwatch);
                    return;
                }

                if (inventory.Quantity < item.Quantity)
                {
                    await FailAsync(
                        order,
                        $"Insufficient stock for {item.Sku}: requested {item.Quantity}, available {inventory.Quantity}",
                        transaction,
                        stopwatch);
                    return;
                }

                item.UnitPrice = inventory.UnitPrice;
                expectedTotal += inventory.UnitPrice * item.Quantity;
            }

            var discount = expectedTotal > DiscountThreshold
                ? Math.Round(expectedTotal * DiscountRate, 2, MidpointRounding.AwayFromZero)
                : 0m;

            foreach (var item in order.Items)
            {
                inventoryBySku[item.Sku].Quantity -= item.Quantity;
            }

            order.DiscountAmount = discount;
            order.FinalTotalAmount = expectedTotal - discount;
            order.Status = OrderStatus.Processed;
            order.ProcessedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);

            stopwatch.Stop();
            var processed = Interlocked.Increment(ref _processedCount);
            logger.LogInformation(
                "Order {OrderId} processed in {ElapsedMs}ms (totalProcessed={TotalProcessed}, totalFailed={TotalFailed})",
                order.Id, stopwatch.ElapsedMilliseconds, processed, Interlocked.Read(ref _failedCount));
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task FailAsync(Order order, string reason, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, Stopwatch stopwatch)
    {
        order.Status = OrderStatus.Failed;
        order.FailureReason = reason;
        order.ProcessedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);

        stopwatch.Stop();
        var failed = Interlocked.Increment(ref _failedCount);
        logger.LogWarning(
            "Order {OrderId} failed in {ElapsedMs}ms: {Reason} (totalProcessed={TotalProcessed}, totalFailed={TotalFailed})",
            order.Id, stopwatch.ElapsedMilliseconds, reason, Interlocked.Read(ref _processedCount), failed);
    }
}
