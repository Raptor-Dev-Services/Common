# Estado de Common - para continuar en otra maquina

**Cerrado el:** 2026-09-24.

Este documento es el **estado actual y se sobrescribe** en cada cierre. La historia vive en los
mensajes de commit y en [`CHANGELOG.md`](CHANGELOG.md); el uso de la libreria, en
[`docs/guia/`](docs/guia/README.md).

> **Vuelve a medir, no copies este cierre.** Las cifras de abajo se midieron al cerrar.

## Donde quedo

- Rama `main` en `dace97a`, empujada, arbol limpio, sin otras ramas. Ultimo tag: **`v2.1.3`**.
- El espejo publico [`Raptor057/ApiCommon`](https://github.com/Raptor057/ApiCommon) esta sincronizado
  con `v2.1.3`, y los seis paquetes `Raptor.Common.*` **2.1.3** estan publicados e indexados en nuget.org.
- **Consumidores por submodulo ya en `v2.1.3`:** `back-template`, `cmsa`, `hospital-core` y
  `zeit-webapi`, cada uno con sus puertas medidas al subir.

## Puertas al cerrar

`dotnet build Common.slnx -c Release` **0 warnings, 0 errores** (con CS1591 activo),
`dotnet test` **44 de 44**, `python scripts/check-prerelease-deps.py` **verde**.

Este repo **no tiene CI**: esas tres puertas se corren en local antes de cada tag. El CI del espejo
corre las mismas en cada push y antes de publicar.

## Lo que sigue

1. **Subir `ArccNova.WebApi` a `v2.1.3`.** Monta Common en `Shared/Common` y no estaba en la maquina
   donde se hizo este cierre, asi que no se sabe en que version esta. Los cambios que pueden romper al
   subir estan en el `CHANGELOG` (seccion de migracion de 2.1.0 y 2.1.3).
2. **Pruebas que faltan.** El SRS no tiene ningun requisito sin cumplir, pero varios solo se verifican
   a traves de los consumidores: el orden de resolucion del tenant, la propagacion por `HttpClient`, el
   registro por escaneo del mediator y la ejecucion real de las migraciones. La lista exacta esta en la
   seccion 4 de [`docs/srs.md`](docs/srs.md).
3. **Deuda anotada, sin urgencia** (ADR-0003 y SRS REQ-PERF-001): `Mediator.Send` resuelve el handler
   por reflexion en cada llamada, sin cache.

Nada quedo a medias y nada esta bloqueado.

## Lo que NO se verifico

- Los ejemplos de datos y migraciones de `docs/guia/08` se **compilaron pero no se ejecutaron** contra
  una base: en la maquina del cierre un hook impide al agente escribir SQL.
- La guia `01` (submodulo) y la `02` (NuGet) se ejercitaron con dos APIs de prueba desechables que no
  estan en ningun repo.

## Lo que NO viaja con el `pull`

- **Publicar en nuget.org no pasa por este repo.** Se publica desde el espejo `ApiCommon` con Trusted
  Publishing: la politica vive en la cuenta de nuget.org y el espejo necesita el secreto `NUGET_USER`.
  El procedimiento completo, incluida la sincronizacion del espejo, esta en [`CONTRIBUTING.md`](CONTRIBUTING.md).
- **Los consumidores no se actualizan solos.** Cada uno mueve su puntero de submodulo cuando quiere; la
  tabla de "que version fija cada arquitectura" vive en la regla `common-library` del catalogo de
  Raptor Dev Services, no aqui.
- **Nada que configurar para desarrollar:** .NET SDK 10 y Python 3 para la puerta de dependencias. Sin
  `.env`, sin servicios.
- **En Windows, `python3` es el alias de la Microsoft Store** (imprime un aviso y devuelve 0): corre la
  puerta con `python`.
