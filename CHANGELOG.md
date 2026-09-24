# Changelog

Cambios de cada version, con lo que hay que hacer para subir. Sigue
[versionado semantico](https://semver.org/lang/es/): un cambio que rompe sube la mayor.

La version es la del tag de git (`vX.Y.Z`) y la del paquete `Raptor.Common.*` en nuget.org: el
mismo numero es el mismo codigo.

## [2.1.2] - 2026-09-24

### Corregido
- **Una `BusinessRuleException` lanzada de forma sincrona respondia 500 en vez de 400.** El mediator
  invoca al handler por reflexion, y una excepcion lanzada antes del primer `await` llegaba envuelta
  en `TargetInvocationException`: ni `InteractorPipeline` ni `UseCoreProblemDetails` la reconocian.
  Ahora sale de `Send` tal cual se lanzo, sea el handler `async` o no.
- **Las respuestas de error salian con `Content-Type: application/json`** en vez de
  `application/problem+json`, en `UseCoreProblemDetails` y en los rechazos de `UseTenantResolution`.
- **`UseCorrelationId` copiaba el header del cliente sin sanear** a la respuesta y a los logs: un
  cliente podia meter saltos de linea en los logs (log forging) o inflarlos. Ahora solo se conservan
  letras, digitos y `- _ . :`, hasta 128 caracteres; si no queda nada, se genera un id nuevo.
- El log de una excepcion no controlada en el pipeline decia `Error cr�tico` por un problema de
  codificacion del archivo fuente.

### Agregado
- Guia de uso completa en `docs/guia/`: instalacion como submodulo y como NuGet, primer endpoint, y
  una guia por capacidad.
- Comentarios XML en toda la API publica: el IDE muestra la documentacion de cada tipo y metodo.
- `CONTRIBUTING.md` y este `CHANGELOG.md`.

### Migracion desde 2.1.1
Nada que cambiar. Si tenias un `catch (TargetInvocationException)` para esquivar el primer defecto,
ya no hace falta.

### Problemas conocidos (sin corregir en esta version)
Comprobados y registrados en el SRS como requisitos que no se cumplen. Como evitarlos esta en las
guias 06 y 08.
- Un tenant sin cadena de conexion propia, o fuera del catalogo, recibe la cadena global
  (REQ-SEC-006).
- Un host que es una IP resuelve un tenant con la resolucion por subdominio (REQ-SEC-007).
- `ITenantExecutionContextRunner.RunAsync` deja sin tenant al flujo que lo llama si ese flujo ya
  tenia uno (REQ-FUNC-009).
- Las migraciones reintentan tambien los errores de SQL, no solo los de conectividad (REQ-REL-002).

## [2.1.1] - 2026-09-24

### Seguridad
- Los parametros SQL que registra `DapperSqlDbConnectionBase` se enmascaran como los logs del
  pipeline. Antes, el `INSERT` de un usuario dejaba su hash de contrasena en el log. Incluye
  `DynamicParameters`.
- Los diccionarios y `ExpandoObject` se enmascaran por clave. Antes cada entrada salia como
  `{Key, Value}` con el valor en claro.

### Agregado
- Licencia MIT.

### Migracion desde 2.1.0
Nada que cambiar.

## [2.1.0] - 2026-09-24

### Seguridad
- `InteractorPipeline` enmascara peticiones y respuestas antes de registrarlas. Antes escribia en
  claro contrasenas y tokens a nivel `Information`.

### Cambiado (rompe)
- `AddObservability` pide el nombre del meter del proyecto: `AddObservability(configuration, meterName)`.
- Las metricas salen solo por OTLP. Ya no hay exportador de Prometheus ni endpoint `/metrics`
  ([ADR-0005](docs/adr/0005-metricas-y-trazas-solo-por-otlp.md)).

### Agregado
- `Observability:MetricsOtlpEndpoint`, para mandar las metricas a un destino propio por HTTP.
- Puerta de dependencias estables (`scripts/check-prerelease-deps.py`).
- SRS y ADR en `docs/`.

### Migracion desde 2.0.0
1. Pasa el nombre del meter: `builder.Services.AddObservability(builder.Configuration, meterName: "MiApi");`
2. Quita `app.MapPrometheusScrapingEndpoint()` si lo tenias.
3. Si Prometheus raspaba `/metrics`: arrancalo con `--web.enable-otlp-receiver` y configura
   `Observability:MetricsOtlpEndpoint` con su receptor OTLP
   (`http://prometheus:9090/api/v1/otlp/v1/metrics`).

## [2.0.0] - 2026-06-04

### Cambiado (rompe)
- `Common` se divide en cinco ensamblados mas una facade: `Common.Contracts`, `Common.Messaging`,
  `Common.MultiTenancy`, `Common.Infra`, `Common.Web` y `Common`
  ([ADR-0001](docs/adr/0001-dividir-en-sub-librerias-con-facade.md)).

### Migracion desde 1.1.0
- **Sin cambios de codigo:** los namespaces son los mismos. Quien referencia `Common.csproj` sigue
  compilando.
- Opcional: cambia la referencia a la facade por las sub-librerias que use cada capa (tabla en el
  README).

## [1.1.0] - 2026-05-11

### Agregado
- Multi-tenancy: resolucion por header, query string o subdominio; catalogo de tenants por
  configuracion; contexto ambiental; ejecucion fuera de HTTP; propagacion a `HttpClient`.
- Fabricas de conexion (fija, por tipo marcador, por tenant, por tenant actual) y sus variantes Npgsql.
- `IDapperSqlDbConnection` y `DapperSqlDbConnectionBase`: Dapper con medicion de tiempo y logging.
- Migraciones SQL al arranque (`AddSchemaMigrations`) y health check de PostgreSQL.

## [1.0.0] - 2026-02-22

Primera version: resultados estandar, `BusinessRuleException` y `ErrorList`, mediator propio con
`InteractorPipeline`, `ResultViewModel`, middlewares de correlacion y ProblemDetails, logging con
Serilog y Seq, trazas y metricas con OpenTelemetry, health checks, resiliencia HTTP y opciones
validadas.
