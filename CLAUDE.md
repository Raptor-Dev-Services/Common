# CLAUDE.md — Common (Raptor Dev Services)

## Contexto rapido

- Libreria base reutilizable para WebApi .NET 10: logging (Serilog + Seq), observabilidad
  (OpenTelemetry), resultados/errores/excepciones, mediator **custom**, multi-tenancy y Data/PostgreSql.
- Framework: .NET 10 — el mediator es **custom** (`Common.Messaging`), **no** es el NuGet MediatR.
- Estructura: **monolito modular** — 5 sub-librerias + facade (desde `v2.0.0`).
- Tags: `v1.1.0` = ultimo monolito de un solo ensamblado; `v2.0.0` = split modular.

## Regla principal

Los cambios se hacen aqui (clone standalone). Tras un cambio relevante: crear tag semantico
→ los consumidores actualizan su referencia/submodule a su ritmo.

**Los namespaces NO cambian** al mover codigo entre sub-proyectos. Si reubicas un tipo, conserva su
`namespace Common.X`; solo cambia el ensamblado que lo contiene. Asi ningun consumidor toca sus `using`.

## Proyectos (assembly) -> namespaces

| Proyecto | Namespaces | Depende de |
|---|---|---|
| `Common.Contracts` | `Common.Results`, `Common.Errors`, `Common.Exceptions`, `Common.Messaging` (`INotification`/`IResponse`) | — |
| `Common.Messaging` | `Common.Messaging` (contratos mediator), `Common.Abstractions` | Contracts |
| `Common.MultiTenancy` | `Common.MultiTenancy` | ASP.NET + Serilog |
| `Common.Infra` | `Common.Logging`, `Common.Observability`, `Common.HealthChecks`, `Common.Http`, `Common.Options`, `Common.Messaging` (impl), `Common.Data`, `Common.PostgreSql` | Contracts + Messaging + MultiTenancy + NuGets |
| `Common.Web` | `Common.ViewModels`, `Common.Web` | Contracts + MultiTenancy + ASP.NET |
| `Common` (facade) | re-exporta los 5 | los 5 |

## Reglas de capas (donde colocar codigo nuevo)

- Tipo/contrato puro sin deps externas -> `Common.Contracts`.
- Abstraccion del mediator (interfaces/handlers) -> `Common.Messaging`.
- Implementacion + helpers `Microsoft.Extensions.*` / Dapper / Npgsql / OTel / Serilog -> `Common.Infra`.
- Envelope HTTP / middlewares ASP.NET -> `Common.Web`.
- Contexto/resolucion de tenant (lo consumen Web e Infra) -> `Common.MultiTenancy`.
- `Common.MultiTenancy` es un **modulo base**: NO debe referenciar `Common.Infra`/`Common.Web` (ciclo).

## Build / verificacion

```bash
dotnet build Common.slnx -c Debug   # debe dar 0 errores, 0 warnings
```

## Refactorizacion ejecutada

Ver `REFACTORING-PLAN.md` (monolito -> 5 sub-librerias, ejecutado en `v2.0.0`).
