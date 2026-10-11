using Common.Workflow.Calendars;
using static Common.Workflow.Tests.Calendars.Calendarios;

namespace Common.Workflow.Tests.Calendars;

/// <summary>REQ-CMN-C01-003 y 004; V13 y V14.</summary>
public class HoraDeParedTests
{
    [Theory]
    [InlineData("2026-11-01", 4)] // V13: el reloj se atrasa, 01:00-02:00 ocurre dos veces
    [InlineData("2026-03-08", 2)] // V13: el reloj se adelanta, 02:00-03:00 no existe
    [InlineData("2026-03-15", 3)]
    public void Chicago_domingo_de_00_a_03_dura_lo_que_dura_en_tiempo_real(string date, int hours)
    {
        Assert.Equal(TimeSpan.FromHours(hours), ChicagoDomingo().WorkingTimeOn(DateOnly.Parse(date)));
    }

    [Fact]
    public void Un_fin_ambiguo_toma_la_ultima_ocurrencia_como_mide_Zeit()
    {
        // Medicion de Zeit (BranchClock): Chicago 2026-11-01, 00:00-01:30 dura 2.5 h reales, no 1.5.
        var calendar = Build(Chicago, [new WorkSegment(DayOfWeek.Sunday, 0, 90)], []);

        Assert.Equal(TimeSpan.FromHours(2.5), calendar.WorkingTimeOn(new DateOnly(2026, 11, 1)));
    }

    [Fact]
    public void Chicago_cambio_de_horario_en_instantes_UTC()
    {
        var calendar = ChicagoDomingo();

        Assert.Equal(
            [new UtcInterval(Utc("2026-11-01T05:00:00Z"), Utc("2026-11-01T09:00:00Z"))],
            calendar.WorkingIntervals(Utc("2026-11-01T00:00:00Z"), Utc("2026-11-01T12:00:00Z")));
        Assert.Equal(
            [new UtcInterval(Utc("2026-03-08T06:00:00Z"), Utc("2026-03-08T08:00:00Z"))],
            calendar.WorkingIntervals(Utc("2026-03-08T00:00:00Z"), Utc("2026-03-08T12:00:00Z")));
    }

    [Fact]
    public void Hora_en_el_hueco_se_adelanta_al_primer_minuto_existente()
    {
        // Chicago 2026-03-08: las 02:30 no existen; un tramo que abre ahi abre a las 03:00 CDT (08:00Z).
        var calendar = Build(Chicago, [new WorkSegment(DayOfWeek.Sunday, 150, 300)], []);

        Assert.Equal(
            [new UtcInterval(Utc("2026-03-08T08:00:00Z"), Utc("2026-03-08T10:00:00Z"))],
            calendar.WorkingIntervals(Utc("2026-03-08T00:00:00Z"), Utc("2026-03-09T00:00:00Z")));
    }

    [Fact]
    public void Un_tramo_entero_dentro_del_hueco_no_existe_ese_dia()
    {
        var calendar = Build(Chicago, [new WorkSegment(DayOfWeek.Sunday, 120, 150), new WorkSegment(DayOfWeek.Sunday, 600, 660)], []);

        Assert.Equal(TimeSpan.FromHours(1), calendar.WorkingTimeOn(new DateOnly(2026, 3, 8)));
    }

    [Fact]
    public void Tramos_del_mismo_dia_que_se_pisan_por_la_hora_repetida_no_cuentan_dos_veces()
    {
        // 2026-11-01: el fin 01:15 toma la ultima ocurrencia (07:15Z) y el inicio 01:30 la primera
        // (06:30Z). Juntos cubren 05:00Z-08:00Z: 3 h reales, no 3 h 45 min.
        var calendar = Build(Chicago, [new WorkSegment(DayOfWeek.Sunday, 0, 75), new WorkSegment(DayOfWeek.Sunday, 90, 120)], []);

        Assert.Equal(TimeSpan.FromHours(3), calendar.WorkingTimeOn(new DateOnly(2026, 11, 1)));
        Assert.Equal(
            [new UtcInterval(Utc("2026-11-01T05:00:00Z"), Utc("2026-11-01T08:00:00Z"))],
            calendar.WorkingIntervals(Utc("2026-11-01T00:00:00Z"), Utc("2026-11-02T00:00:00Z")));
    }

    [Theory]
    [InlineData("2026-01-15")]
    [InlineData("2026-07-15")]
    [InlineData("2026-11-09")]
    public void Mexico_09_00_local_es_15_00Z_todo_el_anio(string date)
    {
        var day = DateOnly.Parse(date);
        var calendar = Build(Mexico, [new WorkSegment(day.DayOfWeek, 540, 1080)], []);

        Assert.Equal(day.ToDateTime(new TimeOnly(15, 0), DateTimeKind.Utc),
            calendar.NextWorkingMoment(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));
    }

    [Fact]
    public void Instantes_sin_Kind_Utc_son_error_del_programador()
    {
        var calendar = A();
        var local = new DateTime(2026, 11, 9, 9, 0, 0, DateTimeKind.Local);
        var unspecified = new DateTime(2026, 11, 9, 9, 0, 0);
        var utc = Mx("2026-11-09 09:00");

        Assert.Throws<ArgumentException>(() => calendar.IsWorkingTime(local));
        Assert.Throws<ArgumentException>(() => calendar.NextWorkingMoment(unspecified));
        Assert.Throws<ArgumentException>(() => calendar.PreviousWorkingMoment(unspecified));
        Assert.Throws<ArgumentException>(() => calendar.LocalDateOf(unspecified));
        Assert.Throws<ArgumentException>(() => calendar.ActiveTimeBetween(unspecified, utc));
        Assert.Throws<ArgumentException>(() => calendar.ActiveTimeBetween(utc, unspecified));
        Assert.Throws<ArgumentException>(() => calendar.WorkingIntervals(unspecified, utc));
        Assert.Throws<ArgumentException>(() => calendar.WorkingIntervals(utc, unspecified));
        Assert.Throws<ArgumentException>(() => calendar.ComputeEndDate(unspecified, TimeSpan.FromHours(1)));
        Assert.Throws<ArgumentException>(() => calendar.AddMonths(unspecified, 1));
    }

    [Fact]
    public void V14_turno_que_cruza_la_medianoche_es_un_solo_intervalo()
    {
        var calendar = Nocturno();

        Assert.Equal(Mx("2026-11-15 01:00"), calendar.ComputeEndDate(Mx("2026-11-14 23:00"), TimeSpan.FromHours(2)));
        Assert.Equal(Utc("2026-11-15T07:00:00Z"), calendar.ComputeEndDate(Mx("2026-11-14 23:00"), TimeSpan.FromHours(2)));
        Assert.True(calendar.IsWorkingTime(Mx("2026-11-15 00:00")));
        Assert.Equal(TimeSpan.FromHours(2), calendar.ActiveTimeBetween(Mx("2026-11-14 23:00"), Mx("2026-11-15 01:00")));
        Assert.Equal(
            [new UtcInterval(Mx("2026-11-14 22:00"), Mx("2026-11-15 06:00"))],
            calendar.WorkingIntervals(Mx("2026-11-14 12:00"), Mx("2026-11-15 12:00")));
    }

    [Fact]
    public void V14_hacia_atras_el_turno_nocturno_tambien_es_continuo()
    {
        Assert.Equal(Mx("2026-11-14 23:00"), Nocturno().ComputeEndDate(Mx("2026-11-15 01:00"), TimeSpan.FromHours(-2)));
    }
}
