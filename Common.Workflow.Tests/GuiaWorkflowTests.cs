using Common.Results;
using Common.Workflow;
using Common.Workflow.Calendars;

namespace Common.Workflow.Tests;

/// <summary>El ejemplo de docs/guia/10-workflow.md, para que la guia no se desfase.</summary>
public class GuiaWorkflowTests
{
    [Fact]
    public void Ejemplo_de_la_guia_salta_fin_de_semana_y_feriado()
    {
        var semana = Enumerable.Range(1, 5)
            .Select(d => new WorkSegment((DayOfWeek)d, 9 * 60, 18 * 60))
            .ToList();
        var definicion = new BusinessCalendarDefinition(
            "America/Mexico_City", semana,
            [new Holiday(new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 16), IsPerpetual: false)]);

        var creado = BusinessCalendar.Create(definicion);
        Assert.IsNotType<DefinitionFailure<BusinessCalendar>>(creado);
        var calendario = ((ISuccess<BusinessCalendar>)creado).Data;

        var vence = ServiceClock.ComputeDue(calendario, new ServiceTarget(9, DurationUnit.Hour),
            new DateTime(2026, 11, 13, 23, 0, 0, DateTimeKind.Utc), accumulatedPause: TimeSpan.Zero);

        Assert.Equal(new DateTime(2026, 11, 17, 23, 0, 0, DateTimeKind.Utc), vence);
    }
}
