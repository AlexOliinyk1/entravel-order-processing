using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderProcessing.Shared.Contracts;
using OrderProcessing.Shared.Data;
using OrderProcessing.Shared.Domain;
using OrderProcessing.Shared.Messaging;
using Prometheus;

namespace OrderProcessing.Api.Endpoints;

public static class OrdersEndpoints
{
    private static readonly Counter OrdersReceived = Metrics.CreateCounter(
        "orders_received_total",
        "Total number of orders accepted by the API.");

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder routes)
    {
        var orders = routes.MapGroup("/api/orders").WithTags("Orders");

        orders.MapPost("", SubmitOrder).WithName("SubmitOrder");
        orders.MapGet("/{id:guid}", GetOrder).WithName("GetOrder");

        routes.MapGet("/api/inventory", GetInventory)
            .WithTags("Inventory")
            .WithName("GetInventory");

        return routes;
    }

    private static async Task<IResult> SubmitOrder(
        [FromBody] SubmitOrderRequest request,
        OrderDbContext db,
        IOrderQueuePublisher publisher,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var validationErrors = Validate(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var order = new Order
        {
            CustomerId = request.CustomerId,
            RequestedTotalAmount = request.TotalAmount,
            Status = OrderStatus.Pending,
            Items = request.Items
                .Select(i => new OrderItem
                {
                    Sku = i.Sku,
                    Quantity = i.Quantity,
                    UnitPrice = 0m
                })
                .ToList()
        };

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        publisher.Publish(new OrderCreatedMessage(order.Id));

        OrdersReceived.Inc();
        logger.LogInformation("Order {OrderId} accepted for customer {CustomerId}", order.Id, order.CustomerId);

        var response = new SubmitOrderResponse(order.Id, order.Status.ToString());
        return Results.Accepted($"/api/orders/{order.Id}", response);
    }

    private static async Task<Results<Ok<OrderResponse>, NotFound>> GetOrder(
        Guid id,
        OrderDbContext db,
        CancellationToken ct)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

        if (order is null)
        {
            return TypedResults.NotFound();
        }

        var response = new OrderResponse(
            order.Id,
            order.CustomerId,
            order.RequestedTotalAmount,
            order.FinalTotalAmount,
            order.DiscountAmount,
            order.Status,
            order.CreatedAt,
            order.ProcessedAt,
            order.FailureReason,
            order.Items
                .Select(i => new OrderItemResponse(i.Sku, i.Quantity, i.UnitPrice))
                .ToList());

        return TypedResults.Ok(response);
    }

    private static async Task<Ok<IReadOnlyCollection<InventoryItemResponse>>> GetInventory(
        OrderDbContext db,
        CancellationToken ct)
    {
        var items = await db.Inventory
            .AsNoTracking()
            .OrderBy(i => i.Sku)
            .Select(i => new InventoryItemResponse(i.Sku, i.Name, i.Quantity, i.UnitPrice))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyCollection<InventoryItemResponse>>(items);
    }

    private static Dictionary<string, string[]> Validate(SubmitOrderRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.CustomerId))
        {
            errors[nameof(request.CustomerId)] = ["customerId is required"];
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            errors[nameof(request.Items)] = ["items must contain at least one entry"];
        }
        else
        {
            var index = 0;
            foreach (var item in request.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Sku))
                {
                    errors[$"items[{index}].sku"] = ["sku is required"];
                }
                if (item.Quantity <= 0)
                {
                    errors[$"items[{index}].quantity"] = ["quantity must be greater than zero"];
                }
                index++;
            }
        }

        if (request.TotalAmount < 0)
        {
            errors[nameof(request.TotalAmount)] = ["totalAmount must be non-negative"];
        }

        return errors;
    }
}
