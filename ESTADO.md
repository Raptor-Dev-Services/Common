# Estado de Common - para continuar en otra maquina

**Cerrado el:** 2026-10-11.

Este documento es el **estado actual y se sobrescribe** en cada cierre. La historia vive en los
mensajes de commit y en [`CHANGELOG.md`](CHANGELOG.md); el uso de la libreria, en
[`docs/guia/`](docs/guia/README.md).

> **Vuelve a medir, no copies este cierre.** Las cifras de abajo se midieron al cerrar.

## Donde quedo

- Rama `main`, empujada, arbol limpio. Ultimo tag: **`v2.1.3`**. En `main` hay trabajo **sin publicar**:
  el ensamblado nuevo **`Common.Workflow`** ([ADR-0009](docs/adr/0009-motores-de-calculo-en-common-workflow.md)),
  con los siete motores C01-C07 de la adopcion de GLPI (calendario habil, maquina de estados, motor de
  reglas, alertas, folios, condiciones de formulario y depreciacion). Esta en la seccion "sin publicar"
  del `CHANGELOG` y esta pensado como **2.2.0** (menor: solo agrega).
- Los seis paquetes `Raptor.Common.*` **2.1.3** siguen siendo lo publicado; el espejo `ApiCommon` no
  tiene aun `Common.Workflow`.
- **Consumidores por submodulo en `v2.1.3`:** `back-template`, `cmsa`, `hospital-core` y `zeit-webapi`.

## Puertas al cerrar

`dotnet build Common.slnx -c Release` **0 warnings, 0 errores** (con CS1591 activo),
`dotnet test` **44 de 44** (Common.Tests) y **721 de 721** (Common.Workflow.Tests, 10 corridas seguidas
en verde), `python scripts/check-prerelease-deps.py` **verde**.

Este repo **no tiene CI**: esas puertas se corren en local antes de cada tag. En el servidor de
staging no hay SDK de .NET instalado: se corrieron con la imagen `mcr.microsoft.com/dotnet/sdk:10.0`
(`docker run --rm -u $(id -u):$(id -g) -e HOME=/tmp -v "$PWD":/src -w /src ...`).

## Lo que sigue

1. **Publicar 2.2.0** (tag `v2.2.0`, espejo `ApiCommon` con el paquete nuevo `Raptor.Common.Workflow`,
   y la tabla de la regla `common-library` del catalogo). Antes:
   - **Que un contador revise los vectores de depreciacion con tasas LISR** (C07, V1-V6): estan con los
     numeros del SRS, no revisados.
   - Revisar los **codigos de error que los agentes agregaron** ademas de los del SRS (son contrato):
     `depreciation.invalid_method|invalid_cap|invalid_rounding`, `alerts.invalid_lead|invalid_stop`,
     `states.invalid_code|invalid_reason_length|policy_unknown_transition`, `rules.missing_id`,
     `forms.duplicate_id|unknown_section|invalid_value`.
   - El espejo necesita empaquetar el proyecto nuevo (su CI publica por proyecto).
2. **Subir `ArccNova.WebApi` a `v2.1.3`** (pendiente de antes, ver el `CHANGELOG`).
3. **Pruebas que faltan** del resto de la libreria: la lista esta en la seccion 4 de [`docs/srs.md`](docs/srs.md).
4. **Deuda anotada, sin urgencia:** `Mediator.Send` resuelve el handler por reflexion sin cache (ADR-0003).

## Lo que NO se verifico

- **Rendimiento de los motores (QOS):** hay pruebas con margen amplio (p. ej. reglas p95 < 10 ms con
  meta de 1 ms), no benchmarks. C01 midio ~8-117 us por operacion.
- **Cobertura:** no se midio (el proyecto de pruebas no tiene coverlet).
- Los ejemplos de datos y migraciones de `docs/guia/08` se compilaron pero no se ejecutaron contra
  una base.

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
