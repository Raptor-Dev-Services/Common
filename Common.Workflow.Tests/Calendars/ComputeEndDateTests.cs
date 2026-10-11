using System.Diagnostics;
using Common.Workflow.Calendars;
using static Common.Workflow.Tests.Calendars.Calendarios;

namespace Common.Workflow.Tests.Calendars;

/// <summary>REQ-CMN-C01-007 a 012 y 017; V1-V5, V7, V9, V10.</summary>
public class ComputeEndDateTests
{
    private static BusinessCalendar Cal(string name) => name == "B" ? B() : A();

    [Theory]
    [InlineData("V1", "A", "2026-11-13T22:00:00Z", 240, "2026-11-17T17:00:00Z")]
    [InlineData("V2", "A", "2026-11-09T15:00:00Z", 540, "2026-11-10T00:00:00Z")]
    [InlineData("V3", "A", "2026-11-14T16:00:00Z", 60, "2026-11-17T16:00:00Z")]
    [InlineData("V7", "A", "2026-11-17T16:00:00Z", -60, "2026-11-17T15:00:00Z")]
    [InlineData("V10", "B", "2026-11-09T19:00:00Z", 120, "2026-11-09T23:00:00Z")]
    public void Vectores_en_horas_habiles(string vector, string cal, string start, int minutes, string expected)
    {
        Assert.Equal(Utc(expected), Cal(cal).ComputeEndDate(Utc(start), TimeSpan.FromMinutes(minutes)));
        Assert.NotEmpty(vector);
    }

    [Theory]
    [InlineData("V4", "2026-11-13 15:00", 2, "2026-11-18 15:00")]
    [InlineData("V9", "2026-11-19 10:00", -3, "2026-11-13 10:00")]
    [InlineData("fuera de horario", "2026-11-09 19:00", 1, "2026-11-11 09:00")]
    [InlineData("antes de abrir", "2026-11-09 07:00", 1, "2026-11-10 09:00")]
    [InlineData("cero dias", "2026-11-14 10:00", 0, "2026-11-17 09:00")]
    public void Dias_habiles_conservan_la_hora_de_pared(string caso, string start, int days, string expected)
    {
        Assert.Equal(Mx(expected), A().ComputeEndDate(Mx(start), TimeSpan.FromDays(days), mode: EndDateMode.WorkingDays));
        Assert.NotEmpty(caso);
    }

    [Fact]
    public void Dias_habiles_acotan_la_hora_al_horario_del_dia_destino()
    {
        // Viernes con horario corto 09:00-13:00: las 15:00 del jueves llegan como 13:00 del viernes.
        var corto = Build(Mexico, [.. LaV(540, 1080).Where(s => s.Day != DayOfWeek.Friday), new WorkSegment(DayOfWeek.Friday, 540, 780)], []);
        // Lunes con apertura tardia 11:00: las 10:00 del viernes llegan como 11:00 del lunes.
        var tarde = Build(Mexico, [.. LaV(540, 1080).Where(s => s.Day != DayOfWeek.Monday), new WorkSegment(DayOfWeek.Monday, 660, 1080)], []);

        Assert.Equal(Mx("2026-11-13 13:00"), corto.ComputeEndDate(Mx("2026-11-12 15:00"), TimeSpan.FromDays(1), mode: EndDateMode.WorkingDays));
        Assert.Equal(Mx("2026-11-16 11:00"), tarde.ComputeEndDate(Mx("2026-11-13 10:00"), TimeSpan.FromDays(1), mode: EndDateMode.WorkingDays));
    }

    [Theory]
    [InlineData("V5", "2026-11-13 10:00", 1, "2026-11-17 18:00")]
    [InlineData("N=0", "2026-11-13 10:00", 0, "2026-11-13 18:00")]
    [InlineData("N=0 ya cerrado", "2026-11-13 19:00", 0, "2026-11-17 18:00")]
    [InlineData("negativo", "2026-11-17 10:00", -1, "2026-11-13 18:00")]
    public void Fin_de_jornada(string caso, string start, int days, string expected)
    {
        Assert.Equal(Mx(expected), A().ComputeEndDate(Mx(start), TimeSpan.FromDays(days), mode: EndDateMode.EndOfWorkingDay));
        Assert.NotEmpty(caso);
    }

