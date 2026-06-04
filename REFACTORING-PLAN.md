# Plan de refactorizacion — Common en sub-librerias ✅ EJECUTADO (v2.0.0)

> **Estado:** Ejecutado el 2026-06-04.
> Monolito estable previo: tag `v1.1.0` (commit `b81f5e6`). Resultado modular: tag `v2.0.0`.

---

## Contexto

`Common` era un **unico ensamblado** con ~10 namespaces de responsabilidades muy distintas. Cualquier
consumidor arrastraba Npgsql + Dapper + OpenTelemetry + Serilog + ASP.NET aunque usara solo una fraccion.
Un cambio en `Mediator.cs` invalidaba la compilacion de capas `Domain` que nunca lo tocan.

Para adaptarla a un **monolito modular** se partio por modulos, espejando la libreria hermana
`GTM.Common`, pero con una sub-libreria extra (`Common.MultiTenancy`) que este repo si necesita.

## Diseno final — 5 sub-librerias + facade

| Proyecto (assembly) | Namespaces | Depende de |
|---|---|---|
| `Common.Contracts` | `Common.Results`, `Common.Errors`, `Common.Exceptions`, `Common.Messaging` (solo `INotification`/`IResponse`) | — |
| `Common.Messaging` | `Common.Messaging` (mediator), `Common.Abstractions` | Contracts |
| `Common.MultiTenancy` | `Common.MultiTenancy` | ASP.NET + Serilog |
| `Common.Infra` | `Common.Logging`, `Common.Observability`, `Common.HealthChecks`, `Common.Http`, `Common.Options`, `Common.Messaging` (impl `Mediator`/pipeline/`AddMediator`), `Common.Data`, `Common.PostgreSql` | Contracts + Messaging + MultiTenancy + NuGets |
| `Common.Web` | `Common.ViewModels`, `Common.Web` | Contracts + MultiTenancy + ASP.NET |
| `Common` (facade) | re-exporta los 5 | los 5 |

### Por que `MultiTenancy` es su propio modulo (a diferencia de GTM.Common)

`Common.MultiTenancy` lo consumen **tanto** `Common.Web` (`TenantResolutionMiddleware`) **como**
`Common.Infra` (`Http`, `Logging`, `Data`, `PostgreSql`). Si viviera dentro de `Common.Infra`,
`Common.Web` arrastraria Npgsql/Dapper. Por eso es un **modulo base** por debajo de Web e Infra
(solo depende de ASP.NET + Serilog; no referencia ningun `Common.*`).

## Dependencias por capa (consumidor del monolito modular)

| Capa | References |
|---|---|
| Domain | `Common.Contracts` |
| Application | `Common.Contracts` + `Common.Messaging` (+ `Common.MultiTenancy` si lee tenant) |
| Infrastructure | `Common.Contracts` + `Common.Messaging` + `Common.MultiTenancy` + `Common.Infra` |
| Presentation | `Common.Contracts` + `Common.Messaging` + `Common.Web` |
| Host | `Common.Infra` + `Common.Web` |

## Pasos ejecutados

1. Tag `v1.1.0` sobre el monolito estable (`b81f5e6`).
2. Creados `Common.Contracts`, `Common.Messaging`, `Common.MultiTenancy`, `Common.Infra`, `Common.Web`.
3. `git mv` de los 40 archivos `.cs` (historial preservado como renames). `INotification`/`IResponse`
   extraidos a `Common.Contracts/Messaging/INotification.cs` (porque `Result : IResponse`); el resto del
   mediator quedo en `Common.Messaging/Messaging/MediatorAbstractions.cs`.
4. `Common.csproj` convertido en **facade transitoria** (referencia a los 5); `Common.slnx` con 6 proyectos.
5. `dotnet build Common.slnx` -> **0 errores, 0 warnings**.
6. Docs actualizadas (`README.md`, `CLAUDE.md`, este plan).
7. Tag `v2.0.0`.

## Migracion de consumidores (a su ritmo)

- **Sin cambios:** quien referencia la facade `Common.csproj` sigue compilando igual.
- **Adoptar layering:** reemplazar la referencia a `Common.csproj` por las sub-librerias que
  correspondan a cada capa (tabla de arriba). Migracion incremental, sin big-bang.
- **Pinning independiente:** cada repo consumidor puede quedarse en `v1.1.0` o saltar a `v2.0.0`
  cuando le convenga.

## Futuro (opcional)

- Eliminar la facade `Common.csproj` cuando todos los consumidores apunten a sub-librerias -> `v2.1.0`.
