# Especificacion de Requisitos de Software (SRS)
## Para Common - libreria base para WebApi .NET

Version 1.0 del documento, para `Common` v2.1.0
Preparado por Rogelio Arriaga
Raptor Dev Services
2026-09-24

> Estructura tomada de la plantilla MSRS (Markdown Software Requirements Specification), alineada con
> IEEE 830 e ISO/IEC/IEEE 29148. Las secciones que no aplican a una libreria se conservan y dicen
> por que no aplican, para que la ausencia sea una decision y no un olvido.

## Tabla de contenido
<!-- TOC -->
* [1. Introduccion](#1-introduccion)
    * [1.1 Proposito del documento](#11-proposito-del-documento)
    * [1.2 Alcance del producto](#12-alcance-del-producto)
    * [1.3 Definiciones, acronimos y abreviaturas](#13-definiciones-acronimos-y-abreviaturas)
    * [1.4 Referencias](#14-referencias)
    * [1.5 Organizacion del documento](#15-organizacion-del-documento)
* [2. Vision general del producto](#2-vision-general-del-producto)
    * [2.1 Perspectiva del producto](#21-perspectiva-del-producto)
    * [2.2 Funciones del producto](#22-funciones-del-producto)
    * [2.3 Restricciones del producto](#23-restricciones-del-producto)
    * [2.4 Caracteristicas de los usuarios](#24-caracteristicas-de-los-usuarios)
    * [2.5 Supuestos y dependencias](#25-supuestos-y-dependencias)
    * [2.6 Reparto de requisitos](#26-reparto-de-requisitos)
* [3. Requisitos](#3-requisitos)
    * [3.1 Interfaces externas](#31-interfaces-externas)
    * [3.2 Funcionales](#32-funcionales)
    * [3.3 Calidad de servicio](#33-calidad-de-servicio)
    * [3.4 Cumplimiento](#34-cumplimiento)
    * [3.5 Diseno e implementacion](#35-diseno-e-implementacion)
    * [3.6 IA/ML](#36-iaml)
* [4. Verificacion](#4-verificacion)
* [5. Apendices](#5-apendices)
<!-- TOC -->

## Historial de revisiones

| Nombre | Fecha | Motivo del cambio | Version |
|---|---|---|---|
| Rogelio Arriaga | 2026-09-24 | Primera version: requisitos extraidos del codigo de `aedf830` y de los ADR 0001-0008. | 1.0 |

## 1. Introduccion

### 1.1 Proposito del documento

Este documento dice **que** tiene que hacer `Common` y **como se comprueba**, no como esta hecho: el
como vive en el codigo y el porque en los ADR de [`docs/adr/`](adr/README.md). Esta escrito para
quien mantiene la libreria (que no puede romper), para quien la consume (que puede esperar de ella)
y para quien revisa un cambio (con que requisito se contrasta).

### 1.2 Alcance del producto

`Common` es una libreria .NET 10 que resuelve lo transversal de una WebApi para que cada producto no
lo reescriba: resultados y errores estandar, un mediator con tuberia, multi-tenencia, logging
estructurado, observabilidad, resiliencia HTTP, health checks, fabricas de conexion y migraciones
SQL al arranque.

**Incluye:** los seis ensamblados de la solucion (`Common.Contracts`, `Common.Messaging`,
`Common.MultiTenancy`, `Common.Infra`, `Common.Web` y la facade `Common`) y sus dos canales de
distribucion.

**No incluye:** reglas de negocio de ningun producto, autenticacion y autorizacion (cada producto trae
la suya), acceso a datos con ORM, ni infraestructura (Seq, Postgres, colector OTLP): eso lo pone el
consumidor.

### 1.3 Definiciones, acronimos y abreviaturas

| Termino | Definicion |
|---|---|
| Consumidor | Proyecto que referencia `Common`, por submodulo o por paquete NuGet. |
| Facade | El proyecto `Common.csproj`: no tiene codigo, re-exporta las cinco sub-librerias. |
| Handler | Clase que atiende una peticion (`IRequestHandler`). En los consumidores se llama *interactor*. |
| OTLP | OpenTelemetry Protocol: el formato con el que salen trazas y metricas. |
| Pipeline | Cadena de `IPipelineBehavior` que envuelve a cada handler. |
| Presenter | Handler de notificacion (`INotificationHandler`) que recibe la respuesta de un caso de uso. |
| ProblemDetails | Formato de error HTTP de RFC 9457 (`application/problem+json`). |
| Seq | Servidor de logs estructurados. |
| Tenant | Cliente aislado dentro de una misma instancia de un consumidor multi-tenant. |

### 1.4 Referencias

| Referencia | Tipo | Ubicacion |
|---|---|---|
| ADR 0001-0008 | Normativa | [`docs/adr/`](adr/README.md) |
| Regla de dependencias estables | Normativa | [`.claude/rules/stable-dependencies.md`](../.claude/rules/stable-dependencies.md) |
| Plan de la division en sub-librerias | Informativa | [`REFACTORING-PLAN.md`](../REFACTORING-PLAN.md) |
| Guia de uso | Informativa | [`README.md`](../README.md) |
| RFC 9457, Problem Details for HTTP APIs | Normativa | https://www.rfc-editor.org/rfc/rfc9457 |
| Plantilla MSRS | Informativa | https://github.com/jam01/SRS-Template |

### 1.5 Organizacion del documento

La seccion 2 da el contexto: de donde viene la libreria, quien la usa y que la restringe. La 3 tiene
los requisitos verificables, cada uno con un identificador `REQ-AREA-NNN` que no se reutiliza nunca.
La 4 dice como se verifica cada uno y en que estado esta hoy. Un requisito que cambia conserva su ID
y lo anota en el historial de revisiones.

## 2. Vision general del producto

### 2.1 Perspectiva del producto

`Common` nacio en febrero de 2026 como la base compartida de los productos de Raptor Dev Services y
se alineo con el `Common` de GTM-Suite V3. En junio de 2026 se dividio en sub-librerias para servir a
monolitos modulares ([ADR-0001](adr/0001-dividir-en-sub-librerias-con-facade.md)).

Tiene dos canales de distribucion con el mismo codigo:

```
Raptor-Dev-Services/Common  --(submodulo fijado a commit)-->  productos de la organizacion
            |
            +--(copia 1:1)--> Raptor057/ApiCommon --(nuget.org)--> Raptor.Common.*
```

No hay SLA: es una libreria, no un servicio. El soporte es el del mantenedor.

### 2.2 Funciones del producto

- **Contratos:** `Result`/`Result<T>`, tipos de fallo, `BusinessRuleException`, `ErrorList`.
- **Mediator:** `Send` con tuberia, `Publish` a varios presentadores, registro por escaneo de ensamblados.
- **Multi-tenencia:** resolucion del tenant por request, contexto ambiental, catalogo de tenants por
  configuracion, propagacion a llamadas HTTP salientes y ejecucion fuera de HTTP.
- **Web:** middlewares de correlacion, tenant y ProblemDetails; envelopes `ResultViewModel` y `GenericViewModel`.
- **Logging:** Serilog con enriquecimiento, consola y Seq; enmascarado de datos sensibles.
- **Observabilidad:** trazas y metricas por OTLP.
- **Datos:** fabricas de conexion (fija, por tenant, por tenant actual), envoltorio Dapper con
  medicion de tiempo, migraciones SQL al arranque para PostgreSQL.
- **Operacion:** health checks, resiliencia HTTP, opciones validadas al arranque.

### 2.3 Restricciones del producto

- **RES-1** Debe apuntar a `net10.0` y compilar con el SDK de .NET 10.
- **RES-2** Solo debe depender de paquetes en version estable publicada ([ADR-0007](adr/0007-solo-dependencias-estables.md)).
- **RES-3** `Common.Contracts` no debe depender de ningun paquete ni de otro `Common.*`.
- **RES-4** Los namespaces publicos no deben cambiar al mover un tipo de ensamblado.
- **RES-5** El unico motor de base de datos soportado por las piezas de datos concretas es PostgreSQL.
- **RES-6** No debe contener logica de negocio ni nombres de un producto concreto.

### 2.4 Caracteristicas de los usuarios

| Clase de usuario | Que hace con la libreria | Que necesita |
|---|---|---|
| Desarrollador de un consumidor | Registra servicios en su `Program.cs`, escribe handlers y presentadores. | Una API estable y un README que diga que registrar y en que orden. |
| Mantenedor de `Common` | Cambia la libreria y la distribuye. | Saber que no puede romper: este documento y los ADR. |
| Operacion | Lee los logs, trazas y metricas de los consumidores. | Propiedades consistentes (`Application`, `TenantId`, `CorrelationId`) en todos los productos. |

### 2.5 Supuestos y dependencias

| Supuesto o dependencia | Si resulta falso |
|---|---|
| Los consumidores son aplicaciones ASP.NET Core. | `Common.MultiTenancy` y `Common.Web` no sirven fuera de un host web. |
| El consumidor registra `UseTenantResolution()` antes de sus endpoints. | Los logs y trazas salen sin tenant, y las fabricas "por tenant actual" fallan. |
| El consumidor configura `CustomLogging:LogEventLevel` en produccion. | El nivel cae a `Verbose` y se registra todo (ver REQ-OBS-002). |
| Existe un receptor OTLP (colector, Tempo, Prometheus con receptor OTLP). | No hay trazas ni metricas; la aplicacion sigue funcionando. |
| La base de datos es PostgreSQL. | Las migraciones y las fabricas `Npgsql` no aplican; las abstracciones de `Common.Data` si. |

### 2.6 Reparto de requisitos

| Ensamblado | Requisitos |
|---|---|
| `Common.Contracts` | REQ-FUNC-001, REQ-FUNC-002 |
| `Common.Messaging` | REQ-FUNC-003, REQ-FUNC-004 (contratos) |
| `Common.MultiTenancy` | REQ-FUNC-007, REQ-FUNC-009, REQ-FUNC-011 |
| `Common.Infra` | REQ-FUNC-003 a 006, 010, 015 a 020; REQ-OBS-001 a 004; REQ-SEC-001, 004; REQ-REL-001 a 003 |
| `Common.Web` | REQ-FUNC-008, 012 a 014; REQ-SEC-002, 003 |
| Solucion completa | REQ-MAINT, REQ-BUILD, REQ-PORT, REQ-DIST, REQ-CM, REQ-COMP |

## 3. Requisitos

Cada requisito usa **debe** para lo obligatorio y **deberia** para lo recomendado. La columna
"Verificacion" de la seccion 4 dice si hay prueba automatizada que lo sostenga.

### 3.1 Interfaces externas

#### 3.1.1 Interfaces de usuario

No aplica: `Common` no tiene interfaz de usuario. Su "interfaz" es la API publica de .NET, cubierta
en 3.1.3.

#### 3.1.2 Interfaces de hardware

No aplica.

#### 3.1.3 Interfaces de software

- ID: REQ-INT-001
- Titulo: Punto de entrada por extensiones de registro
- Enunciado: Toda capacidad de la libreria debe poder activarse con un metodo de extension sobre
  `IServiceCollection`, `IHttpClientBuilder`, `IHealthChecksBuilder` o `IApplicationBuilder`, sin que
  el consumidor instancie tipos internos.
- Criterios de aceptacion: existen `AddLoggingServices`, `AddObservability`, `AddMultiTenancy`,
  `AddMediator`, `AddSchemaMigrations`, `AddValidatedOptions`, `AddCoreHealthChecks`,
  `AddCoreResilience`, `AddTenantPropagation`, `UseCorrelationId`, `UseTenantResolution` y `UseCoreProblemDetails`.
- Verificacion: Inspeccion

- ID: REQ-INT-002
- Titulo: Configuracion por secciones conocidas
- Enunciado: La libreria debe leer su configuracion solo de las secciones `CustomLogging`,
  `Observability` y `MultiTenancy`, mas las `ConnectionStrings` que el consumidor nombre.
- Criterios de aceptacion: las claves documentadas en el README son todas las que se leen.
- Verificacion: Inspeccion

- ID: REQ-INT-003
- Titulo: Cabeceras HTTP
- Enunciado: La libreria debe leer y escribir la cabecera de tenant (por defecto `X-Tenant-Id`,
  configurable) y la de correlacion `X-Correlation-Id`.
- Verificacion: Inspeccion

- ID: REQ-INT-004
- Titulo: Destinos de telemetria
- Enunciado: Los logs deben salir por consola y, si se configura, a Seq; las trazas y metricas deben
  salir por OTLP.
- Verificacion: Demostracion

### 3.2 Funcionales

#### Contratos

- ID: REQ-FUNC-001
- Titulo: Resultado estandar
- Enunciado: `Result` y `Result<T>` deben representar el exito (`SuccessResult`, con `Data` en la
  version generica) o el fallo (`FailureResult`, con `Exception` y `Message`), y crearse con
  `Result.OK(...)` y `Result.Fail(...)`.
- Razon: todos los casos de uso devuelven lo mismo, y el presentador decide el status HTTP.
- Criterios de aceptacion: un fallo expone `IFailure`; existen `IValidationFailure`,
  `INotFoundFailure` e `IConflictFailure` para que el consumidor distinga el tipo.
- Verificacion: Inspeccion

- ID: REQ-FUNC-002
- Titulo: Violacion de regla de negocio
- Enunciado: `BusinessRuleException` debe señalar una regla de negocio incumplida, y `ErrorList` debe
  acumular varios mensajes y convertirse en una sola `BusinessRuleException`.
- Verificacion: Inspeccion

#### Mediator

- ID: REQ-FUNC-003
- Titulo: Envio de una peticion
- Enunciado: `IMediator.Send` debe resolver el unico handler registrado para el tipo de peticion y
  ejecutarlo envuelto en todos los `IPipelineBehavior` registrados, en orden de registro (el primero
  registrado es el mas externo).
- Criterios de aceptacion: sin handler registrado lanza `InvalidOperationException` con el nombre
  del tipo; una peticion nula lanza `ArgumentNullException`.
- Verificacion: Sin prueba (ver seccion 4)

- ID: REQ-FUNC-004
- Titulo: Publicacion de una notificacion
- Enunciado: `IMediator.Publish` debe entregar la notificacion a todos los handlers registrados para
  su tipo y completar cuando todos terminen. No se garantiza orden entre ellos.
- Verificacion: Sin prueba

- ID: REQ-FUNC-005
- Titulo: Registro por escaneo
- Enunciado: `AddMediator(ensamblados)` debe registrar como scoped el mediator, `InteractorPipeline`
  y toda clase concreta de esos ensamblados que implemente `IRequestHandler<,>` o `INotificationHandler<>`.
- Verificacion: Sin prueba

- ID: REQ-FUNC-006
- Titulo: Tuberia de interactores
- Enunciado: `InteractorPipeline` debe registrar la peticion y la respuesta **enmascaradas**
  (REQ-SEC-001), registrar un fallo como `Warning`, publicar la respuesta a sus presentadores y
  relanzar cualquier excepcion tras registrarla.
- Verificacion: Sin prueba

#### Multi-tenencia

- ID: REQ-FUNC-007
- Titulo: Resolucion del tenant
- Enunciado: El tenant de un request debe resolverse en este orden, tomando el primero no vacio:
  cabecera (si `ResolveFromHeader`), query string (si `ResolveFromQueryString`), subdominio (si
  `ResolveFromSubdomain`, el host tiene 3 o mas segmentos y el primero no esta en `IgnoredSubdomains`)
  y por ultimo `DefaultTenantId`.
- Criterios de aceptacion: `AddMultiTenancy` falla al arrancar si `RequireTenant` esta activo y no hay
  ninguna estrategia de resolucion.
- Verificacion: Sin prueba

- ID: REQ-FUNC-008
- Titulo: Admision del tenant
- Enunciado: `UseTenantResolution` debe responder ProblemDetails **400** si el tenant es obligatorio
  y no se resolvio, **403** si el tenant esta deshabilitado, y **403** si no esta registrado cuando
  `RejectUnknownTenants` esta activo y hay catalogo. Si lo admite, debe fijar el contexto de tenant,
  devolver el tenant en la cabecera de respuesta, etiquetar la traza con `tenant.id`, abrir un scope de
  log con `TenantId` y limpiar el contexto al terminar el request.
- Verificacion: Sin prueba

- ID: REQ-FUNC-009
- Titulo: Tenant fuera de HTTP
- Enunciado: `ITenantExecutionContextRunner.RunAsync` debe ejecutar trabajo (jobs, consumidores) con
  un tenant fijado, de modo que logs, trazas y fabricas de conexion lo vean como si fuera un request.
- Verificacion: Sin prueba

- ID: REQ-FUNC-010
- Titulo: Propagacion del tenant
- Enunciado: Un `HttpClient` registrado con `AddTenantPropagation` debe enviar el tenant actual en la
  cabecera de tenant, reemplazando cualquier valor previo, y no enviar nada si no hay tenant.
- Verificacion: Sin prueba

- ID: REQ-FUNC-011
- Titulo: Cadena de conexion por tenant
- Enunciado: `ITenantConnectionStringResolver` debe devolver la cadena de conexion nombrada del
  tenant desde el catalogo de configuracion, y `GetRequiredConnectionString` debe lanzar si no existe.
- Verificacion: Sin prueba

#### Web

- ID: REQ-FUNC-012
- Titulo: Identificador de correlacion
- Enunciado: `UseCorrelationId` debe reutilizar `X-Correlation-Id` si llega en el request o generar
  uno nuevo, devolverlo en la respuesta, etiquetar la traza con `correlation_id` y abrir un scope de log
  con `CorrelationId`.
- Verificacion: Sin prueba

- ID: REQ-FUNC-013
- Titulo: Errores como ProblemDetails
- Enunciado: `UseCoreProblemDetails` debe convertir una `BusinessRuleException` no atrapada en
  **400** con su mensaje, y cualquier otra excepcion en **500** con un mensaje generico. Ambas
  respuestas deben ser `application/problem+json` e incluir `traceId` y, si lo hay, `tenantId`.
- Verificacion: Sin prueba

- ID: REQ-FUNC-014
- Titulo: Envelope de respuesta
- Enunciado: `ResultViewModel<T>` y `GenericViewModel<T>` deben exponer `Data`, `IsSuccess`,
  `Message` y `UtcTimeStamp`, y construirse desde un `Result` o con `OK`/`Fail`.
- Verificacion: Inspeccion

#### Datos

- ID: REQ-FUNC-015
- Titulo: Migraciones SQL al arranque
- Enunciado: `AddSchemaMigrations` debe aplicar al arrancar los `.sql` de la carpeta configurada, en
  orden alfabetico, omitiendo los que empiezan con `000_template`, cada uno en su transaccion junto con
  su registro en `dbo.SchemaMigrations`. Un script ya registrado no se vuelve a aplicar.
- Criterios de aceptacion: si la carpeta no existe, avisa y el host arranca igual; si un script
  falla, el host no arranca.
- Verificacion: Sin prueba

- ID: REQ-FUNC-016
- Titulo: Fabricas de conexion
- Enunciado: La libreria debe ofrecer fabricas que abran una conexion por cadena fija, por el nombre
  de un tipo marcador (`ConnectionStrings:{Tipo}`), por tenant explicito y por el tenant actual, con
  variantes concretas para Npgsql.
- Verificacion: Inspeccion

- ID: REQ-FUNC-017
- Titulo: Envoltorio Dapper medido
- Enunciado: `DapperSqlDbConnectionBase` debe ejecutar consultas y comandos abriendo y cerrando su
  propia conexion, midiendo cada ejecucion y registrandola segun REQ-OBS-004.
- Verificacion: Sin prueba

#### Operacion

- ID: REQ-FUNC-018
- Titulo: Health checks
- Enunciado: La libreria debe ofrecer un check `self` y checks de PostgreSQL y Redis registrables con
  nombre, estado de fallo, etiquetas y timeout.
- Verificacion: Inspeccion

- ID: REQ-FUNC-019
- Titulo: Opciones validadas
- Enunciado: `AddValidatedOptions` debe enlazar una seccion de configuracion, validarla con data
  annotations y hacer fallar el arranque si no es valida.
- Verificacion: Inspeccion

- ID: REQ-FUNC-020
- Titulo: Resiliencia HTTP
- Enunciado: `AddCoreResilience` debe aplicar el manejador estandar de resiliencia de .NET a un
  `HttpClient`, con sus valores por defecto o con los de una seccion de configuracion.
- Verificacion: Inspeccion

### 3.3 Calidad de servicio

#### 3.3.1 Rendimiento

- ID: REQ-PERF-001
- Titulo: Sin objetivo de rendimiento medido
- Enunciado: No hay hoy un objetivo cuantitativo. Se registra como deuda que `Send` resuelve el
  handler por reflexion sin cache ([ADR-0003](adr/0003-mediator-propio-en-lugar-de-mediatr.md)).
- Verificacion: Analisis (pendiente)

#### 3.3.2 Seguridad

- ID: REQ-SEC-001
- Titulo: Enmascarado de datos sensibles en el pipeline
- Enunciado: Antes de registrar una peticion o una respuesta, el pipeline debe reemplazar por `***`
  el valor de toda propiedad cuyo nombre contenga un termino sensible (password, contrasena, token,
  secret, apikey, authorization, cardnumber, cvv, cvc, privatekey, connectionstring, otp, totp y
  variantes), sin distinguir mayusculas y **sin leer** el valor. Las propiedades que terminan en un
  sufijo descriptivo (`ExpiresAt`, `Type`, `Length`, `Count`...) no se tapan.
- Razon: [ADR-0006](adr/0006-enmascarar-datos-sensibles-por-lista-negra.md).
- Criterios de aceptacion: una peticion de login no deja la contrasena en el log; una respuesta con
  tokens no los deja; la fecha de caducidad de un token si queda.
- Verificacion: Prueba

- ID: REQ-SEC-002
- Titulo: Un error 500 no expone detalles internos
- Enunciado: La respuesta a una excepcion no controlada no debe incluir su mensaje ni su traza: solo
  un mensaje generico y el `traceId` para buscarla en los logs.
- Verificacion: Inspeccion

- ID: REQ-SEC-003
- Titulo: Tenants no admitidos
- Enunciado: Un request de un tenant deshabilitado, o desconocido cuando hay catalogo y
  `RejectUnknownTenants` esta activo, no debe llegar a ningun endpoint (REQ-FUNC-008).
- Verificacion: Sin prueba

- ID: REQ-SEC-004
- Titulo: Enmascarado de los parametros SQL en el log
- Enunciado: Los parametros que `DapperSqlDbConnectionBase` registra deben pasar por el mismo
  enmascarado que REQ-SEC-001.
- Razon: hoy registra `{@Params}` tal cual, y un `INSERT` de usuarios lleva su hash de contrasena.
- Verificacion: Prueba. **Estado: no se cumple** (ver seccion 4).

#### 3.3.3 Confiabilidad

- ID: REQ-REL-001
- Titulo: El enmascarado nunca tumba el request
- Enunciado: Enmascarar no debe lanzar: una propiedad que falla al leerse se registra como
  `<no legible>`, un grafo ciclico se corta a profundidad 4 y una coleccion a 50 elementos.
- Verificacion: Prueba

- ID: REQ-REL-002
- Titulo: Migraciones tolerantes a una base que aun no esta lista
- Enunciado: Ante un error de conectividad de Npgsql, las migraciones deben reintentar hasta 20 veces
  con espera creciente (1 s por intento, maximo 5 s) antes de fallar.
- Verificacion: Sin prueba

- ID: REQ-REL-003
- Titulo: La telemetria es opcional
- Enunciado: Sin `SeqUri` ni endpoints OTLP configurados, la aplicacion debe arrancar y funcionar;
  solo deja de exportar.
- Verificacion: Inspeccion

#### 3.3.4 Disponibilidad

No aplica: `Common` no se ejecuta por si sola. La disponibilidad es de cada consumidor.

#### 3.3.5 Observabilidad

- ID: REQ-OBS-001
- Titulo: Logs enriquecidos
- Enunciado: Todo evento de log debe llevar `Project`, `Application`, `Version`, `Environment`,
  `MachineName` y, cuando existan, `TenantId` y `CorrelationId`.
- Verificacion: Demostracion

- ID: REQ-OBS-002
- Titulo: Nivel de log
- Enunciado: El nivel minimo debe salir de `CustomLogging:LogEventLevel`; sin ese valor es `Verbose`,
  y en `Development` debe ser como maximo `Debug`.
- Verificacion: Inspeccion

- ID: REQ-OBS-003
- Titulo: Trazas y metricas por OTLP
- Enunciado: Las trazas (ASP.NET Core y HttpClient) y las metricas (ASP.NET Core, HttpClient, runtime y
  el meter del consumidor) deben exportarse por OTLP a `Observability:OtlpEndpoint`; si existe
  `Observability:MetricsOtlpEndpoint`, las metricas deben ir ahi por HTTP/protobuf.
- Razon: [ADR-0005](adr/0005-metricas-y-trazas-solo-por-otlp.md).
- Verificacion: Demostracion

- ID: REQ-OBS-004
- Titulo: Consultas lentas
- Enunciado: Cada ejecucion SQL del envoltorio Dapper debe registrarse con nombre, resultado, tiempo
  y hash del SQL; a partir de 300 ms como `Warning`, de 1000 ms como `Error` y de 2000 ms como
  `Critical`. El texto del SQL solo se registra si `CustomLogging:IncludeSqlText` es verdadero.
- Verificacion: Sin prueba

### 3.4 Cumplimiento

- ID: REQ-COMP-001
- Titulo: Licencia declarada
- Enunciado: Cada canal de distribucion debe declarar la licencia bajo la que se usa el codigo.
- Razon: el espejo `Raptor057/ApiCommon` declara MIT (`LICENSE` y `PackageLicenseExpression`), pero
  `Raptor-Dev-Services/Common` es publico y no tiene archivo de licencia, lo que por defecto significa
  "todos los derechos reservados". El mismo codigo queda con dos licencias distintas segun por donde llegue.
- Verificacion: Inspeccion. **Estado: no se cumple en `Raptor-Dev-Services/Common`** (ver seccion 4).

### 3.5 Diseno e implementacion

#### 3.5.1 Instalacion

- ID: REQ-INST-001
- Titulo: Instalacion por submodulo o por paquete
- Enunciado: Un consumidor debe poder incorporar `Common` como submodulo con `ProjectReference`
  relativo, o como paquetes `Raptor.Common.*` desde nuget.org, sin pasos adicionales.
- Verificacion: Demostracion

#### 3.5.2 Build y entrega

- ID: REQ-BUILD-001
- Titulo: Solo dependencias estables
- Enunciado: Ningun `.csproj` debe referenciar un paquete en prerelease ni una version flotante, salvo
  excepcion declarada en `PRERELEASE-PERMITIDOS.txt` con motivo y fecha de revision.
- Razon: [ADR-0007](adr/0007-solo-dependencias-estables.md).
- Verificacion: Prueba (`scripts/check-prerelease-deps.py`)

- ID: REQ-BUILD-002
- Titulo: Build limpio
- Enunciado: `dotnet build Common.slnx` debe terminar con 0 errores y 0 warnings.
- Verificacion: Prueba

- ID: REQ-BUILD-003
- Titulo: Un arreglo de seguridad trae su prueba
- Enunciado: Todo cambio que corrija una fuga o un control de seguridad debe traer una prueba que falle
  sin el arreglo.
- Verificacion: Inspeccion (en la revision)

#### 3.5.3 Distribucion

- ID: REQ-DIST-001
- Titulo: Submodulo fijado
- Enunciado: Los consumidores de la organizacion deben fijar el submodulo a un commit o tag, nunca a
  una rama.
- Razon: [ADR-0008](adr/0008-consumo-como-submodulo-fijado-a-commit.md).
- Verificacion: Inspeccion

- ID: REQ-DIST-002
- Titulo: Espejo publico en nuget.org
- Enunciado: El repositorio `Raptor057/ApiCommon` debe contener el mismo codigo que un tag de este
  repositorio y publicarlo como un paquete `Raptor.<Proyecto>` por ensamblado, con la misma version que
  ese tag.
- Verificacion: Inspeccion (comparacion blob a blob al sincronizar)

#### 3.5.4 Mantenibilidad

- ID: REQ-MAINT-001
- Titulo: Grafo de dependencias entre ensamblados
- Enunciado: Las referencias entre ensamblados deben ser exactamente: `Contracts` -> nada;
  `Messaging` -> `Contracts`; `MultiTenancy` -> nada de `Common.*`; `Web` -> `Contracts` +
  `MultiTenancy`; `Infra` -> `Contracts` + `Messaging` + `MultiTenancy`; facade -> los cinco.
- Razon: [ADR-0001](adr/0001-dividir-en-sub-librerias-con-facade.md) y [ADR-0002](adr/0002-multitenancy-como-modulo-base.md).
- Verificacion: Inspeccion

- ID: REQ-MAINT-002
- Titulo: Decisiones registradas
- Enunciado: Una decision que cambie lo que reciben los consumidores o sea cara de revertir debe
  registrarse como ADR en `docs/adr/`.
- Verificacion: Inspeccion

#### 3.5.5 Reutilizacion

- ID: REQ-REUSE-001
- Titulo: Referencia por capa
- Enunciado: Cada capa de un consumidor modular debe poder referenciar solo el ensamblado que usa:
  `Domain` -> `Contracts`; `Application` -> `Contracts` + `Messaging`; `Infrastructure` -> + `Infra`;
  `Presentation` -> + `Web`; `Host` -> `Infra` + `Web`.
- Verificacion: Inspeccion

#### 3.5.6 Portabilidad

- ID: REQ-PORT-001
- Titulo: Plataforma
- Enunciado: La libreria debe correr donde corra .NET 10 (Windows, Linux, contenedores), sin codigo
  especifico de un sistema operativo.
- Verificacion: Demostracion (CI del espejo en Ubuntu; desarrollo en Windows)

#### 3.5.7 Costo

No aplica: ninguna dependencia tiene costo de licencia ([ADR-0003](adr/0003-mediator-propio-en-lugar-de-mediatr.md)).

#### 3.5.8 Fechas limite

No aplica: la libreria evoluciona con sus consumidores, sin fechas comprometidas.

#### 3.5.9 Prueba de concepto

No aplica.

#### 3.5.10 Gestion de cambios

- ID: REQ-CM-001
- Titulo: Versionado semantico
- Enunciado: Cada version debe etiquetarse `vMAYOR.MENOR.PARCHE`. Romper un contrato publico, cambiar
  el framework o retirar una dependencia que el consumidor configura sube la mayor; agregar sin romper
  sube la menor; corregir sube el parche.
- Verificacion: Inspeccion

- ID: REQ-CM-002
- Titulo: Retiro de la facade
- Enunciado: La facade `Common.csproj` debe retirarse solo cuando ningun consumidor la referencie, y
  en una version mayor.
- Verificacion: Inspeccion

### 3.6 IA/ML

No aplica: `Common` no incorpora modelos de aprendizaje automatico.

## 4. Verificacion

Estado medido el 2026-09-24 sobre `aedf830`: `dotnet build Common.slnx -c Release` con **0 warnings
y 0 errores**, `dotnet test` con **12 de 12** pruebas en verde (todas en
`Common.Tests/SensitiveDataMaskerTests.cs`), y la puerta de dependencias en **verde**.

| Requisito | Metodo | Artefacto | Estado |
|---|---|---|---|
| REQ-SEC-001 | Prueba | `Common.Tests/SensitiveDataMaskerTests.cs` | Cumple |
| REQ-REL-001 | Prueba | `SensitiveDataMaskerTests` (getter que lanza, grafo ciclico) | Cumple |
| REQ-BUILD-001 | Prueba | `scripts/check-prerelease-deps.py` | Cumple |
| REQ-BUILD-002 | Prueba | `dotnet build Common.slnx` | Cumple |
| REQ-MAINT-001 | Inspeccion | los seis `.csproj` | Cumple |
| REQ-SEC-004 | Prueba | - | **No cumple**: `DapperSqlDbConnectionBase` registra `{@Params}` sin enmascarar |
| REQ-COMP-001 | Inspeccion | - | **No cumple** en `Raptor-Dev-Services/Common`: no hay archivo `LICENSE` (el espejo si lo tiene) |
| REQ-FUNC-003 a 013, 015, 017 | Prueba | - | Sin prueba automatizada |
| REQ-SEC-003, REQ-REL-002, REQ-OBS-004 | Prueba | - | Sin prueba automatizada |
| REQ-INT-*, REQ-FUNC-001, 002, 014, 016, 018 a 020 | Inspeccion | codigo fuente | Cumple por inspeccion |
| REQ-OBS-001, 003, REQ-INT-004, REQ-INST-001, REQ-PORT-001 | Demostracion | un consumidor corriendo | Cumple en los consumidores; sin prueba en la libreria |
| REQ-PERF-001 | Analisis | - | Pendiente |

**La brecha principal es de pruebas:** el mediator, la multi-tenencia, los middlewares y las
migraciones solo se prueban a traves de los consumidores. Son los candidatos naturales para las
siguientes pruebas, empezando por REQ-FUNC-008 y REQ-SEC-003, que son los que protegen el aislamiento
entre tenants.

## 5. Apendices

### 5.1 Configuracion de referencia

La configuracion completa, con todas las claves que lee la libreria, esta en la seccion
"Configuracion esperada" del [`README.md`](../README.md).

### 5.2 Mapa de requisitos a ADR

| ADR | Requisitos que sostiene |
|---|---|
| [0001](adr/0001-dividir-en-sub-librerias-con-facade.md) | REQ-MAINT-001, REQ-REUSE-001, REQ-CM-002 |
| [0002](adr/0002-multitenancy-como-modulo-base.md) | REQ-MAINT-001 |
| [0003](adr/0003-mediator-propio-en-lugar-de-mediatr.md) | REQ-FUNC-003 a 006, REQ-PERF-001 |
| [0004](adr/0004-logging-con-serilog-y-seq.md) | REQ-OBS-001, REQ-OBS-002 |
| [0005](adr/0005-metricas-y-trazas-solo-por-otlp.md) | REQ-OBS-003 |
| [0006](adr/0006-enmascarar-datos-sensibles-por-lista-negra.md) | REQ-SEC-001, REQ-SEC-004, REQ-REL-001 |
| [0007](adr/0007-solo-dependencias-estables.md) | REQ-BUILD-001 |
| [0008](adr/0008-consumo-como-submodulo-fijado-a-commit.md) | REQ-DIST-001, REQ-DIST-002 |
