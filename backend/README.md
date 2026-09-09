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
