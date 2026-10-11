using System.Text.Json.Serialization;

namespace Common.Workflow.Alerts
{
    /// <summary>
    /// Tipo de renovacion de un contrato. Conserva la numeracion de GLPI (<c>Contract.renewal</c>).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<RenewalKind>))]
    public enum RenewalKind : short
    {
        /// <summary>Sin renovacion: vence al terminar la duracion.</summary>
        None = 0,

        /// <summary>Tacita: se renueva sola por otra duracion mientras no se de por terminado.</summary>
        Tacit = 1,

        /// <summary>Expresa: hay que renovarla; puede renovarse por <c>Periodicity</c> meses.</summary>
        Express = 2,
    }

    /// <summary>
    /// Alertas habilitadas en un contrato (GLPI: bitmask <c>alert</c>).
    /// </summary>
    [Flags]
    [JsonConverter(typeof(JsonStringEnumConverter<ContractAlertKinds>))]
    public enum ContractAlertKinds
    {
        /// <summary>Ninguna.</summary>
        None = 0,

        /// <summary>Fin del periodo vigente.</summary>
        End = 1,

        /// <summary>Preaviso antes del fin (y de cada ocurrencia periodica).</summary>
        Notice = 2,

        /// <summary>Ocurrencias periodicas.</summary>
        Periodicity = 4,
    }

    /// <summary>
    /// Terminos de un contrato, en meses.
    /// </summary>
    /// <param name="Begin">Inicio del contrato (fecha local).</param>
    /// <param name="DurationMonths">Duracion inicial; 0 = sin fin.</param>
    /// <param name="NoticeMonths">Meses de preaviso antes del fin; 0 = sin preaviso.</param>
    /// <param name="PeriodicityMonths">Periodicidad; 0 = sin periodicidad.</param>
    /// <param name="Renewal">Tipo de renovacion.</param>
    /// <param name="TerminatedOn">Fecha en que se dio por terminado; acota el periodo vigente.</param>
    public sealed record ContractTerms(DateOnly Begin, int DurationMonths, int NoticeMonths,
        int PeriodicityMonths, RenewalKind Renewal, DateOnly? TerminatedOn = null);

    /// <summary>
    /// Fechas calculadas de un contrato a un dia dado.
    /// </summary>
    /// <param name="InitialEnd">Fin de la duracion inicial; nulo si el contrato no tiene fin.</param>
    /// <param name="CurrentPeriodEnd">Fin del periodo vigente; nulo si no tiene fin.</param>
    /// <param name="NoticeDate">Fecha de preaviso del periodo vigente; nula sin preaviso.</param>
    /// <param name="RenewableUntil">Expresa: hasta cuando puede renovarse (la fecha "efectiva" de GLPI).</param>
    /// <param name="IsExpired">Si el contrato ya vencio.</param>
    public sealed record ContractDates(DateOnly? InitialEnd, DateOnly? CurrentPeriodEnd,
        DateOnly? NoticeDate, DateOnly? RenewableUntil, bool IsExpired);

    /// <summary>
    /// Fechas de contrato de GLPI (fin, preaviso, periodicidad, renovacion tacita y expresa) y los sujetos de
    /// alerta que de ellas salen. Los meses siempre se suman desde el ancla, nunca acumulando.
    /// </summary>
    public static class ContractSchedule
    {
        /// <summary>
        /// Calcula las fechas del contrato en <paramref name="today"/>.
        /// </summary>
        /// <param name="terms">Terminos del contrato.</param>
        /// <param name="today">Fecha local de la sucursal.</param>
        /// <exception cref="ArgumentException">Si los terminos son nulos o tienen meses negativos.</exception>
        public static ContractDates Compute(ContractTerms terms, DateOnly today)
        {
            Validate(terms);

            DateOnly? initialEnd = terms.DurationMonths > 0 ? terms.Begin.AddMonths(terms.DurationMonths) : null;
            DateOnly? currentEnd = initialEnd;

            if (terms.Renewal == RenewalKind.Tacit && initialEnd is not null)
            {
                // Menor Begin + (k + 1) * Duration que sea > today, calculado desde Begin.
                var months = (today.Year * 12 + today.Month) - (terms.Begin.Year * 12 + terms.Begin.Month);
                var k = Math.Max(0, months / terms.DurationMonths - 1);
                while (k > 0 && terms.Begin.AddMonths(k * terms.DurationMonths) > today) k--;
                while (terms.Begin.AddMonths((k + 1) * terms.DurationMonths) <= today) k++;
                currentEnd = terms.Begin.AddMonths((k + 1) * terms.DurationMonths);
            }

            if (terms.TerminatedOn is { } terminated && (currentEnd is null || terminated < currentEnd))
                currentEnd = terminated;

            DateOnly? noticeDate = terms.NoticeMonths > 0 && currentEnd is { } end ? end.AddMonths(-terms.NoticeMonths) : null;

            DateOnly? renewableUntil = terms.Renewal == RenewalKind.Express && initialEnd is { } ie && terms.PeriodicityMonths > 0
                ? ie.AddMonths(terms.PeriodicityMonths)
                : null;

            // GLPI: no esta vencido si es tacito (sin terminar) o si el fin aun no llega.
            var tacitRunning = terms.Renewal == RenewalKind.Tacit && terms.TerminatedOn is null;
            var isExpired = !tacitRunning && currentEnd is { } ce && today >= ce;

            return new ContractDates(initialEnd, currentEnd, noticeDate, renewableUntil, isExpired);
        }

