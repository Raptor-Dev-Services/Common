# ADR-0002 - Common.MultiTenancy como modulo base, por debajo de Web e Infra

**Fecha:** 2026-06-04 (registrado a posteriori el 2026-09-24)
**Estado:** Aceptado
**Proyecto:** Common
**Origen:** commit `c5b419d`. El nucleo multi-tenant entro antes, en `ab9aa3a` y `f5248ee` (2026-03-08).

---

## Contexto

Al partir `Common` ([ADR-0001](0001-dividir-en-sub-librerias-con-facade.md)) habia que decidir donde
vive la multi-tenencia. La consumen dos modulos: `Common.Web` (el middleware que resuelve el tenant
del request) y `Common.Infra` (propagacion por HTTP, enriquecedor de logs, conexiones por tenant).
La libreria hermana `GTM.Common` no tiene este modulo: alla la multi-tenencia no existe.

---

## Opciones consideradas

### Opcion A - Dentro de `Common.Infra`

**Pros:** un ensamblado menos; espeja a `GTM.Common`.
**Contras:** `Common.Web` tendria que referenciar `Common.Infra` y arrastraria Npgsql y Dapper solo
para leer el tenant del request.

### Opcion B - Dentro de `Common.Web`

**Contras:** `Common.Infra` dependeria de Web, y una capa de infraestructura quedaria atada a ASP.NET
por un modulo de presentacion.

### Opcion C - Modulo propio, sin depender de ningun `Common.*`

**Pros:** Web e Infra lo referencian sin arrastrarse entre si.
**Contras:** un sexto ensamblado.

---

## Decision

**Optamos por la Opcion C.** `Common.MultiTenancy` depende solo de ASP.NET y Serilog, y no referencia
ningun otro `Common.*`. Es la base sobre la que se apoyan Web e Infra.

---

## Consecuencias

**Positivas:**
- `Common.Web` no arrastra acceso a datos.
- Una capa `Application` que solo necesita leer el tenant referencia este modulo y nada mas.

**Negativas / trade-offs a vigilar:**
- Depende de `Microsoft.AspNetCore.App` (usa `HttpContext`): un consumidor que no es web tambien
  carga el framework de ASP.NET si quiere multi-tenencia.
