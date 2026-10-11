using static Common.Workflow.Tests.Calendars.Calendarios;

namespace Common.Workflow.Tests.Calendars;

/// <summary>REQ-CMN-C01-005 y 006 (V6).</summary>
public class ConsultasTests
{
    [Theory]
    [InlineData("2026-11-13", true)]
    [InlineData("2026-11-14", false)]
    [InlineData("2026-11-16", false)]
    public void Dia_habil_es_no_feriado_y_con_tramos(string date, bool expected)
    {
        Assert.Equal(expected, A().IsWorkingDay(DateOnly.Parse(date)));
    }

    [Theory]
    [InlineData("2026-11-13 09:00", true)]
    [InlineData("2026-11-13 17:59:59", true)]
    [InlineData("2026-11-13 18:00", false)] // semiabierto (GLPI: habil)
    [InlineData("2026-11-13 08:59", false)]
    [InlineData("2026-11-14 10:00", false)]
    public void Hora_habil_es_semiabierta(string local, bool expected)
    {
        Assert.Equal(expected, A().IsWorkingTime(Mx(local)));
    }

    [Theory]
    [InlineData("2026-11-13 10:00", "2026-11-13 10:00")]
    [InlineData("2026-11-13 18:00", "2026-11-17 09:00")]
    [InlineData("2026-11-13 07:00", "2026-11-13 09:00")]
    [InlineData("2026-11-14 12:00", "2026-11-17 09:00")]
    public void Siguiente_momento_habil(string local, string expected)
    {
        Assert.Equal(Mx(expected), A().NextWorkingMoment(Mx(local)));
    }

    [Theory]
    [InlineData("2026-11-13 10:00", "2026-11-13 10:00")]
    [InlineData("2026-11-13 09:00", "2026-11-13 09:00")]
    [InlineData("2026-11-13 18:00", "2026-11-13 18:00")] // fin exacto de un intervalo
    [InlineData("2026-11-13 20:00", "2026-11-13 18:00")]
    [InlineData("2026-11-17 08:00", "2026-11-13 18:00")]
    public void Momento_habil_anterior(string local, string expected)
    {
        Assert.Equal(Mx(expected), A().PreviousWorkingMoment(Mx(local)));
    }

    [Theory]
    [InlineData("A", "2026-11-13 17:00", "2026-11-17 10:00", 120)] // V6
    [InlineData("A", "2026-11-12 15:00", "2026-11-13 12:00", 360)] // V8 (pausa)
    [InlineData("A", "2026-11-13 10:00", "2026-11-13 10:00", 0)]
    [InlineData("A", "2026-11-13 12:00", "2026-11-13 10:00", 0)]
    [InlineData("B", "2026-11-09 08:00", "2026-11-10 08:00", 480)]
    [InlineData("B", "2026-11-09 13:00", "2026-11-09 17:00", 120)]
    public void Tiempo_habil_entre_dos_instantes(string cal, string from, string to, int minutes)
    {
        var calendar = cal == "A" ? A() : B();

        Assert.Equal(TimeSpan.FromMinutes(minutes), calendar.ActiveTimeBetween(Mx(from), Mx(to)));
    }

    [Fact]
    public void Tiempo_habil_de_una_semana_completa_suma_dias_enteros()
    {
        Assert.Equal(TimeSpan.FromHours(36), A().ActiveTimeBetween(Mx("2026-11-15 00:00"), Mx("2026-11-22 00:00")));
    }

    [Fact]
    public void Intervalos_vacios_si_el_rango_esta_invertido()
    {
        Assert.Empty(A().WorkingIntervals(Mx("2026-11-13 12:00"), Mx("2026-11-13 10:00")));
    }

    [Fact]
    public void Intervalos_de_B_salen_recortados_al_rango_y_en_orden()
    {
        Assert.Equal(
            [
                new(Mx("2026-11-09 10:00"), Mx("2026-11-09 14:00")),
                new(Mx("2026-11-09 16:00"), Mx("2026-11-09 19:00")),
                new Common.Workflow.Calendars.UtcInterval(Mx("2026-11-10 09:00"), Mx("2026-11-10 11:00")),
            ],
            B().WorkingIntervals(Mx("2026-11-09 10:00"), Mx("2026-11-10 11:00")));
    }

    [Fact]
    public void Fecha_local_de_un_instante()
    {
        Assert.Equal(new DateOnly(2026, 11, 9), A().LocalDateOf(Utc("2026-11-10T05:59:00Z")));
    }

    [Fact]
    public void Duracion_de_un_intervalo()
    {
        Assert.Equal(TimeSpan.FromHours(9), new Common.Workflow.Calendars.UtcInterval(Mx("2026-11-09 09:00"), Mx("2026-11-09 18:00")).Duration);
    }
}
