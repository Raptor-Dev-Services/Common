using System.Text.Json;
using Common.Results;
using Common.Workflow.Calendars;
using static Common.Workflow.Tests.Calendars.Calendarios;

namespace Common.Workflow.Tests.Calendars;

/// <summary>REQ-CMN-C01-001, 016 y QOS-004.</summary>
public class BusinessCalendarCreateTests
{
    private static DefinitionFailure<BusinessCalendar> Falla(BusinessCalendarDefinition definition) =>
        Assert.IsType<DefinitionFailure<BusinessCalendar>>(BusinessCalendar.Create(definition));

    [Fact]
    public void Una_definicion_valida_compila_y_conserva_la_zona()
    {
        var calendar = A();

        Assert.Equal(Mexico, calendar.TimeZone.Id);
        Assert.Equal(Mexico, calendar.Definition.TimeZoneId);
        Assert.Equal(5, calendar.Definition.Segments.Count);
    }

    [Fact]
    public void Devuelve_todos_los_errores_cada_uno_con_su_ruta()
    {
        var failure = Falla(new BusinessCalendarDefinition(
            "America/Nowhere",
            [
                new WorkSegment(DayOfWeek.Monday, 540, 1080),
                new WorkSegment(DayOfWeek.Tuesday, 540, 1080),
                new WorkSegment(DayOfWeek.Monday, 600, 700),
            ],
            [new Holiday(new DateOnly(2026, 12, 2), new DateOnly(2026, 12, 1), false)]));

        Assert.IsAssignableFrom<IValidationFailure>(failure);
        Assert.Equal(3, failure.Errors.Count);
        Assert.Contains(failure.Errors, e => e is { Code: "calendar.timezone_unknown", Path: "TimeZoneId" });
        Assert.Contains(failure.Errors, e => e is { Code: "calendar.segment_overlap", Path: "Segments[2]" });
        Assert.Contains(failure.Errors, e => e is { Code: "calendar.holiday_range", Path: "Holidays[0]" });
    }

    [Theory]
    [InlineData(-1, 600)]
    [InlineData(540, 1441)]
    [InlineData(600, 600)]
    [InlineData(700, 600)]
    public void Un_tramo_fuera_de_rango_es_segment_range(int start, int end)
    {
        var failure = Falla(new BusinessCalendarDefinition(Mexico,
            [new WorkSegment(DayOfWeek.Monday, 540, 600), new WorkSegment(DayOfWeek.Monday, start, end)], []));

        var error = Assert.Single(failure.Errors);
        Assert.Equal(("calendar.segment_range", "Segments[1]"), (error.Code, error.Path));
    }

    [Fact]
    public void Un_tramo_nulo_o_con_dia_invalido_es_segment_range()
    {
        var failure = Falla(new BusinessCalendarDefinition(Mexico,
            [null!, new WorkSegment((DayOfWeek)9, 540, 600)], [null!]));

        Assert.Equal(
            ["calendar.segment_range", "calendar.segment_range", "calendar.holiday_range"],
            failure.Errors.Select(e => e.Code));
    }

    [Fact]
    public void Sin_tramos_es_no_working_time_y_la_zona_vacia_es_desconocida()
    {
        var failure = Falla(new BusinessCalendarDefinition(" ", null!, null!));

        Assert.Equal(["calendar.timezone_unknown", "calendar.no_working_time"], failure.Errors.Select(e => e.Code));
        Assert.Equal("Segments", failure.Errors[1].Path);
    }

    [Fact]
    public void Tramos_contiguos_son_validos_y_se_funden()
    {
        var partido = Build(Mexico, [new WorkSegment(DayOfWeek.Monday, 840, 1080), new WorkSegment(DayOfWeek.Monday, 540, 840)], []);

        Assert.Equal(TimeSpan.FromHours(9), partido.WorkingTimeOn(new DateOnly(2026, 11, 9)));
        Assert.Equal(
            [new UtcInterval(Mx("2026-11-09 09:00"), Mx("2026-11-09 18:00"))],
            partido.WorkingIntervals(Mx("2026-11-09 00:00"), Mx("2026-11-10 00:00")));
    }

    [Fact]
    public void Un_perpetuo_con_begin_mayor_que_end_es_valido()
    {
        var calendar = A(new Holiday(new DateOnly(2026, 12, 24), new DateOnly(2027, 1, 6), true));

        Assert.Equal(2, calendar.Definition.Holidays.Count);
    }

    [Fact]
    public void Definicion_nula_es_error_del_programador()
    {
        Assert.Throws<ArgumentNullException>(() => BusinessCalendar.Create(null!));
    }

    [Fact]
    public void La_definicion_guardada_es_una_copia_defensiva()
    {
        var segments = new List<WorkSegment> { new(DayOfWeek.Monday, 540, 1080) };
        var calendar = Build(Mexico, segments, []);

        segments.Add(new WorkSegment(DayOfWeek.Tuesday, 540, 1080));

        Assert.Single(calendar.Definition.Segments);
        Assert.False(calendar.IsWorkingDay(new DateOnly(2026, 11, 10)));
    }

    [Fact]
    public void AlwaysOpen_abre_24x7_sin_feriados()
    {
        var calendar = BusinessCalendar.AlwaysOpen(Mexico);

        Assert.Equal(7, calendar.Definition.Segments.Count);
        Assert.Empty(calendar.Definition.Holidays);
        Assert.Equal(TimeSpan.FromHours(24), calendar.WorkingTimeOn(new DateOnly(2026, 11, 15)));
    }

    [Fact]
    public void AlwaysOpen_con_zona_desconocida_es_error_del_programador()
    {
        var ex = Assert.Throws<ArgumentException>(() => BusinessCalendar.AlwaysOpen("Mars/Olympus"));
        Assert.Contains("calendar.timezone_unknown", ex.Message);
    }

    [Fact]
    public void Definicion_y_meta_se_serializan_con_enums_como_texto()
    {
        var definition = new BusinessCalendarDefinition(Mexico,
            [new WorkSegment(DayOfWeek.Monday, 540, 1080)],
            [new Holiday(new DateOnly(2026, 12, 24), new DateOnly(2027, 1, 6), true, "Fiestas")]);
        var target = new ServiceTarget(8, DurationUnit.Hour);

        var json = JsonSerializer.Serialize(definition);
        var targetJson = JsonSerializer.Serialize(target);
        var back = JsonSerializer.Deserialize<BusinessCalendarDefinition>(json)!;

        Assert.Contains("\"Monday\"", json);
        Assert.Contains("\"Hour\"", targetJson);
        Assert.Equal(target, JsonSerializer.Deserialize<ServiceTarget>(targetJson));
        Assert.Equal(definition.Segments, back.Segments);
        Assert.Equal(definition.Holidays, back.Holidays);
        Assert.Contains("\"WorkingDays\"", JsonSerializer.Serialize(EndDateMode.WorkingDays));
        Assert.IsType<SuccessResult<BusinessCalendar>>(BusinessCalendar.Create(back));
    }

    [Fact]
    public void Una_zona_sin_reglas_de_ajuste_usa_desplazamiento_fijo()
    {
        var utc = Build("Etc/UTC", LaV(540, 1080), []);

        Assert.Equal(Utc("2026-11-09T18:00:00Z"), utc.ComputeEndDate(Utc("2026-11-09T09:00:00Z"), TimeSpan.FromHours(9)));
        Assert.Equal(TimeSpan.FromHours(45), utc.ActiveTimeBetween(Utc("2026-11-08T00:00:00Z"), Utc("2026-11-15T00:00:00Z")));
    }
}
