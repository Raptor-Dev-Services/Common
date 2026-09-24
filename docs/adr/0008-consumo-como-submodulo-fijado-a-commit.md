# ADR-0008 - Los consumidores reciben Common como submodulo fijado a commit

**Fecha:** 2026-02-14 (registrado a posteriori el 2026-09-24)
**Estado:** Aceptado
**Proyecto:** Common
**Origen:** regla `common-library` del catalogo de Raptor Dev Services, que fija la tabla de versiones.

---

## Contexto

`Common` la consumen varios productos de la organizacion que evolucionan a ritmos distintos. Cada uno tiene que compilar igual hoy que dentro de seis meses, y poder depurar dentro de
`Common` sin salir de su propio repo.

---

## Opciones consideradas

### Opcion A - Paquete NuGet en un feed

**Pros:** version explicita en el `.csproj`; es lo estandar en .NET.
**Contras:** publicar cada cambio antes de poder probarlo en un consumidor; depurar dentro de la
libreria pide symbols y SourceLink.

### Opcion B - Submodulo de git fijado a un commit o tag

**Pros:** el codigo esta ahi, se lee y se depura; el puntero del submodulo es la version exacta.
**Contras:** `git submodule update --init` es un paso que se olvida, y es facil mover el puntero sin querer.

### Opcion C - Submodulo siguiendo una rama

**Contras:** dos clones del mismo commit del consumidor compilan cosas distintas.

---

## Decision

**Optamos por la Opcion B.** Cada consumidor agrega `Common` como submodulo, lo fija a un commit de la
tabla de versiones y lo referencia con `ProjectReference` relativo. El submodulo es **de solo lectura**
desde el consumidor: los cambios se hacen aqui y el consumidor mueve su puntero.

---

## Consecuencias

**Positivas:**
- Subir de version es un commit visible en el consumidor, con su diff.

**Negativas / trade-offs a vigilar:**
- La tabla de versiones vive en el catalogo, no aqui: hay que actualizarla en cada tag.
- Para uso fuera de la organizacion existe el espejo `Raptor057/ApiCommon`, que publica el mismo
  codigo como paquetes `Raptor.Common.*` en nuget.org. Es un segundo canal de distribucion, no el
  de los productos.
