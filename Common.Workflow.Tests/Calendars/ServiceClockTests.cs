using Common.Workflow.Calendars;
using static Common.Workflow.Tests.Calendars.Calendarios;

namespace Common.Workflow.Tests.Calendars;

/// <summary>REQ-CMN-C01-013 a 015; V8, V11 y V15.</summary>
public class ServiceClockTests
{
    private static readonly ServiceTarget Ttr8h = new(8, DurationUnit.Hour);
    private static readonly DateTime JuevesDiez = Mx("2026-11-12 10:00");
    private static readonly DateTime VenceViernes15 = Utc("2026-11-13T21:00:00Z");

    [Fact]
    public void Sin_pausa_vence_el_mismo_jueves_a_las_18()
    {
        Assert.Equal(Mx("2026-11-12 18:00"), ServiceClock.ComputeDue(A(), Ttr8h, JuevesDiez, TimeSpan.Zero));
    }

    [Fact]
    public void V8_la_pausa_se_mide_en_tiempo_habil_y_desplaza_desde_el_inicio_original()
    {
        var calendar = A();

        var increment = ServiceClock.PauseIncrement(calendar, Mx("2026-11-12 15:00"), Mx("2026-11-13 12:00"));
        var due = ServiceClock.ComputeDue(calendar, Ttr8h, JuevesDiez, increment);

        Assert.Equal(TimeSpan.FromHours(6), increment);
        Assert.Equal(VenceViernes15, due);
    }