    [Fact]
    public void V5_en_UTC()
    {
        Assert.Equal(Utc("2026-11-18T00:00:00Z"),
            A().ComputeEndDate(Mx("2026-11-13 10:00"), TimeSpan.FromDays(1), mode: EndDateMode.EndOfWorkingDay));
    }

    [Theory]
    [InlineData(EndDateMode.WorkingDays)]
    [InlineData(EndDateMode.EndOfWorkingDay)]
    public void Modos_por_dias_exigen_dias_enteros(EndDateMode mode)
    {
        Assert.Throws<ArgumentException>(() => A().ComputeEndDate(Mx("2026-11-13 10:00"), TimeSpan.FromHours(36), mode: mode));
    }

    [Fact]
    public void Modo_desconocido_es_error_del_programador()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => A().ComputeEndDate(Mx("2026-11-13 10:00"), TimeSpan.FromHours(1), mode: (EndDateMode)7));
    }

    [Theory]
    [InlineData("2026-11-13 10:00", "2026-11-13 10:00")]
    [InlineData("2026-11-13 18:00", "2026-11-17 09:00")]
    [InlineData("2026-11-14 10:00", "2026-11-17 09:00")]
    public void Retraso_cero_es_el_siguiente_momento_habil(string start, string expected)
    {
        Assert.Equal(Mx(expected), A().ComputeEndDate(Mx(start), TimeSpan.Zero));
    }

    [Theory]
    [InlineData("2026-11-09 13:00", -120, "2026-11-09 11:00")]
    [InlineData("2026-11-17 10:00", -120, "2026-11-13 17:00")]
    [InlineData("2026-11-14 10:00", -60, "2026-11-13 17:00")]
    [InlineData("2026-11-10 09:00", -540, "2026-11-09 09:00")]
    public void Retraso_negativo_devuelve_el_ultimo_instante(string start, int minutes, string expected)
    {
        Assert.Equal(Mx(expected), A().ComputeEndDate(Mx(start), TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public void Retraso_negativo_en_B_recorre_los_tramos_al_reves()
    {
        Assert.Equal(Mx("2026-11-09 13:00"), B().ComputeEndDate(Mx("2026-11-09 17:00"), TimeSpan.FromHours(-2)));
    }

    [Fact]
    public void Retraso_adicional_se_suma_despues_en_horas_habiles()
    {
        var calendar = A();
        var start = Mx("2026-11-12 10:00");

        // V8: 8 h -> jueves 18:00, + 6 h -> viernes 15:00.
        Assert.Equal(Utc("2026-11-13T21:00:00Z"), calendar.ComputeEndDate(start, TimeSpan.FromHours(8), TimeSpan.FromHours(6)));
        // En dias: 1 dia -> viernes 10:00, + 9 h -> martes 10:00 (lunes 16 feriado).
        Assert.Equal(Mx("2026-11-17 10:00"),
            calendar.ComputeEndDate(start, TimeSpan.FromDays(1), TimeSpan.FromHours(9), EndDateMode.WorkingDays));
        // Al fin de jornada: viernes 18:00, + 1 h -> martes 10:00.
        Assert.Equal(Mx("2026-11-17 10:00"),
            calendar.ComputeEndDate(start, TimeSpan.FromDays(1), TimeSpan.FromHours(1), EndDateMode.EndOfWorkingDay));
    }

    [Fact]
    public void Retraso_adicional_negativo_es_error_del_programador()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            A().ComputeEndDate(Mx("2026-11-12 10:00"), TimeSpan.FromHours(8), TimeSpan.FromMinutes(-1)));
    }

    [Theory]
    [InlineData("2026-01-30 10:00", 1, false, "2026-03-02 09:00")] // V11: 28-feb es sabado
    [InlineData("2026-01-30 10:00", 1, true, "2026-03-02 18:00")]
    [InlineData("2026-01-30 10:00", 0, true, "2026-01-30 18:00")]
    [InlineData("2026-01-30 10:00", -1, false, "2025-12-30 10:00")]
    [InlineData("2026-01-30 10:00", 2, false, "2026-03-30 10:00")]
    public void Meses_de_calendario(string start, int months, bool endOfWorkingDay, string expected)
    {
        Assert.Equal(Mx(expected), A().AddMonths(Mx(start), months, endOfWorkingDay));
    }

    [Fact]
    public void Meses_se_suman_desde_el_origen_sin_acumular()
    {
        var calendar = BusinessCalendar.AlwaysOpen(Mexico);

        Assert.Equal(Mx("2027-03-31 10:00"), calendar.AddMonths(Mx("2027-01-31 10:00"), 2));
        Assert.Equal(Mx("2027-02-28 10:00"), calendar.AddMonths(Mx("2027-01-31 10:00"), 1));
    }

    [Fact]
    public void AlwaysOpen_conserva_la_hora_de_pared_en_dias_aunque_cambie_el_horario()
    {
        var chicago = BusinessCalendar.AlwaysOpen(Chicago);

        // 2026-10-31 12:00 CDT (17:00Z) + 1 dia = 2026-11-01 12:00 CST (18:00Z): 25 h reales.
        Assert.Equal(Utc("2026-11-01T18:00:00Z"),
            chicago.ComputeEndDate(Utc("2026-10-31T17:00:00Z"), TimeSpan.FromDays(1), mode: EndDateMode.WorkingDays));
        Assert.Equal(TimeSpan.FromHours(25), chicago.WorkingTimeOn(new DateOnly(2026, 11, 1)));
        Assert.Equal(Utc("2026-11-01T13:00:00Z"), chicago.ComputeEndDate(Utc("2026-11-01T05:00:00Z"), TimeSpan.FromHours(8)));
    }

    [Fact]
    public void Calendario_sin_tiempo_habil_por_feriados_se_rinde_rapido()
    {
        var calendar = A(new Holiday(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), true));
        var start = Mx("2026-11-13 10:00");
        var watch = Stopwatch.StartNew();

        var ex = Assert.Throws<InvalidOperationException>(() => calendar.ComputeEndDate(start, TimeSpan.FromHours(1)));

        watch.Stop();
        Assert.True(watch.ElapsedMilliseconds < 50, $"Tardo {watch.ElapsedMilliseconds} ms");
        Assert.Contains(Mexico, ex.Message);
        Assert.Contains("2026-11-13T16:00:00Z", ex.Message);
    }

    [Fact]
    public void Todos_los_recorridos_terminan_sin_tiempo_habil()
    {
        var calendar = A(new Holiday(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), true));
        var start = Mx("2026-11-13 10:00");

        Assert.Throws<InvalidOperationException>(() => calendar.ComputeEndDate(start, TimeSpan.FromHours(-1)));
        Assert.Throws<InvalidOperationException>(() => calendar.PreviousWorkingMoment(start));
        Assert.Throws<InvalidOperationException>(() => calendar.ComputeEndDate(start, TimeSpan.FromDays(1), mode: EndDateMode.WorkingDays));
        Assert.Throws<InvalidOperationException>(() => calendar.AddMonths(start, 1));
        Assert.False(calendar.IsWorkingTime(start));
        Assert.Equal(TimeSpan.Zero, calendar.ActiveTimeBetween(start, start.AddDays(30)));
    }

    [Fact]
    public void Dias_habiles_se_rinden_si_los_dias_destino_no_tienen_tiempo_habil()
    {
        // Habil solo hasta 2026; despues todo es feriado.
        var calendar = A(new Holiday(new DateOnly(2027, 1, 1), new DateOnly(2035, 12, 31), false));
        var start = Mx("2026-12-31 10:00");

        Assert.Throws<InvalidOperationException>(() => calendar.ComputeEndDate(start, TimeSpan.FromDays(1), mode: EndDateMode.WorkingDays));
        Assert.Throws<InvalidOperationException>(() => calendar.ComputeEndDate(Mx("2036-01-01 10:00"), TimeSpan.FromDays(-1), mode: EndDateMode.WorkingDays));
    }
}
