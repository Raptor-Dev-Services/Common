# ADR-0003 - Mediator propio en lugar de MediatR

**Fecha:** 2026-02-14 (registrado a posteriori el 2026-09-24)
**Estado:** Aceptado
**Proyecto:** Common
**Origen:** `Common/Messaging/Mediator.cs` existe desde el primer commit (`e7135d4`). La
generacion anterior de esta libreria (el paquete `Raptor.Common` 0.0.x) usaba MediatR 12.5.

---

## Contexto

Los consumidores estan construidos sobre Request/Handler/Presenter: un caso de uso recibe una
peticion, la procesa y publica su respuesta a un presentador. Eso necesita un despachador con
tuberia (logging, manejo de fallos) y notificaciones. La primera generacion lo resolvia con MediatR.
El motivo original del cambio no quedo escrito; aqui se registran las fuerzas que hoy lo sostienen.

---

## Opciones consideradas

### Opcion A - MediatR

**Pros:** conocido, probado, con ecosistema.
**Contras:** es una dependencia central que atraviesa todas las capas; desde la version 13 pasa a
licencia comercial (gratuita solo bajo ciertas condiciones), y quedarse en la 12 es quedarse sin parches.

### Opcion B - Mediator propio, minimo

**Pros:** ~100 lineas bajo nuestro control; `Send`, `Publish` y `IPipelineBehavior` son todo lo que se usa.
**Contras:** lo que MediatR ya resolvio (rendimiento, casos raros) hay que resolverlo aqui.

---

## Decision

**Optamos por la Opcion B.** Los contratos (`IRequest`, `IRequestHandler`, `INotificationHandler`,
`IPipelineBehavior`, `IMediator`) viven en `Common.Messaging` y la implementacion (`Mediator`,
`InteractorPipeline`, `AddMediator`) en `Common.Infra`. Los nombres imitan a MediatR a proposito:
migrar un handler es cambiar el `using`.

---

## Consecuencias

**Positivas:**
- Ninguna licencia externa condiciona el nucleo de todos los productos.

**Negativas / trade-offs a vigilar:**
- `Send` resuelve el handler por reflexion en cada llamada (`MakeGenericType` + `MethodInfo.Invoke`),
  sin cache. Si alguna vez pesa en un perfil, se cachean los delegados por tipo de peticion.
- `Publish` lanza todos los handlers en paralelo con `Task.WhenAll`: no hay orden entre presentadores.
