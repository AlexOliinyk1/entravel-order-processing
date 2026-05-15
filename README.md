# Order Processing Service

Small .NET 8 microservice that accepts orders via HTTP, processes them asynchronously through RabbitMQ, and persists them in PostgreSQL.

Two core entities: **Order** and **Inventory**. The HTTP endpoint returns immediately; the worker validates inventory, snapshots prices, applies a discount rule, decrements stock, and marks the order `Processed` or `Failed`.

## Stack

- .NET 8 (ASP.NET Core Minimal API + `BackgroundService` Worker)
- PostgreSQL 16 + EF Core 8 (`Npgsql.EntityFrameworkCore.PostgreSQL`)
- RabbitMQ 3.13 (raw `RabbitMQ.Client` 6.x with manual ack + DLQ)
- Serilog (structured JSON console logs in both hosts)
- prometheus-net (single counter `orders_received_total` exposed on the API)
- xUnit + EF InMemory + FluentAssertions (4 focused unit tests)
- Docker Compose

## Quick start

Requires Docker Desktop (or any Compose-capable engine).

```bash
docker compose up --build -d
```

Wait ~15 seconds for `postgres` and `rabbitmq` healthchecks to pass. The API and Worker start only after both are healthy.

### Submit an order

```bash
curl -X POST http://localhost:8080/api/orders \
  -H "Content-Type: application/json" \
  -d '{
    "customerId": "guest-123",
    "totalAmount": 540,
    "items": [{ "sku": "SKU-HTL-NYC-EXEC", "quantity": 1 }]
  }'
```

Response (immediately, without waiting for worker):

```
HTTP/1.1 202 Accepted
Location: /api/orders/<orderId>

{ "orderId": "<guid>", "status": "Pending" }
```

### Read it back (after ~1 second)

```bash
curl http://localhost:8080/api/orders/<orderId>
```

```json
{
  "id": "<guid>",
  "customerId": "guest-123",
  "requestedTotalAmount": 540.00,
  "finalTotalAmount": 486.00,
  "discountAmount": 54.00,
  "status": "Processed",
  "createdAt": "...",
  "processedAt": "...",
  "failureReason": null,
  "items": [
    { "sku": "SKU-HTL-NYC-EXEC", "quantity": 1, "unitPrice": 540.00 }
  ]
}
```

The 10% discount triggers because the inventory price (540) is above the 500 threshold.

### Other endpoints

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/api/orders` | Submit a new order. Returns `202` immediately. |
| `GET` | `/api/orders/{id}` | Read an order with current status and items. |
| `GET` | `/api/inventory` | List seeded inventory (5 hotel SKUs). |
| `GET` | `/health` | Liveness + DB reachability via EF Core `DbContextCheck`. |
| `GET` | `/metrics` | Prometheus scrape endpoint (`orders_received_total` + ASP.NET Core HTTP defaults). |
| `GET` | `/swagger` | OpenAPI UI (Development environment only). |

RabbitMQ management UI: <http://localhost:15672> (guest / guest).

### Local build and tests

```bash
dotnet build
dotnet test
```

> Tests target `net8.0`. If you only have a newer .NET SDK installed, set `DOTNET_ROLL_FORWARD=LatestMajor` before `dotnet test`. Inside Docker this is irrelevant — the runtime image is `dotnet/runtime:8.0`.

> If host port `5432`, `5672`, `15672`, or `8080` is already taken, change the left-hand port in `docker-compose.yml`.

## Architecture

```
        ┌──────────┐  POST /api/orders   ┌──────────────────────┐
client ─┤  Api     ├────────────────────►│ orders.exchange      │
        │ :8080    │  202 + orderId      │  → orders.created Q  │
        └──┬───────┘                     └──────────┬───────────┘
           │  EF Core save (Pending)                │  consume (manual ack, prefetch=8)
           ▼                                        ▼
        ┌──────────┐                          ┌──────────────────┐
        │ Postgres │◄─────────────────────────┤ Worker           │
        │ :5432    │  read inventory, update  │  OrderProcessor  │
        └──────────┘  order, decrement stock  └──────────────────┘
                                                    │
                                                    │ on unexpected error → nack → DLX
                                                    ▼
                                              orders.dlx → orders.dlq
