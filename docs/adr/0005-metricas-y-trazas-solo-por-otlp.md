# ADR-0005 - Metricas y trazas salen solo por OTLP

**Fecha:** 2026-09-17 (registrado a posteriori el 2026-09-24)
**Estado:** Aceptado
**Proyecto:** Common
**Origen:** commit `69afac2`; README corregido en `69a5dbe`.

---

## Contexto

`AddObservability` publicaba las metricas en un endpoint `/metrics` para que Prometheus las raspara,
con `OpenTelemetry.Exporter.Prometheus.AspNetCore`. Ese paquete **nunca ha publicado una version
estable**: todas son `-beta`, incluida la mas nueva, asi que no habia a que subir. Chocaba de frente
con [ADR-0007](0007-solo-dependencias-estables.md). Las trazas ya salian por OTLP.

---

## Opciones consideradas

### Opcion A - Mantener el exportador de Prometheus como excepcion declarada

**Pros:** ningun consumidor cambia.
**Contras:** una excepcion sin fecha de salida real: el paquete lleva anios en beta.

### Opcion B - Metricas por OTLP, el mismo exportador estable que ya usan las trazas

**Pros:** cero dependencias en prerelease; un solo mecanismo de exportacion.
**Contras:** quien raspaba `/metrics` tiene que mover Prometheus a su receptor OTLP.

---

## Decision

**Optamos por la Opcion B.** Trazas y metricas se exportan por OTLP a `Observability:OtlpEndpoint`.
Se agrega `Observability:MetricsOtlpEndpoint`, opcional: si esta, las metricas van ahi **por HTTP**
(el receptor OTLP de Prometheus no habla gRPC); si no, van al endpoint general. Sin configuracion no
se exporta nada, y la aplicacion arranca igual.

---

## Consecuencias

**Positivas:**
- La excepcion de dependencias se borro (`934061e`): `Common` no tiene ningun prerelease.

**Negativas / trade-offs a vigilar:**
- Cambio que rompe para quien usaba `MapPrometheusScrapingEndpoint`: tiene que quitarlo y arrancar
  Prometheus con `--web.enable-otlp-receiver`.
- Prometheus pasa de pull a push: si la API no puede alcanzarlo, las metricas se pierden en silencio.
