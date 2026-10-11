using System.Diagnostics;
using Xunit.Abstractions;
using Common.Workflow.Calendars;
using static Common.Workflow.Tests.Calendars.Calendarios;

namespace Common.Workflow.Tests.Calendars;

/// <summary>
/// REQ-CMN-C01-QOS-001 sin BenchmarkDotNet: un orden de magnitud. La meta del SRS es 200 us (p95 en
/// CI); aqui se exige el promedio por debajo de 1 ms para no volver la prueba fragil en maquinas
/// cargadas, y la salida reporta la cifra medida.
/// </summary>
public class RendimientoTests(ITestOutputHelper output)
{
    private const int Iteraciones = 2000;

    private double MicrosegundosPromedio(string nombre, Action accion)
    {
        for (var i = 0; i < 200; i++) accion();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < Iteraciones; i++) accion();
        watch.Stop();
        var us = watch.Elapsed.TotalMicroseconds / Iteraciones;
        output.WriteLine($"{nombre}: {us:F1} us por llamada");
        return us;
    }

    [Fact]
    public void Treinta_dias_habiles_y_un_anio_de_tiempo_habil_tardan_microsegundos()
    {
        var calendar = A(new Holiday(new DateOnly(2026, 12, 24), new DateOnly(2027, 1, 6), true));
        var start = Mx("2026-11-12 10:00");
        DateTime sink = default;
        TimeSpan total = default;

        var dias = MicrosegundosPromedio("30 dias habiles", () => sink = calendar.ComputeEndDate(start, TimeSpan.FromDays(30), mode: EndDateMode.WorkingDays));
        var horas = MicrosegundosPromedio("270 horas habiles", () => sink = calendar.ComputeEndDate(start, TimeSpan.FromHours(30 * 9)));
        var anio = MicrosegundosPromedio("ActiveTimeBetween 1 anio", () => total = calendar.ActiveTimeBetween(start, start.AddYears(1)));

        Assert.NotEqual(default, sink);
        Assert.True(total > TimeSpan.Zero);
        Assert.True(dias < 1000, $"30 dias habiles: {dias:F1} us");
        Assert.True(horas < 1000, $"270 horas habiles: {horas:F1} us");
        Assert.True(anio < 1000, $"1 anio: {anio:F1} us");
    }
}
