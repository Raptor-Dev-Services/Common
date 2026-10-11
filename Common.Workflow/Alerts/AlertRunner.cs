namespace Common.Workflow.Alerts
{
    /// <summary>
    /// Orquesta una corrida de avisos: lee el ledger en lote, evalua cada sujeto, reclama cada aviso y
    /// solo notifica si gano el reclamo. Es la deduplicacion entre corridas y replicas: la lectura previa
    /// solo evita trabajo.
    /// </summary>
    /// <remarks>
    /// El producto ejecuta la corrida dentro de una transaccion que incluye el reclamo y el encolado de la
    /// notificacion (outbox): si encolar falla, el reclamo se revierte y la siguiente corrida reintenta.
    /// Se recomiendan lotes de 500 sujetos por transaccion.
    /// </remarks>
    public static class AlertRunner
    {
        /// <summary>
        /// Ejecuta la corrida.
        /// </summary>
        /// <param name="subjects">Sujetos a evaluar.</param>
        /// <param name="policyOf">Politica de cada sujeto (normalmente por tipo de elemento).</param>
        /// <param name="ledger">Ledger del producto.</param>
        /// <param name="notify">Encola la notificacion; solo se llama para avisos reclamados.</param>
        /// <param name="today">Fecha local de la sucursal.</param>
        /// <param name="nowUtc">Instante de la corrida, en UTC.</param>
        /// <param name="ct">Token de cancelacion.</param>
        /// <returns>Conteos para la bitacora de corridas.</returns>
        /// <exception cref="ArgumentException">Si falta un argumento, <paramref name="nowUtc"/> no es UTC o una politica es invalida.</exception>
        public static async Task<AlertRunSummary> RunAsync(IReadOnlyList<AlertSubject> subjects,
            Func<AlertSubject, AlertPolicy> policyOf, IAlertLedger ledger,
            Func<AlertDecision, AlertSubject, CancellationToken, Task> notify,
            DateOnly today, DateTime nowUtc, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(subjects);
            ArgumentNullException.ThrowIfNull(policyOf);
            ArgumentNullException.ThrowIfNull(ledger);
            ArgumentNullException.ThrowIfNull(notify);
            if (nowUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("nowUtc debe tener Kind = Utc.", nameof(nowUtc));

            var work = new (AlertSubject Subject, AlertPolicy Policy, AlertKey Key)[subjects.Count];
            for (var i = 0; i < subjects.Count; i++)
            {
                var subject = subjects[i];
                var policy = policyOf(subject);
                work[i] = (subject, policy, AlertEvaluator.KeyFor(subject, policy, today));
            }

            var existing = await ledger.GetAsync(work.Select(w => w.Key).Distinct().ToList(), ct).ConfigureAwait(false);

            int notified = 0, skipped = 0, cleared = 0;
            foreach (var (subject, policy, key) in work)
            {
                ct.ThrowIfCancellationRequested();
                var decision = AlertEvaluator.Evaluate(subject, policy, existing.GetValueOrDefault(key), today);
                switch (decision.Action)
                {
                    case AlertAction.Notify:
                        if (await ledger.TryClaimAsync(key, today, nowUtc, policy.RepeatEveryDays, ct).ConfigureAwait(false))
                        {
                            await notify(decision, subject, ct).ConfigureAwait(false);
                            notified++;
                        }
                        else
                        {
                            skipped++;
                        }
                        break;

                    case AlertAction.Clear:
                        await ledger.ClearAsync(subject.ItemType, subject.ItemId, subject.Kind, ct).ConfigureAwait(false);
                        cleared++;
                        break;

                    default:
                        skipped++;
                        break;
                }
            }

            return new AlertRunSummary(work.Length, notified, skipped, cleared);
        }
    }
}