    [Theory]
    [InlineData(30, DurationUnit.Minute, false, "2026-11-12 10:30")]
    [InlineData(2, DurationUnit.Day, false, "2026-11-17 10:00")]
    [InlineData(1, DurationUnit.Day, true, "2026-11-13 18:00")]
    [InlineData(0, DurationUnit.Hour, false, "2026-11-12 10:00")]
    public void Cada_unidad_usa_su_modo(int amount, DurationUnit unit, bool endOfWorkingDay, string expected)
    {
        Assert.Equal(Mx(expected), ServiceClock.ComputeDue(A(), new ServiceTarget(amount, unit, endOfWorkingDay), JuevesDiez, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(false, "2026-03-02 09:00")] // V11
    [InlineData(true, "2026-03-02 18:00")]
    public void V11_un_mes_es_mes_de_calendario(bool endOfWorkingDay, string expected)
    {
        var due = ServiceClock.ComputeDue(A(), new ServiceTarget(1, DurationUnit.Month, endOfWorkingDay), Mx("2026-01-30 10:00"), TimeSpan.Zero);

        Assert.Equal(Mx(expected), due);
    }

    [Fact]
    public void Un_mes_con_pausa_suma_la_pausa_en_horas_habiles()
    {
        var due = ServiceClock.ComputeDue(A(), new ServiceTarget(1, DurationUnit.Month, true), Mx("2026-01-30 10:00"), TimeSpan.FromHours(2));

        Assert.Equal(Mx("2026-03-03 11:00"), due);
    }

    [Fact]
    public void Metas_invalidas_son_error_del_programador()
    {
        var calendar = A();

        Assert.Throws<ArgumentOutOfRangeException>(() => ServiceClock.ComputeDue(calendar, new ServiceTarget(-1, DurationUnit.Hour), JuevesDiez, TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => ServiceClock.ComputeDue(calendar, new ServiceTarget(8, DurationUnit.Hour, true), JuevesDiez, TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => ServiceClock.ComputeDue(calendar, new ServiceTarget(8, DurationUnit.Minute, true), JuevesDiez, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => ServiceClock.ComputeDue(calendar, new ServiceTarget(8, (DurationUnit)9), JuevesDiez, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => ServiceClock.ComputeDue(calendar, Ttr8h, JuevesDiez, TimeSpan.FromMinutes(-1)));
        Assert.Throws<ArgumentNullException>(() => ServiceClock.ComputeDue(null!, Ttr8h, JuevesDiez, TimeSpan.Zero));
        Assert.Throws<ArgumentNullException>(() => ServiceClock.ComputeDue(calendar, null!, JuevesDiez, TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => ServiceClock.ComputeDue(calendar, Ttr8h, new DateTime(2026, 11, 12, 10, 0, 0), TimeSpan.Zero));
    }

    [Fact]
    public void Vencimiento_fijado_a_mano_se_desplaza_en_horas_habiles()
    {
        var calendar = A();

        Assert.Equal(Mx("2026-11-17 10:00"), ServiceClock.ShiftManualDue(calendar, Mx("2026-11-13 17:00"), TimeSpan.FromHours(2)));
        Assert.Equal(Mx("2026-11-13 18:00"), ServiceClock.ShiftManualDue(calendar, Mx("2026-11-13 18:00"), TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => ServiceClock.ShiftManualDue(calendar, Mx("2026-11-13 18:00"), TimeSpan.FromHours(-1)));
        Assert.Throws<ArgumentException>(() => ServiceClock.ShiftManualDue(calendar, new DateTime(2026, 11, 13), TimeSpan.Zero));
        Assert.Throws<ArgumentNullException>(() => ServiceClock.ShiftManualDue(null!, Mx("2026-11-13 18:00"), TimeSpan.Zero));
    }

    [Theory]
    [InlineData(-1, DurationUnit.Hour, "2026-11-13 14:00")]
    [InlineData(2, DurationUnit.Hour, "2026-11-13 17:00")]
    [InlineData(4, DurationUnit.Hour, "2026-11-17 10:00")] // 3 h viernes, lunes feriado, 1 h martes
    [InlineData(-30, DurationUnit.Minute, "2026-11-13 14:30")]
    [InlineData(0, DurationUnit.Hour, "2026-11-13 15:00")]
    [InlineData(1, DurationUnit.Day, "2026-11-17 15:00")]
    [InlineData(-1, DurationUnit.Month, "2026-10-13 15:00")]
    public void Fecha_de_escalamiento_desde_el_vencimiento_del_viernes(int amount, DurationUnit unit, string expected)
    {
        var at = ServiceClock.ComputeEscalationAt(A(), Ttr8h, JuevesDiez, TimeSpan.FromHours(6), new ServiceOffset(amount, unit));

        Assert.Equal(Mx(expected), at);
    }

    [Fact]
    public void Offset_invalido_es_error_del_programador()
    {
        Assert.Throws<ArgumentNullException>(() => ServiceClock.ComputeEscalationAt(A(), Ttr8h, JuevesDiez, TimeSpan.Zero, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ServiceClock.ComputeEscalationAt(A(), Ttr8h, JuevesDiez, TimeSpan.Zero, new ServiceOffset(1, (DurationUnit)9)));
    }

    [Fact]
    public void V15_retraso()
    {
        var now = Mx("2026-11-13 16:00");

        Assert.True(ServiceClock.IsLate(VenceViernes15, null, now, isPaused: false));
        Assert.False(ServiceClock.IsLate(VenceViernes15, null, now, isPaused: true));
        Assert.False(ServiceClock.IsLate(VenceViernes15, Mx("2026-11-13 14:59"), now, isPaused: false));
        Assert.True(ServiceClock.IsLate(VenceViernes15, Mx("2026-11-13 15:01"), now, isPaused: false));
        Assert.False(ServiceClock.IsLate(VenceViernes15, null, VenceViernes15, isPaused: false));
    }

    [Fact]
    public void Retraso_exige_instantes_UTC()
    {
        var unspecified = new DateTime(2026, 11, 13, 15, 0, 0);

        Assert.Throws<ArgumentException>(() => ServiceClock.IsLate(unspecified, null, VenceViernes15, false));
        Assert.Throws<ArgumentException>(() => ServiceClock.IsLate(VenceViernes15, null, unspecified, false));
        Assert.Throws<ArgumentException>(() => ServiceClock.IsLate(VenceViernes15, unspecified, VenceViernes15, false));
    }

    [Fact]
    public void Tiempo_efectivo_descuenta_la_pausa_sin_piso_de_un_segundo()
    {
        var calendar = A();

        Assert.Equal(TimeSpan.FromHours(8), ServiceClock.ElapsedActive(calendar, JuevesDiez, VenceViernes15, TimeSpan.FromHours(6)));
        Assert.Equal(TimeSpan.Zero, ServiceClock.ElapsedActive(calendar, JuevesDiez, JuevesDiez, TimeSpan.Zero));
        Assert.Equal(TimeSpan.Zero, ServiceClock.ElapsedActive(calendar, JuevesDiez, Mx("2026-11-12 11:00"), TimeSpan.FromHours(2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ServiceClock.ElapsedActive(calendar, JuevesDiez, JuevesDiez, TimeSpan.FromHours(-1)));
        Assert.Throws<ArgumentNullException>(() => ServiceClock.ElapsedActive(null!, JuevesDiez, JuevesDiez, TimeSpan.Zero));
        Assert.Throws<ArgumentNullException>(() => ServiceClock.PauseIncrement(null!, JuevesDiez, JuevesDiez));
    }
}
