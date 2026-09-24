# ADR-0001 - Dividir Common en 5 sub-librerias + una facade

**Fecha:** 2026-06-04 (registrado a posteriori el 2026-09-24)
**Estado:** Aceptado
**Proyecto:** Common
**Origen:** commit `c5b419d`, tag `v2.0.0`. Detalle de la ejecucion en `REFACTORING-PLAN.md`.

---

## Contexto

`Common` era un solo ensamblado con ~10 namespaces de responsabilidades muy distintas. Una capa
`Domain` que solo queria `Result` arrastraba Npgsql, Dapper, OpenTelemetry, Serilog y ASP.NET, y un
cambio en `Mediator.cs` recompilaba capas que nunca lo tocan. Los consumidores empezaban a ser
monolitos modulares, donde cada capa debe referenciar solo lo que usa.

---

## Opciones consideradas

### Opcion A - Seguir con un ensamblado unico

**Pros:** cero migracion; una sola referencia.
**Contras:** el dominio depende de la infraestructura; no hay forma de hacer cumplir las capas.

### Opcion B - Partir en sub-librerias sin facade

**Pros:** frontera limpia desde el dia uno.
**Contras:** obliga a todos los consumidores a migrar a la vez (big-bang).

### Opcion C - Partir en sub-librerias y dejar `Common` como facade

**Pros:** frontera limpia para quien la quiera; quien referencia `Common.csproj` sigue compilando.
**Contras:** un proyecto mas que mantener mientras la facade viva.

---

## Decision

**Optamos por la Opcion C.** Cinco ensamblados: `Common.Contracts`, `Common.Messaging`,
`Common.MultiTenancy`, `Common.Infra` y `Common.Web`, mas `Common` como facade que los referencia a
todos. **Los namespaces no cambian** (`Common.Results`, `Common.Messaging`...): solo cambia en que
ensamblado viven, asi que ningun `using` de un consumidor se toca.

---

## Consecuencias

**Positivas:**
- `Domain` referencia solo `Common.Contracts`, que no tiene dependencias externas.
- Cada consumidor migra a las sub-librerias a su ritmo.

**Negativas / trade-offs a vigilar:**
- Un mismo namespace (`Common.Messaging`) repartido entre tres ensamblados confunde al buscar un tipo.
- La facade es transitoria: se retira cuando ningun consumidor la use, y eso es un cambio mayor.
