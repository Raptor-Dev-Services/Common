# 10 - Motores de calculo: `Common.Workflow`

`Common.Workflow` trae **mecanismos de calculo** para mesa de ayuda, SLA, activos y contratos
([ADR-0009](../adr/0009-motores-de-calculo-en-common-workflow.md)). Tu producto pone el *contenido*
(que estados, que reglas, que tasas) y la persistencia; la libreria pone el calculo, probado con los
mismos vectores para todos.

## Referenciarlo

**No viene en la facade.** Se referencia explicito, desde Domain o Application:

```xml
<!-- submodulo (guia 01) -->
<ProjectReference Include="..\..\Common\Common.Workflow\Common.Workflow.csproj" />
<!-- NuGet (guia 02) -->
<PackageReference Include="Raptor.Common.Workflow" Version="x.y.z" />
```

Solo depende de `Common.Contracts`: sirve tambien a productos que usan MediatR en vez del mediator de
Common.

## Las siete piezas

| Namespace | Pieza | Para que |
|---|---|---|
| `Calendars` | `BusinessCalendar`, `ServiceClock` | Horas/dias habiles, feriados, vencimientos de SLA con pausa y escalamiento |
| `States` | `StateMachine<TState,TContext>` | Transiciones con matriz por rol (deniega por omision), motivo, guardas y ganchos |
| `Rules` | `RuleCollection` | Reglas tipo "si categoria = X y texto contiene Y, asigna a Z", con `Explain` |
| `Alerts` | `AlertEvaluator`, `IAlertLedger`, `ContractSchedule` | Si toca avisar de un vencimiento, sin avisar dos veces |
| `Folios` | `FolioFormat`, `IFolioGenerator` | Folios legibles `T-000123` por ambito |
| `Forms` | `FormConditionEngine` | Preguntas visibles, validacion y destinos condicionales |
| `Depreciation` | `DepreciationCalculator`, `Warranty`, `Tco` | Depreciacion, valor en libros, garantia y TCO |

## Las mismas reglas en las siete

1. **Definicion -> compilacion -> evaluacion.** La definicion es un record serializable con
   `System.Text.Json` (enums como texto): guardala como quieras. Compilala **una vez** (`Create` /
   `Compile`) y registra el resultado como singleton: es inmutable y seguro entre hilos.
2. **Una definicion invalida no lanza:** devuelve `DefinitionFailure<T>`, un `IValidationFailure` con
   todos los errores (`Code`, `Path`, `Message`). Los codigos son contrato estable.
3. **Nunca lee el reloj.** El "ahora" entra como `nowUtc` o `today`. Los instantes van en UTC
   (`DateTimeKind.Utc`, si no, `ArgumentException`); las fechas de negocio, como `DateOnly` local.
4. **Los puertos los implementas tu** (`IAlertLedger`, `IFolioGenerator`) contra tu base. La libreria
   trae `InMemoryAlertLedger` e `InMemoryFolioGenerator` para tus pruebas.

## Ejemplo: vencimiento de un SLA de 9 horas habiles

```csharp
using Common.Results;
using Common.Workflow;
using Common.Workflow.Calendars;

var semana = Enumerable.Range(1, 5)                     // lunes a viernes, 09:00-18:00
    .Select(d => new WorkSegment((DayOfWeek)d, 9 * 60, 18 * 60))
    .ToList();
var definicion = new BusinessCalendarDefinition(
    "America/Mexico_City", semana,
    [new Holiday(new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 16), IsPerpetual: false)]);

var creado = BusinessCalendar.Create(definicion);
if (creado is DefinitionFailure<BusinessCalendar> fallo)
    throw new InvalidOperationException(fallo.Message);   // o devuelvelo como 400
var calendario = ((ISuccess<BusinessCalendar>)creado).Data;

// Ticket abierto el viernes 13-nov-2026 a las 17:00 (23:00 UTC); el lunes 16 es feriado.
var vence = ServiceClock.ComputeDue(calendario, new ServiceTarget(9, DurationUnit.Hour),
    new DateTime(2026, 11, 13, 23, 0, 0, DateTimeKind.Utc), accumulatedPause: TimeSpan.Zero);
// -> martes 17-nov-2026 17:00 local (23:00 UTC)
```

El ejemplo esta como prueba en `Common.Workflow.Tests/GuiaWorkflowTests.cs`, asi que no se desfasa.

## Donde esta el detalle

Cada motor tiene su SRS con requisitos y vectores en el repo de adopcion
`Raptor-Dev-Services/glpi-ideas` (`02-comun/SRS-CMN-C01` a `C07`). Las pruebas de
`Common.Workflow.Tests/<Namespace>/` usan esos mismos vectores: son la mejor documentacion de los bordes.