```

Message flow:

1. API validates the request and persists the order with `Status=Pending`.
2. API publishes `OrderCreatedMessage { OrderId }` to `orders.exchange` and returns `202` to the caller.
3. Worker consumer (one channel, manual ack, `prefetch=8`) deserializes the message and resolves a scoped `IOrderProcessor`.
4. `OrderProcessor` loads the order with items, runs an idempotency guard (`Status != Pending` → processor no-ops, consumer acks), opens an EF Core transaction, validates inventory, snapshots `OrderItem.UnitPrice` from `InventoryItem.UnitPrice`, applies the discount rule, decrements stock, sets `Status=Processed`, and commits. The consumer then acks.
5. Business failure (unknown SKU or insufficient stock) → `Status=Failed` + `FailureReason` + ack (terminal state, not a poison message).
6. Unexpected exception (DB unavailable, deserialization error, etc.) → rollback + nack(requeue=false) → message lands in `orders.dlq` via `orders.dlx`.

## Design decisions

- **RabbitMQ over Redis.** RabbitMQ provides durable queues, manual acknowledgments, and a clean dead-letter mechanism out of the box, which matches the reliability needs of order processing better than Redis Pub/Sub. Redis Streams could work too, but for "process every order at least once and never lose one" RabbitMQ is the more natural fit. Also explicitly listed in the target stack.
- **Two hosts (Api + Worker) instead of one process with a hosted service.** The task specifically calls out asynchronous processing decoupled from the HTTP request. Splitting hosts shows the boundary clearly and lets each scale independently.
- **PostgreSQL + EF Core.** Preferred by the task. EF lets a single transaction cover both the order status update and the inventory decrement, which is the integrity guarantee that matters here.
- **Raw `RabbitMQ.Client`** instead of MassTransit. Fewer dependencies, more visible mechanics for review.
- **Single Prometheus counter on the API**, structured logs in the Worker. The task asks for *at least one metric or log* showing processed orders. The Worker logs every order with `totalProcessed` / `totalFailed` counters; the API exposes `orders_received_total` on `/metrics` so the surface still matches the broader observability stack.
- **`EnsureCreated()` + seeded inventory at startup**, not EF migrations. Keeps the take-home runnable with a single `docker compose up`. In production the schema would be managed by `dotnet ef migrations`.
- **`OrderItem.UnitPrice` is a snapshot taken by the Worker** from `InventoryItem.UnitPrice` at processing time. The API stores `0m` placeholder; the Worker overwrites with the authoritative price. This avoids trusting the client for pricing.

## Observability

- **Logs.** Serilog JSON to stdout in both Api and Worker. Worker writes one line per order: `Order {OrderId} processed in {ElapsedMs}ms (totalProcessed=N, totalFailed=F)`. Inspect via `docker compose logs -f worker`.
- **Metric.** API exposes `orders_received_total` (Prometheus counter) plus the standard ASP.NET Core HTTP request metrics on `GET /metrics`.
- **Health.** `GET /health` runs an EF Core `DbContextCheck` against PostgreSQL.
- **RabbitMQ UI.** <http://localhost:15672> (guest / guest) — inspect `orders.exchange`, `orders.created`, `orders.dlx`, `orders.dlq`.

## Trade-offs (deliberately skipped)

- **No transactional outbox.** The publisher is called immediately after `SaveChangesAsync`. If the process crashes between save and publish, the order stays `Pending` forever. Production fix: outbox table + relay worker, or publisher confirms with an outbox-style retry.
- **`EnsureCreated()` instead of EF migrations.** Acceptable for a minimal example; production would use generated migrations under version control.
- **No auth, no rate limiting, no CORS configuration.** Out of scope for the take-home.
- **No MassTransit / MediatR / FluentValidation / AutoMapper.** Manual mapping and inline validation keep the dependency surface small.
- **No retry queue with backoff.** Single attempt per delivery: business failures terminate as `Failed`, technical failures go straight to the DLQ. A real system would add a delayed-retry queue (e.g. `orders.retry` with TTL) before the DLQ.
- **Worker counters are in-memory.** They reset on restart. A real deployment would scrape them via a metrics exporter or push them to a gateway.
- **No integration tests.** Only the business logic of `OrderProcessor` is unit-tested through the EF InMemory provider.

## Assumptions

- Inventory is seeded at startup with five hotel SKUs (one of them is `SKU-HTL-NYC-EXEC` at 540 — used in the curl example above).
- The client-supplied `totalAmount` is recorded as `RequestedTotalAmount` for audit only. `FinalTotalAmount` is recomputed server-side from inventory prices. A mismatch is *not* treated as a failure for this exercise.
- Discount rule is illustrative: 10% of the full subtotal when the subtotal is greater than 500.
- Single-node deployment. No leader election, no sharding, no cross-node coordination.
- The Worker channel is single-threaded: one channel = one consumer = sequential processing. `IModel` in `RabbitMQ.Client` 6.x is not thread-safe.

## Project layout

```
src/
  OrderProcessing.Shared/
    Domain/        Order, OrderItem, InventoryItem, OrderStatus
    Contracts/     Submit/Order/Inventory DTOs
    Messaging/     QueueOptions, OrderCreatedMessage, RabbitMqTopology,
                   RabbitMqConnectionFactory, IOrderQueuePublisher,
                   RabbitMqOrderQueuePublisher
    Data/          OrderDbContext, DatabaseInitializer (seeds 5 SKUs)
  OrderProcessing.Api/
    Endpoints/     OrdersEndpoints (POST/GET orders, GET inventory)
    Program.cs     Serilog, EF, RabbitMQ publisher, Prometheus, /health
    Dockerfile
  OrderProcessing.Worker/
    Processing/    IOrderProcessor, OrderProcessor (business logic)
    Hosting/       OrderConsumerHostedService (RabbitMQ consumer)
    Program.cs     Serilog, EF, hosted service wiring
    Dockerfile

tests/
  OrderProcessing.Tests/
    OrderProcessorTests.cs    happy-path, discount, insufficient stock,
                              idempotency
```
