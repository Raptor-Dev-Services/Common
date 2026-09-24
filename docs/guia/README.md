# Guia de uso

Todo lo necesario para usar `Common` en una WebApi .NET 10, de la instalacion al primer endpoint y
cada capacidad por separado.

## Por donde empezar

1. **Instalala**, por uno de los dos caminos:
   - [Como submodulo de git](01-instalar-como-submodulo.md): el codigo vive dentro de tu repo.
   - [Como paquete NuGet](02-instalar-desde-nuget.md): `Raptor.Common` desde nuget.org.
2. **Haz tu primer endpoint** con [la guia rapida](03-primer-endpoint.md): un caso de uso completo,
   de la peticion HTTP a la respuesta, en un solo archivo.
3. **Profundiza** en lo que vayas necesitando:

| Guia | Que cubre |
|---|---|
| [04 - Casos de uso y mediator](04-casos-de-uso-y-mediator.md) | Peticiones, handlers, respuestas tipadas o `Result<T>`, presenters, pipeline, registro. |
| [05 - Errores](05-errores.md) | `BusinessRuleException`, `ErrorList`, respuestas ProblemDetails, mapeo a status HTTP. |
| [06 - Multi-tenancy](06-multi-tenancy.md) | Resolucion del tenant, catalogo por configuracion, trabajo fuera de HTTP, propagacion. |
| [07 - Logging y observabilidad](07-logging-y-observabilidad.md) | Serilog y Seq, enmascarado de secretos, correlacion, trazas y metricas por OTLP. |
| [08 - Datos y migraciones](08-datos-y-migraciones.md) | Fabricas de conexion, envoltorio Dapper medido, migraciones SQL al arranque. |
| [09 - HTTP, salud y opciones](09-http-salud-y-opciones.md) | Resiliencia de `HttpClient`, health checks, opciones validadas al arrancar. |

## Submodulo o NuGet: ¿es lo mismo?

**Para tu codigo, si.** Los dos caminos entregan el mismo codigo, los mismos namespaces y los mismos
nombres de ensamblado (`Common.Contracts.dll`, `Common.Infra.dll`...). Un `using Common.Messaging;` y
todo lo que escribes encima es identico, y puedes cambiar de un camino al otro sin tocar una linea de
C#: solo cambias las referencias en los `.csproj`.

Lo que cambia es todo lo de alrededor:

| | Submodulo | NuGet |
|---|---|---|
| Como se referencia | `ProjectReference` a cada `.csproj` | `PackageReference` a `Raptor.Common.*` |
| Como se fija la version | El commit del submodulo (fijalo a un tag) | El numero de version del paquete |
| Leer y depurar el codigo | Directo: esta en tu repo | Con SourceLink y los simbolos (`.snupkg`) |
| Clonar tu repo | Pide `--recursive` o `git submodule update --init` | Nada extra |
| Tu `Directory.Build.props` y la gestion central de paquetes | **Hay que excluir a Common** o no compila (ver la guia 01) | No aplica |
| Versiones de las dependencias de Common | Las exactas de sus `.csproj` | Minimas: NuGet puede subirlas si otro paquete tuyo pide una mayor |
| Probar un cambio de Common antes de publicarlo | Si, en tu propio repo | No, hasta que se publique |

**La unica regla dura:** no mezcles los dos caminos en una misma solucion. Tendrias dos copias de los
mismos namespaces y el compilador no sabria cual usar.

**Cual elegir:** si vas a leer o depurar mucho dentro de `Common`, o si necesitas probar cambios
antes de que se publiquen, el submodulo. Si solo la vas a usar, NuGet: es mas simple y no toca tu
configuracion de compilacion.

## Referencia

- [`../srs.md`](../srs.md): que tiene que hacer la libreria, requisito por requisito, y como se verifica.
- [`../adr/`](../adr/README.md): por que es como es.
- [`../../CHANGELOG.md`](../../CHANGELOG.md): que cambio en cada version y como migrar.
