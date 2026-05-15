using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderProcessing.Shared.Domain;

namespace OrderProcessing.Shared.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, bool seedInventory = true, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<OrderDbContext>>();

        const int maxAttempts = 10;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.EnsureCreatedAsync(cancellationToken);
                break;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(attempt * 2, 10));
                logger.LogWarning(ex,
                    "Database not ready (attempt {Attempt}/{Max}). Retrying in {Delay}s.",
                    attempt, maxAttempts, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
            }
        }

        if (!seedInventory)
        {
            return;
        }

        if (!await db.Inventory.AnyAsync(cancellationToken))
        {
            try
            {
                db.Inventory.AddRange(SeedInventory());
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Seeded inventory with starter SKUs.");
            }
            catch (DbUpdateException ex)
            {
                logger.LogInformation(ex,
                    "Inventory seed lost the race against another startup; treating as already seeded.");
            }
        }
    }

    public static IEnumerable<InventoryItem> SeedInventory() =>
    [
        new() { Sku = "SKU-HTL-PAR-DLX",  Name = "Paris Deluxe Suite — 1 night",       Quantity = 50,  UnitPrice = 320.00m  },
        new() { Sku = "SKU-HTL-LON-STD",  Name = "London Standard Room — 1 night",     Quantity = 80,  UnitPrice = 180.00m  },
        new() { Sku = "SKU-HTL-NYC-EXEC", Name = "New York Executive Suite — 1 night", Quantity = 30,  UnitPrice = 540.00m  },
        new() { Sku = "SKU-HTL-TKO-CAP",  Name = "Tokyo Capsule — 1 night",            Quantity = 200, UnitPrice = 65.00m   },
        new() { Sku = "SKU-HTL-DXB-VIP",  Name = "Dubai VIP Penthouse — 1 night",      Quantity = 10,  UnitPrice = 1250.00m }
    ];
}
