# Architecture Decision Records (ADR)

Decisiones de arquitectura de `Common`. Cada una en su archivo, numerada, con el contexto que la
forzo, las opciones que se descartaron y lo que cuesta.

| ADR | Decision | Estado |
|---|---|---|
| [0001](0001-dividir-en-sub-librerias-con-facade.md) | Dividir `Common` en 5 sub-librerias + una facade | Aceptado |
| [0002](0002-multitenancy-como-modulo-base.md) | `Common.MultiTenancy` como modulo base, por debajo de Web e Infra | Aceptado |
| [0003](0003-mediator-propio-en-lugar-de-mediatr.md) | Mediator propio en lugar de MediatR | Aceptado |
| [0004](0004-logging-con-serilog-y-seq.md) | Logging estructurado con Serilog, consola y Seq | Aceptado |
| [0005](0005-metricas-y-trazas-solo-por-otlp.md) | Metricas y trazas salen solo por OTLP | Aceptado |
| [0006](0006-enmascarar-datos-sensibles-por-lista-negra.md) | Enmascarar datos sensibles en el pipeline por lista negra de nombres | Aceptado |
| [0007](0007-solo-dependencias-estables.md) | Solo dependencias en version estable, con puerta que truena | Aceptado |
| [0008](0008-consumo-como-submodulo-fijado-a-commit.md) | Los consumidores reciben `Common` como submodulo fijado a commit | Aceptado |

Las ocho se registraron **a posteriori** el 2026-09-24: la decision ya estaba tomada e implementada,
y cada ADR cita el commit donde ocurrio. Los requisitos que estas decisiones sostienen estan en
[`../srs.md`](../srs.md).

## Como agregar uno

1. Copia [`0000-plantilla.md`](0000-plantilla.md) como `NNNN-titulo-en-kebab.md` con el siguiente
   numero libre.
2. Llena Contexto, Opciones, Decision y Consecuencias. Maximo ~30 lineas de contenido: si no cabe,
   son dos decisiones.
3. Entra en `Propuesto`. Pasa a `Aceptado` cuando se implementa, en el mismo commit que el codigo.
4. Agrega la fila a la tabla de arriba.

**Un ADR aceptado no se edita.** Si la decision cambia, se escribe uno nuevo que lo reemplaza y el
viejo pasa a `Reemplazado por ADR-NNNN`. Corregir una errata o un enlace roto si se vale.

## Cuando hace falta uno

Cuando la decision cambia lo que los consumidores reciben o es cara de revertir: una dependencia
central nueva o que se va, un cambio de frontera entre ensamblados, un contrato publico que se rompe,
una politica que afecta a todos (logging, seguridad, versionado). Un metodo nuevo o un arreglo
interno no llevan ADR; llevan buen mensaje de commit.

> `Raptor-Dev-Services/Common` tiene un espejo que se publica en nuget.org (`Raptor057/ApiCommon`),
> con esta misma carpeta copiada tal cual. Las decisiones que son solo del espejo viven alla, en
> `docs/adr-nuget/`, con su propia numeracion.
