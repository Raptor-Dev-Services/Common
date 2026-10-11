using Common.Results;

namespace Common.Workflow.Alerts
{
    /// <summary>
    /// Responde "toca avisar hoy de esto?". Calculo puro: el ledger entra como dato y <c>today</c> es la
    /// fecha <b>local</b> de la sucursal, que el producto obtiene con su reloj (C04 no convierte zonas).
    /// </summary>
    public static class AlertEvaluator
    {
        /// <summary>
        /// Valida una politica de aviso.
        /// </summary>
        /// <param name="policy">Politica a validar.</param>
        /// <returns>
        /// La misma politica, o un <see cref="DefinitionFailure{T}"/> con codigos <c>alerts.invalid_lead</c>,
        /// <c>alerts.invalid_repeat</c> o <c>alerts.invalid_stop</c>.
        /// </returns>
        /// <exception cref="ArgumentNullException">Si <paramref name="policy"/> es nula.</exception>
        public static Result<AlertPolicy> ValidatePolicy(AlertPolicy policy)
        {
            ArgumentNullException.ThrowIfNull(policy);
            var errors = PolicyErrors(policy);
            return errors.Count == 0 ? Result.OK(policy) : new DefinitionFailure<AlertPolicy>(errors);
        }

        /// <summary>
        /// Decide si toca avisar del sujeto en <paramref name="today"/>.
        /// </summary>
        /// <param name="subject">Sujeto a evaluar.</param>
        /// <param name="policy">Politica de aviso; debe ser valida (<see cref="ValidatePolicy"/>).</param>
        /// <param name="lastForCycle">Entrada del ledger para la clave de <see cref="KeyFor"/>; nula si no hay.</param>
        /// <param name="today">Fecha local de la sucursal.</param>
        /// <exception cref="ArgumentException">Si el sujeto o la politica son invalidos, o la entrada no es del ciclo.</exception>
        public static AlertDecision Evaluate(AlertSubject subject, AlertPolicy policy,
            AlertLedgerEntry? lastForCycle, DateOnly today)
        {
            var key = KeyFor(subject, policy, today);
            if (lastForCycle is not null && lastForCycle.Key != key)
                throw new ArgumentException($"La entrada del ledger es de otro ciclo ({lastForCycle.Key}); se esperaba {key}.", nameof(lastForCycle));

            return subject switch
            {
                ThresholdAlertSubject t => EvaluateThreshold(t, policy, key, lastForCycle, today),
                DateAlertSubject d => EvaluateDated(d.TargetDate, d.ValidUntil, policy, key, lastForCycle, today),
                PeriodicAlertSubject p => EvaluatePeriodic(p, policy, key, lastForCycle, today),
                _ => throw new ArgumentException($"Sujeto no soportado: {subject.GetType().Name}.", nameof(subject)),
            };
        }

        /// <summary>
        /// Clave del ledger que corresponde al sujeto en <paramref name="today"/>: la fecha del ciclo para
        /// sujetos de fecha, la ultima ocurrencia cuya ventana ya abrio para periodicos (o la primera si
        /// ninguna abrio) y <c>below</c> para umbrales.
        /// </summary>
        /// <param name="subject">Sujeto.</param>
        /// <param name="policy">Politica de aviso.</param>
        /// <param name="today">Fecha local de la sucursal.</param>
        /// <exception cref="ArgumentException">Si el sujeto o la politica son invalidos.</exception>
        public static AlertKey KeyFor(AlertSubject subject, AlertPolicy policy, DateOnly today)
        {
            ValidateArguments(subject, policy);
            var cycle = subject switch
            {
                ThresholdAlertSubject => AlertKey.BelowCycle,
                DateAlertSubject d => AlertKey.CycleOf(d.CycleDate ?? d.TargetDate),
                PeriodicAlertSubject p => PeriodicCycle(p, LastOpenOccurrence(p, policy, today) ?? p.FirstOccurrence),
                _ => throw new ArgumentException($"Sujeto no soportado: {subject.GetType().Name}.", nameof(subject)),
            };
            return new AlertKey(subject.ItemType, subject.ItemId, subject.Kind, cycle);
        }

        private static AlertDecision EvaluateThreshold(ThresholdAlertSubject t, AlertPolicy policy, AlertKey key,
            AlertLedgerEntry? last, DateOnly today)
        {
            if (t.Threshold < 0)
                return new(key, AlertAction.None, AlertReasons.Disabled, null);

            if (t.CurrentValue > t.Threshold)
                return last is null
                    ? new(key, AlertAction.None, AlertReasons.NotBelow, null)
                    : new(key, AlertAction.Clear, AlertReasons.NotBelow, null);

            return Repeatable(key, policy, last, today, null);
        }

        private static AlertDecision EvaluateDated(DateOnly target, DateOnly? validUntil, AlertPolicy policy,
            AlertKey key, AlertLedgerEntry? last, DateOnly today)
        {
            var opens = target.AddDays(-policy.LeadDays);

            if (policy.StopAfterDays is { } stop && today > target.AddDays(stop))
                return new(key, AlertAction.None, AlertReasons.Stale, opens);

            if (validUntil is { } until && today >= until)
                return new(key, AlertAction.None, AlertReasons.OutOfValidity, opens);

            if (today < opens)
                return new(key, AlertAction.None, AlertReasons.NotYet, opens);

            return Repeatable(key, policy, last, today, opens);
        }

