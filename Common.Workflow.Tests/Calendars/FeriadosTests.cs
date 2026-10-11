using Common.Workflow.Calendars;
using static Common.Workflow.Tests.Calendars.Calendarios;

namespace Common.Workflow.Tests.Calendars;

/// <summary>REQ-CMN-C01-002 y V12.</summary>
public class FeriadosTests
{
    private static readonly Holiday FiestasDeFinDeAnio = new(new DateOnly(2026, 12, 24), new DateOnly(2027, 1, 6), true);

    [Theory]
    [InlineData("2026-11-16", true)]
    [InlineData("2027-11-16", false)]
    [InlineData("2026-11-17", false)]
    public void Feriado_con_rango_solo_aplica_a_sus_fechas(string date, bool expected)
    {
        Assert.Equal(expected, A().IsHoliday(DateOnly.Parse(date)));
    }

    [Theory]
    [InlineData("2026-12-31", true)]
    [InlineData("2027-01-02", true)] // V12 (GLPI: false)
    [InlineData("2027-01-07", false)]
    [InlineData("2026-12-23", false)]
    [InlineData("2030-12-24", true)]
    public void Perpetuo_que_cruza_el_anio_coincide_en_ambos_lados(string date, bool expected)
    {
        Assert.Equal(expected, A(FiestasDeFinDeAnio).IsHoliday(DateOnly.Parse(date)));
    }

    [Theory]
    [InlineData("2028-02-29", true)]
    [InlineData("2027-02-28", false)]
    [InlineData("2027-03-01", false)]
    public void Perpetuo_29_de_febrero_solo_coincide_en_bisiestos(string date, bool expected)
    {
        var calendar = A(new Holiday(new DateOnly(2024, 2, 29), new DateOnly(2024, 2, 29), true));

        Assert.Equal(expected, calendar.IsHoliday(DateOnly.Parse(date)));
    }

    [Fact]
    public void Perpetuo_dentro_del_mismo_anio()
    {
        var calendar = A(new Holiday(new DateOnly(2000, 9, 15), new DateOnly(2000, 9, 16), true));

        Assert.True(calendar.IsHoliday(new DateOnly(2031, 9, 16)));
        Assert.False(calendar.IsHoliday(new DateOnly(2031, 9, 17)));
    }

    [Fact]
    public void Un_feriado_no_es_dia_habil_aunque_tenga_tramos()
    {
        var calendar = A();

        Assert.False(calendar.IsWorkingDay(new DateOnly(2026, 11, 16)));
        Assert.False(calendar.IsWorkingTime(Mx("2026-11-16 10:00")));
        Assert.Equal(TimeSpan.Zero, calendar.WorkingTimeOn(new DateOnly(2026, 11, 16)));
    }
}
