# ADR-0007 - Solo dependencias en version estable, con puerta que truena

**Fecha:** 2026-09-17 (registrado a posteriori el 2026-09-24)
**Estado:** Aceptado
**Proyecto:** Common
**Origen:** commit `fb63f49`. Regla completa en `.claude/rules/stable-dependencies.md`.

---

## Contexto

`Common` es una libreria compartida: lo que ella referencia, lo heredan todos sus consumidores. NuGet
exige ademas que quien consume un prerelease tambien pueda serlo, asi que un `-beta` aqui contamina
hacia abajo. Un prerelease puede cambiar su API sin aviso, despublicarse (y el build deja de
restaurar en CI, no en la maquina que ya lo tiene en cache) y no recibe parches de seguridad.

---

## Opciones consideradas

### Opcion A - Criterio de quien revisa

**Contras:** un aviso que no rompe nada se lee una vez y despues se ignora.

### Opcion B - Puerta automatica con excepciones declaradas y con fecha

**Pros:** no depende de la memoria de nadie; la excepcion vence y pone la puerta en rojo.
**Contras:** un script mas que mantener.

---

## Decision

**Optamos por la Opcion B.** `scripts/check-prerelease-deps.py` parsea los `.csproj` (como XML, no con
`grep`) y termina en 1 si encuentra una version en prerelease o flotante sin declarar. La unica
excepcion valida es un paquete que **nunca** ha publicado una estable, y se declara en
`PRERELEASE-PERMITIDOS.txt` con motivo y fecha de revision.

---

## Consecuencias

**Positivas:**
- Hoy `Common` tiene cero prereleases y cero excepciones.

**Negativas / trade-offs a vigilar:**
- Obligo a sacar el exportador de Prometheus ([ADR-0005](0005-metricas-y-trazas-solo-por-otlp.md)).
- Este repo no tiene CI propio: la puerta corre en local y en los consumidores. En el espejo
  `Raptor057/ApiCommon` si corre en CI, en cada push y antes de publicar.