        /// <summary>
        /// Sujetos de alerta del contrato listos para <see cref="AlertEvaluator.Evaluate"/>: fin del periodo
        /// vigente, preaviso (con ciclo = fin del periodo y vigente hasta el fin; no se genera si el contrato
        /// vencio), la serie periodica (desde <c>Begin</c> + duracion inicial) y, con preaviso habilitado, la
        /// serie periodica de preaviso (<c>NoticeMonths</c> antes, ciclo con prefijo <c>notice</c>).
        /// </summary>
        /// <param name="itemType">Tipo de elemento (por ejemplo <c>contract</c>).</param>
        /// <param name="itemId">Id publico del contrato.</param>
        /// <param name="terms">Terminos del contrato.</param>
        /// <param name="enabled">Alertas habilitadas.</param>
        /// <param name="today">Fecha local de la sucursal.</param>
        /// <exception cref="ArgumentException">Si los terminos o los identificadores son invalidos.</exception>
        public static IReadOnlyList<AlertSubject> Subjects(string itemType, string itemId,
            ContractTerms terms, ContractAlertKinds enabled, DateOnly today)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(itemType);
            ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
            var dates = Compute(terms, today);
            var subjects = new List<AlertSubject>();

            if (enabled.HasFlag(ContractAlertKinds.End) && dates.CurrentPeriodEnd is { } end)
                subjects.Add(new DateAlertSubject(itemType, itemId, AlertKind.End, end));

            if (enabled.HasFlag(ContractAlertKinds.Notice) && !dates.IsExpired
                && dates.NoticeDate is { } notice && dates.CurrentPeriodEnd is { } periodEnd)
                subjects.Add(new DateAlertSubject(itemType, itemId, AlertKind.Notice, notice, periodEnd, periodEnd));

            if (enabled.HasFlag(ContractAlertKinds.Periodicity) && terms.PeriodicityMonths > 0)
            {
                var initial = terms.DurationMonths > 0 ? terms.DurationMonths : terms.PeriodicityMonths;
                var first = terms.Begin.AddMonths(initial);
                subjects.Add(new PeriodicAlertSubject(itemType, itemId, first, terms.PeriodicityMonths, terms.TerminatedOn));

                if (enabled.HasFlag(ContractAlertKinds.Notice) && terms.NoticeMonths > 0)
                    subjects.Add(new PeriodicAlertSubject(itemType, itemId, terms.Begin.AddMonths(initial - terms.NoticeMonths),
                        terms.PeriodicityMonths, terms.TerminatedOn, "notice"));
            }

            return subjects;
        }

        private static void Validate(ContractTerms terms)
        {
            ArgumentNullException.ThrowIfNull(terms);
            ArgumentOutOfRangeException.ThrowIfNegative(terms.DurationMonths, nameof(terms));
            ArgumentOutOfRangeException.ThrowIfNegative(terms.NoticeMonths, nameof(terms));
            ArgumentOutOfRangeException.ThrowIfNegative(terms.PeriodicityMonths, nameof(terms));
        }
    }
}
