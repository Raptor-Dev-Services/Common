# ADR-0009 - Motores de calculo de negocio genericos en un ensamblado nuevo, `Common.Workflow`

**Fecha:** 2026-10-11
**Estado:** Propuesto
**Proyecto:** Common
**Origen:** plan de adopcion de GLPI, repo `Raptor-Dev-Services/glpi-ideas` (`00-PLAN-MAESTRO.md` §2 y
§10.1; SRS `SRS-CMN-C01` a `C07` en `02-comun/`). Aprobado por el dueno el 2026-10-11. **Amplia** el
alcance de [`../srs.md`](../srs.md) §1.2.

---

## Contexto

Cinco productos (SocioFit, SocioRent, SocioPOS, Zeit, hospital-core) van a adoptar mesa de ayuda, SLA,
activos y contratos tomando la logica de GLPI. Siete piezas son **mecanismo sin negocio** y dificiles de
acertar: calendario habil con feriados y cambio de horario, maquina de estados con matriz por rol, motor de
reglas, alertas deduplicadas, folios, condiciones de formulario y depreciacion. Escritas cinco veces
divergen (Zeit ya resolvio el cambio de horario en `BranchClock`; nadie mas lo tiene). Pero
`srs.md` §1.2 dice que Common **no incluye reglas de negocio de ningun producto**, y Common hoy no tiene
tablas ni las quiere.

---

## Opciones consideradas

### Opcion A - Cada producto copia el calculo

**Pros:** cero cambio en Common; cada producto ajusta a su gusto.
**Contras:** cinco calendarios habiles con cinco bugs distintos; los vectores de prueba no se comparten.

### Opcion B - Agregar los motores a `Common.Infra` o a la facade

**Contras:** Infra arrastra Serilog, OTel y Npgsql; un calculo puro no los necesita y el dominio de los
productos no puede referenciar Infra. La facade los empujaria a consumidores que no los piden.

### Opcion C - Ensamblado nuevo `Common.Workflow`, calculo puro + contratos de puerto, sin tablas

**Pros:** referenciable desde la capa Domain de cada producto; se prueba con listas; opt-in.
**Contras:** un ensamblado mas que versionar; hay que trazar bien la frontera "mecanismo vs negocio".

---

## Decision

**Optamos por la Opcion C.** Se crea `Common.Workflow` (net10.0, sin dependencias salvo
`Common.Contracts`), con un namespace por componente: `Calendars` (C01), `States` (C02), `Rules` (C03),
`Alerts` (C04), `Folios` (C05), `Forms` (C06), `Depreciation` (C07).

- **Frontera:** entra el *mecanismo* (como se calcula un vencimiento, como se evalua una regla); se queda en
  el producto el *contenido* (que estados tiene un ticket, que acciones tiene una regla, que tasa tiene un
  activo) y toda persistencia.
- **No incluye:** tablas, migraciones, SQL, `DbContext`, jobs, notificaciones, reloj (`DateTime.UtcNow`
  prohibido por prueba de arquitectura), DI obligatoria. Los puertos (`IAlertLedger`, `IFolioGenerator`)
  los implementa el producto; Common trae implementaciones en memoria para pruebas.
- **No entra a la facade** `Common.csproj`: se referencia explicito.
- **Versionado:** **2.2.0** (menor: solo agrega). Si C06/C07 retrasan la etapa 0, se publica 2.2.0 con
  C01, C02, C03 y C05 (los que pide la etapa 1) y el resto como 2.3.0.
- **Consumo:** submodulo fijado al tag `v2.2.0` ([ADR-0008](0008-consumo-como-submodulo-fijado-a-commit.md))
  con `ProjectReference` a `Common/Common.Workflow/Common.Workflow.csproj` desde Domain o Application; en el
  espejo NuGet, paquete `Raptor.Common.Workflow`.

---

## Consecuencias

**Positivas:**
- Un solo calendario habil, una sola maquina de estados y un solo motor de reglas con 100 % de cobertura y
  vectores compartidos.
- Los productos siguen duenos de sus datos, su tenancy y sus migraciones.

**Negativas / trade-offs a vigilar:**
- `srs.md` §1.2 cambia: "no incluye reglas de negocio de ningun producto; si incluye mecanismos genericos de
  calculo (`Common.Workflow`)". Se agregan requisitos `REQ-WF-*` y la fila en §2.6.
- La tabla de versiones de la regla `common-library` del catalogo pasa a `v2.2.0` para modulares.
- SocioRent y SocioPOS **no** consumen Common hoy (usan MediatR directo, inventario 2026-10-11): por eso
  Workflow depende solo de `Common.Contracts`, y para ellos sera su primer contacto con la libreria.
- Un cambio que rompa un motor obliga a mayor (3.0.0) aunque solo lo use un producto: se mitiga con
  definiciones serializables estables y codigos de error fijos.
