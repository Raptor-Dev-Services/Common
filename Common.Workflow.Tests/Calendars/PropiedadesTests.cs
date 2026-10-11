using Common.Workflow.Calendars;
using static Common.Workflow.Tests.Calendars.Calendarios;

namespace Common.Workflow.Tests.Calendars;

/// <summary>
/// Propiedades de REQ-CMN-C01-006, 008 y 016 con 1 000 casos aleatorios (semilla fija) por calendario.
/// </summary>
public class PropiedadesTests
{
    private const int Casos = 1000;

    public static TheoryData<string> CalendariosDelSrs => ["A", "B", "Nocturno", "Chicago"];

    private static BusinessCalendar Cal(string name) => name switch
    {
        "A" => A(),
        "B" => B(),
        "Nocturno" => Nocturno(),
        _ => ChicagoDomingo(),
    };

    private static DateTime InstanteAleatorio(Random random) =>
        Utc("2026-01-01T00:00:00Z").AddMinutes(random.Next(0, 366 * 24 * 60)).AddSeconds(random.Next(0, 60));

    private static TimeSpan RetrasoAleatorio(Random random) =>
        TimeSpan.FromMinutes(random.Next(1, 9 * 60 * 15)) + TimeSpan.FromSeconds(random.Next(0, 60));

    [Theory]
    [MemberData(nameof(CalendariosDelSrs))]
    public void El_tiempo_habil_hasta_la_fecha_fin_es_el_retraso_y_es_el_primer_instante(string name)
    {
        var calendar = Cal(name);
        var random = new Random(20261111);

        for (var i = 0; i < Casos; i++)
        {
            var t = InstanteAleatorio(random);
            var d = RetrasoAleatorio(random);

            var e = calendar.ComputeEndDate(t, d);

            Assert.Equal(d, calendar.ActiveTimeBetween(t, e));
            Assert.True(calendar.ActiveTimeBetween(t, e.AddTicks(-1)) < d, $"{name}: {t:O} + {d} no es el primer instante");
        }
    }

    [Theory]
    [MemberData(nameof(CalendariosDelSrs))]
    public void El_retraso_negativo_es_el_ultimo_instante_y_deshace_el_positivo(string name)
    {
        var calendar = Cal(name);
        var random = new Random(20261117);

        for (var accepted = 0; accepted < Casos;)
        {
            var t = InstanteAleatorio(random);
            var d = RetrasoAleatorio(random);

            var back = calendar.ComputeEndDate(t, -d);
            Assert.Equal(d, calendar.ActiveTimeBetween(back, t));
            Assert.True(calendar.ActiveTimeBetween(back.AddTicks(1), t) < d, $"{name}: {t:O} - {d} no es el ultimo instante");

            if (!calendar.IsWorkingTime(t)) continue;
            Assert.Equal(t, calendar.ComputeEndDate(calendar.ComputeEndDate(t, d), -d));
            accepted++;
        }
    }

    [Theory]
    [InlineData(Mexico)]
    [InlineData(Chicago)]
    public void AlwaysOpen_suma_tiempo_real(string zone)
    {
        var calendar = BusinessCalendar.AlwaysOpen(zone);
        var random = new Random(24 * 7);

        for (var i = 0; i < Casos; i++)
        {
            var t = InstanteAleatorio(random);
            Assert.Equal(t.AddHours(8), calendar.ComputeEndDate(t, TimeSpan.FromHours(8)));
            Assert.Equal(TimeSpan.FromHours(8), calendar.ActiveTimeBetween(t, t.AddHours(8)));
        }
    }
}
