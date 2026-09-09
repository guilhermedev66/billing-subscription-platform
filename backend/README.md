# Billing Platform backend

.NET 10 modular-monolith backend for the billing simulator.

## Structure

- `src/Host/BillingPlatform.Api` is the composition root.
- `src/Modules/Identity` and `src/Modules/Organizations` own the M1 vertical slices.
- `src/Modules/SimulationClock` owns the cross-cutting virtual clock contract and implementation.
- Remaining module folders are placeholders for later milestones.
- `tests/BillingPlatform.ArchitectureTests` enforces dependency direction and module isolation.

Build and test from this directory:

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-build
```

## Run with Docker Compose

Set a strong signing key and start PostgreSQL plus the API:

```bash
export JWT_SIGNING_KEY="$(openssl rand -base64 48)"
docker compose up --build
```

The API listens on `http://localhost:8080`. Liveness and readiness are available
at `/health/live` and `/health/ready`; OpenAPI is available at
`/openapi/v1.json`.

The API deliberately refuses to start when `JWT_SIGNING_KEY` is missing or is
shorter than 32 UTF-8 bytes.