        private static AlertDecision EvaluatePeriodic(PeriodicAlertSubject p, AlertPolicy policy, AlertKey key,
            AlertLedgerEntry? last, DateOnly today)
        {
            var occurrence = LastOpenOccurrence(p, policy, today);
            if (occurrence is null)
            {
                var opens = p.FirstOccurrence.AddDays(-policy.LeadDays);
                return p.Until is { } until && p.FirstOccurrence > until
                    ? new(key, AlertAction.None, AlertReasons.OutOfValidity, opens)
                    : new(key, AlertAction.None, AlertReasons.NotYet, opens);
            }

            return EvaluateDated(occurrence.Value, null, policy, key, last, today);
        }

        // Ya se aviso en el ciclo: solo repite si la politica lo pide y ya pasaron los dias.
        private static AlertDecision Repeatable(AlertKey key, AlertPolicy policy, AlertLedgerEntry? last,
            DateOnly today, DateOnly? opens)
        {
            if (last is null)
                return new(key, AlertAction.Notify, AlertReasons.Due, opens);

            return policy.RepeatEveryDays is { } every && last.LastSentOn.AddDays(every) <= today
                ? new(key, AlertAction.Notify, AlertReasons.Repeat, opens)
                : new(key, AlertAction.None, AlertReasons.AlreadySent, opens);
        }

        // Ultima ocurrencia O(k) = F + k*P meses con O(k) - LeadDays <= today y O(k) <= Until; null si no hay.
        // Cada ocurrencia se calcula desde F: 31-ene + 1 mes = 28-feb y + 2 meses = 31-mar.
        private static DateOnly? LastOpenOccurrence(PeriodicAlertSubject p, AlertPolicy policy, DateOnly today)
        {
            var limit = today.AddDays(policy.LeadDays);
            if (p.Until is { } until && until < limit) limit = until;

            DateOnly Occurrence(int k) => p.FirstOccurrence.AddMonths(k * p.PeriodMonths);

            if (Occurrence(0) > limit) return null;

            // k = meses de calendario / periodo nunca se queda corto (O(k + 1) cae en un mes posterior al
            // limite); a lo mas se pasa por uno cuando el dia de F es mayor que el del limite.
            var months = (limit.Year * 12 + limit.Month) - (p.FirstOccurrence.Year * 12 + p.FirstOccurrence.Month);
            var k = months / p.PeriodMonths;
            while (k > 0 && Occurrence(k) > limit) k--;
            return Occurrence(k);
        }

        private static string PeriodicCycle(PeriodicAlertSubject p, DateOnly occurrence)
            => p.CyclePrefix is null ? AlertKey.CycleOf(occurrence) : $"{p.CyclePrefix}:{AlertKey.CycleOf(occurrence)}";

        private static void ValidateArguments(AlertSubject subject, AlertPolicy policy)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(policy);
            ArgumentException.ThrowIfNullOrWhiteSpace(subject.ItemType, nameof(subject));
            ArgumentException.ThrowIfNullOrWhiteSpace(subject.ItemId, nameof(subject));

            var errors = PolicyErrors(policy);
            if (errors.Count > 0)
                throw new ArgumentException(new DefinitionFailure<AlertPolicy>(errors).Message, nameof(policy));

            switch (subject)
            {
                case DateAlertSubject d when d.Kind is not (AlertKind.End or AlertKind.Notice):
                    throw new ArgumentException($"Un sujeto de fecha es End o Notice, no {d.Kind}.", nameof(subject));
                case PeriodicAlertSubject p when p.PeriodMonths < 1:
                    throw new ArgumentException("El periodo debe ser de al menos un mes.", nameof(subject));
                case PeriodicAlertSubject p when p.Kind != AlertKind.Periodicity:
                    throw new ArgumentException("Un sujeto periodico es de tipo Periodicity.", nameof(subject));
                case ThresholdAlertSubject t when t.Kind != AlertKind.Threshold:
                    throw new ArgumentException("Un sujeto de umbral es de tipo Threshold.", nameof(subject));
            }
        }

        private static List<DefinitionError> PolicyErrors(AlertPolicy policy)
        {
            var errors = new List<DefinitionError>();
            if (policy.LeadDays < 0)
                errors.Add(new("alerts.invalid_lead", "LeadDays", "Los dias de anticipacion no pueden ser negativos."));
            if (policy.RepeatEveryDays is < 1)
                errors.Add(new("alerts.invalid_repeat", "RepeatEveryDays", "La repeticion debe ser de al menos un dia."));
            if (policy.StopAfterDays is < 0)
                errors.Add(new("alerts.invalid_stop", "StopAfterDays", "Los dias para dejar de avisar no pueden ser negativos."));
            return errors;
        }
    }
}
