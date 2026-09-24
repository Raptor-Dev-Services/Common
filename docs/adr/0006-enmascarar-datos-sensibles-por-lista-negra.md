# ADR-0006 - Enmascarar datos sensibles en el pipeline por lista negra de nombres

**Fecha:** 2026-09-22 (registrado a posteriori el 2026-09-24)
**Estado:** Aceptado
**Proyecto:** Common
**Origen:** commits `57d8ff2` y `e198fd9`, PR #1. Pruebas en `Common.Tests/SensitiveDataMaskerTests.cs`.

---

## Contexto

`InteractorPipeline` registraba cada peticion y cada respuesta destructuradas (`{@Request}`,
`{@Response}`) a nivel `Information`, que es el que usan los consumidores en produccion. En una hora
de uso de desarrollo de un consumidor aparecieron 5 contrasenas y 12 JWT en claro, en consola y en Seq.

---

## Opciones consideradas

### Opcion A - Dejar de registrar peticiones y respuestas

**Contras:** se pierde el log con el que se depura casi todo.

### Opcion B - Lista blanca: solo se registra lo marcado como seguro

**Pros:** un campo nuevo nunca se filtra por descuido.
**Contras:** hay que anotar cada DTO de cada producto; el log pierde su valor hasta que se anota.

### Opcion C - Lista negra por nombre de propiedad

**Pros:** funciona hoy, sin tocar los DTO; conserva lo que no es secreto (el correo de un login).
**Contras:** un campo sensible con un nombre fuera de la lista se registra en claro.

---

## Decision

**Optamos por la Opcion C.** `SensitiveDataMasker` tapa con `***` por coincidencia **parcial** e
insensible a mayusculas (`password` cubre `NewPassword` y `PasswordHash`). Tapa **sin leer** el
valor, corta a profundidad 4 para no desbordar la pila con un grafo ciclico, y exime por **sufijo**
lo que describe al secreto sin contenerlo (`AccessTokenExpiresAt`).

---

## Consecuencias

**Positivas:**
- Las dos formas vistas en el log real quedan cubiertas por prueba.

**Negativas / trade-offs a vigilar:**
- Es una lista negra: el limite es conocido y aceptado. Pasar a lista blanca es otro ADR.
- `Terminos` es publica pero de solo lectura (un arreglo tras `IReadOnlyList`): el comentario del
  codigo dice que un producto puede sumar los suyos, y hoy no puede. Un termino nuevo va en `Common`.
- **Solo cubre el pipeline.** `DapperSqlDbConnectionBase` registra `{@Params}` sin enmascarar: los
  parametros de un `INSERT` de usuarios llegan al log tal cual.

> **Nota posterior (2026-09-24, v2.1.1).** La decision no cambia; se amplia su alcance. Los
> parametros SQL ya pasan por el mismo enmascarado, y los diccionarios se tapan por clave (antes
> salian como pares `{Key, Value}` con el valor en claro). Ver REQ-SEC-004 en el SRS.
