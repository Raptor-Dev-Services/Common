# Changelog

Cambios de cada version, con lo que hay que hacer para subir. Sigue
[versionado semantico](https://semver.org/lang/es/): un cambio que rompe sube la mayor.

La version es la del tag de git (`vX.Y.Z`) y la del paquete `Raptor.Common.*` en nuget.org: el
mismo numero es el mismo codigo.

## [Sin publicar]

## [2.2.0] - 2026-10-27

### Agregado
- **Excepciones comunes y errores con envelope** (se publica como v2.2.0, minor: solo agrega).
  - `Common.Contracts`: `ResourceNotFoundException` (404), `DataIntegrityException` (422) y
    `BusinessRuleViolationException` (409), todas subclases de `BusinessRuleException` (400).
  - `Common.Web`: `UseEnvelopeExceptionHandling()` / `EnvelopeExceptionMiddleware` responde esas
    excepciones con el envelope `ResultViewModel` (camelCase), cualquier otra como 500 con mensaje
    generico (el detalle solo al log), la peticion cancelada por el cliente como 499 sin log de error,
    y no escribe si la respuesta ya empezo. Los logs llevan `TraceId` y `CorrelationId`.
    Ver [guia 05](docs/guia/05-errores.md).
- **`Common.Workflow`**, ensamblado nuevo de calculo puro
  ([ADR-0009](docs/adr/0009-motores-de-calculo-en-common-workflow.md)). Solo depende de
  `Common.Contracts`, no lee el reloj y **no entra a la facade**: se referencia explicito.
  - `DefinitionError` / `DefinitionFailure<T>`: una definicion invalida devuelve todos sus errores
    como `IValidationFailure`, con codigos estables.
  - `Calendars`: `BusinessCalendar` (horas y dias habiles, feriados, cambio de horario) y
    `ServiceClock` (vencimientos con pausa y escalamiento).
  - `States`: `StateMachine<TState,TContext>` con matriz por rol que deniega por omision, motivo,
    guardas, ganchos con veto y actividad `workflow.transition`.
  - `Rules`: `RuleCollection` con criterios AND/OR, capturas regex `#n`, orden, stop y modo `Explain`.
  - `Alerts`: `AlertEvaluator` (umbral, fin, preaviso, periodicidad), `IAlertLedger` +
    `InMemoryAlertLedger` y fechas de contrato (`ContractSchedule`).
  - `Folios`: formato `T-000123` por ambito, `IFolioGenerator` + `InMemoryFolioGenerator`.
  - `Forms`: `FormConditionEngine` (visibilidad por punto fijo, validacion, destinos).
  - `Depreciation`: lineal y decreciente, valor en libros, garantia y TCO. **Las tasas fiscales las pone
    el consumidor**; los vectores de prueba con tasas LISR aun no los revisa un contador.

### Migracion
- Excepciones y `UseEnvelopeExceptionHandling`: nada que hacer si no los usas; `UseCoreProblemDetails` sigue igual
  (las subclases nuevas salen ahi como 400).
- Nada que hacer si no lo usas. Para usarlo, agrega un `ProjectReference` a
  `Common/Common.Workflow/Common.Workflow.csproj` (submodulo) o el paquete `Raptor.Common.Workflow`.

## [2.1.3] - 2026-09-24

Corrige los cuatro problemas conocidos de la 2.1.2.

### Seguridad
- **Un tenant ya no recibe la base de otro.** `ITenantConnectionStringResolver` devolvia la cadena
  global `ConnectionStrings:{nombre}` cuando el tenant no tenia la suya o no estaba en el catalogo: un
  tenant mal configurado trabajaba sobre la base compartida o ajena sin ningun error. Ahora devuelve
  solo la cadena del propio tenant, y si no existe, falla.
- **Un host que es una IP ya no resuelve tenant.** Con `ResolveFromSubdomain` (activo por defecto),
  `192.168.1.10` resolvia el tenant `"192"`.

### Corregido
- `ITenantExecutionContextRunner.RunAsync` ya no deja sin tenant al flujo que lo llama. El setter de
  `TenantContextAccessor.Current` vaciaba el contenedor que compartia con ese flujo.
- Las migraciones reintentan solo lo transitorio (`NpgsqlException.IsTransient`). Un error del script
  fallaba tras unos 85 s de reintentos; ahora falla a la primera.

### Migracion desde 2.1.2
- **Si usas una base por tenant**, revisa que cada tenant del catalogo tenga su cadena en
  `MultiTenancy:Tenants:{id}:ConnectionStrings`. Uno que dependia sin saberlo de la cadena global
  ahora falla al abrir la conexion: es el comportamiento buscado.
- **Si todos tus tenants comparten una sola base**, no uses `ITenantConnectionStringResolver` ni las
  fabricas por tenant: usa una fabrica fija (`ConfigurationNpgsqlConnectionFactory<T>`).
- Si llamabas a la API por IP esperando un tenant del primer octeto (no deberia), ahora no se resuelve.

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

### Problemas conocidos (corregidos en la 2.1.3)
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
