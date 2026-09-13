# entravel-order-processing — ARCHIVED

**Status:** portfolio / take-home test artifact (2026-05-15).
**No active development.** Kept as reference for architectural patterns.

## What this is
.NET 8 microservice showcasing:
- HTTP API + RabbitMQ async worker
- PostgreSQL + EF Core
- Serilog structured logging
- Prometheus metrics
- Docker Compose deploy

## Do NOT
- Add new features here
- Attempt to run in production (was a demo)
- Reference from active projects (see resume-update-2026 for career artifacts)

## If reactivated
Convert to `active` status and add:
- Real transactional outbox (currently naive publisher)
- Retry queue with DLQ
- Integration tests beyond the 4 xUnit stubs
- EF migrations pipeline
