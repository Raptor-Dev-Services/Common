using System.Globalization;
using Common.Results;
using Common.Workflow.Calendars;

namespace Common.Workflow.Tests.Calendars;

/// <summary>
/// Calendarios y utilidades de los vectores de §12 del SRS CMN-C01.
/// </summary>
internal static class Calendarios
{
    public const string Mexico = "America/Mexico_City";
    public const string Chicago = "America/Chicago";

    public static readonly Holiday Feriado16Nov = new(new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 16), false, "Revolucion");

    private static readonly DayOfWeek[] LunesAViernes =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    public static IEnumerable<WorkSegment> LaV(int start, int end) =>
        LunesAViernes.Select(d => new WorkSegment(d, start, end));

    /// <summary>Calendario A: L-V 09:00-18:00, feriado 2026-11-16, Mexico.</summary>
    public static BusinessCalendar A(params Holiday[] extra) =>
        Build(Mexico, LaV(540, 1080), [Feriado16Nov, .. extra]);

    /// <summary>Calendario B: L-V 09:00-14:00 y 16:00-19:00, feriado 2026-11-16, Mexico.</summary>
    public static BusinessCalendar B() =>
        Build(Mexico, LaV(540, 840).Concat(LaV(960, 1140)), [Feriado16Nov]);

    /// <summary>Calendario A con turno nocturno sabado 22:00-24:00 + domingo 00:00-06:00.</summary>
    public static BusinessCalendar Nocturno() =>
        Build(Mexico, LaV(540, 1080).Concat([new WorkSegment(DayOfWeek.Saturday, 1320, 1440), new WorkSegment(DayOfWeek.Sunday, 0, 360)]), [Feriado16Nov]);

    /// <summary>Chicago: domingo 00:00-03:00 (para el cambio de horario) y L-V 09:00-18:00.</summary>
    public static BusinessCalendar ChicagoDomingo() =>
        Build(Chicago, LaV(540, 1080).Append(new WorkSegment(DayOfWeek.Sunday, 0, 180)), []);

    public static BusinessCalendar Build(string zone, IEnumerable<WorkSegment> segments, IEnumerable<Holiday> holidays)
    {
        var result = BusinessCalendar.Create(new BusinessCalendarDefinition(zone, [.. segments], [.. holidays]));
        return Assert.IsType<SuccessResult<BusinessCalendar>>(result).Data;
    }

    /// <summary>Instante UTC escrito en ISO con Z.</summary>
    public static DateTime Utc(string iso) =>
        DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    /// <summary>Hora local de Mexico (UTC-6 fijo desde 2022) como instante UTC.</summary>
    public static DateTime Mx(string local) =>
        DateTime.SpecifyKind(DateTime.Parse(local, CultureInfo.InvariantCulture).AddHours(6), DateTimeKind.Utc);
}
