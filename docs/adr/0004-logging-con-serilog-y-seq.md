# ADR-0004 - Logging estructurado con Serilog, consola y Seq

**Fecha:** 2026-02-14 (registrado a posteriori el 2026-09-24)
**Estado:** Aceptado
**Proyecto:** Common
**Origen:** `AddLoggingServices` desde `e7135d4`; alineado con el `Common` de GTM-Suite V3 en `1c7cccf`.

---

## Contexto

Todos los consumidores necesitan logs que se puedan filtrar por aplicacion, version, entorno, tenant
y correlacion, y buscar sin montar una pila de observabilidad completa en desarrollo. Tambien tienen
que salir por consola, que es lo que captura el runtime del contenedor.

---

## Opciones consideradas

### Opcion A - Solo `Microsoft.Extensions.Logging` con el proveedor de consola

**Pros:** cero dependencias.
**Contras:** sin enriquecedores ni un destino buscable; el formato estructurado queda a medias.

### Opcion B - Serilog con consola, debug y Seq opcional

**Pros:** enriquecimiento declarativo (`Project`, `Application`, `Version`, `Environment`,
`MachineName`, `TenantId`); Seq es un solo contenedor y se busca por propiedad.
**Contras:** una dependencia central mas, y Seq es un servicio a levantar.

### Opcion C - Logs tambien por OTLP

**Contras:** duplicaria el destino que ya cubre Seq; se deja para cuando haga falta un backend unico.

---

## Decision

**Optamos por la Opcion B.** `AddLoggingServices` reemplaza a los proveedores por Serilog, escribe a
consola y debug siempre, y a Seq solo si `CustomLogging:SeqUri` esta configurado. El nivel sale de
`CustomLogging:LogEventLevel` y en `Development` baja al menos a `Debug`.

---

## Consecuencias

**Positivas:**
- Un producto sin Seq sigue teniendo logs utiles en consola.

**Negativas / trade-offs a vigilar:**
- Sin configuracion el nivel cae a `Verbose`: un despliegue sin `LogEventLevel` registra todo. Esto
  agrava cualquier fuga de datos en los logs (ver [ADR-0006](0006-enmascarar-datos-sensibles-por-lista-negra.md)).
- El entorno se lee de `ASPNETCORE_ENVIRONMENT`, no de `IHostEnvironment`: un host no-web no lo ve.
