using Common.Workflow.Alerts;

namespace Common.Workflow.Tests.Alerts;

/// <summary>
/// SRS-CMN-C04 REQ-009: orquestacion con el puerto del ledger. V9 (reclamo de repeticion), V10 (limpiar)
/// y V13 (dos corridas concurrentes, un solo aviso).
/// </summary>
public class AlertRunnerTests
{
    private static DateOnly D(string iso) => DateOnly.Parse(iso);
    private static readonly DateTime Now = new(2026, 11, 1, 13, 0, 0, DateTimeKind.Utc);
    private static readonly AlertPolicy Default = new(LeadDays: 30);

    private static readonly DateAlertSubject V2Subject = new("asset.warranty", "A1", AlertKind.End, D("2026-12-01"));

    [Fact]
    public async Task V13_dos_corridas_concurrentes_avisan_una_sola_vez()
    {
        // Las dos corridas leen el ledger vacio antes de que cualquiera reclame: la carrera la decide el reclamo.
        var ledger = new BarrierLedger(new InMemoryAlertLedger(), parties: 2);
        var notifications = 0;
        Task Notify(AlertDecision d, AlertSubject s, CancellationToken ct)
        {
            Interlocked.Increment(ref notifications);
            return Task.CompletedTask;
        }

        var runs = await Task.WhenAll(
            Task.Run(() => AlertRunner.RunAsync([V2Subject], _ => Default, ledger, Notify, D("2026-11-01"), Now, CancellationToken.None)),
            Task.Run(() => AlertRunner.RunAsync([V2Subject], _ => Default, ledger, Notify, D("2026-11-01"), Now, CancellationToken.None)));

        Assert.Equal(1, notifications);
        Assert.Equal(1, runs.Sum(r => r.Notified));
        Assert.Equal(1, runs.Sum(r => r.Skipped));
        Assert.All(runs, r => Assert.Equal(1, r.Evaluated));
    }

    [Fact]
    public async Task V9_reclamo_de_repeticion_suma_al_contador()
    {
        var key = new AlertKey("consumable", "TONER-1", AlertKind.Threshold, "below");
        var ledger = new InMemoryAlertLedger([new(key, D("2026-10-05"), D("2026-10-05"), 1)]);
        var subject = new ThresholdAlertSubject("consumable", "TONER-1", 3m, 5m);
        var policy = new AlertPolicy(30, RepeatEveryDays: 7);
        var sent = new List<AlertDecision>();

        var monday = await AlertRunner.RunAsync([subject], _ => policy, ledger, Collect(sent), D("2026-10-11"), Now, CancellationToken.None);
        Assert.Equal(0, monday.Notified);

        Assert.True(await ledger.TryClaimAsync(key, D("2026-10-12"), Now, 7, CancellationToken.None));
        Assert.Equal(2, ledger.Entries.Single().SendCount);
        Assert.Equal(D("2026-10-12"), ledger.Entries.Single().LastSentOn);
        Assert.Equal(D("2026-10-05"), ledger.Entries.Single().FirstSentOn);
    }

    [Fact]
    public async Task V9_la_corrida_del_dia_de_repeticion_notifica()
    {
        var key = new AlertKey("consumable", "TONER-1", AlertKind.Threshold, "below");
        var ledger = new InMemoryAlertLedger([new(key, D("2026-10-05"), D("2026-10-05"), 1)]);
        var sent = new List<AlertDecision>();

        var summary = await AlertRunner.RunAsync([new ThresholdAlertSubject("consumable", "TONER-1", 3m, 5m)],
            _ => new AlertPolicy(30, RepeatEveryDays: 7), ledger, Collect(sent), D("2026-10-12"), Now, CancellationToken.None);

        Assert.Equal(new AlertRunSummary(1, 1, 0, 0), summary);
        Assert.Equal(AlertReasons.Repeat, sent.Single().Reason);
        Assert.Equal(2, ledger.Entries.Single().SendCount);
    }

    [Fact]
    public async Task V10_umbral_recuperado_limpia_el_ledger()
    {
        var key = new AlertKey("consumable", "TONER-1", AlertKind.Threshold, "below");
        var ledger = new InMemoryAlertLedger([new(key, D("2026-10-05"), D("2026-10-05"), 1)]);
        var sent = new List<AlertDecision>();

        var summary = await AlertRunner.RunAsync([new ThresholdAlertSubject("consumable", "TONER-1", 8m, 5m)],
            _ => Default, ledger, Collect(sent), D("2026-10-12"), Now, CancellationToken.None);

        Assert.Equal(new AlertRunSummary(1, 0, 0, 1), summary);
        Assert.Empty(ledger.Entries);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task Segunda_corrida_del_mismo_dia_no_avisa()
    {
        var ledger = new InMemoryAlertLedger();
        var sent = new List<AlertDecision>();
        var subjects = new AlertSubject[] { V2Subject, new DateAlertSubject("asset.warranty", "A2", AlertKind.End, D("2027-06-01")) };

        var first = await AlertRunner.RunAsync(subjects, _ => Default, ledger, Collect(sent), D("2026-11-01"), Now, CancellationToken.None);
        var second = await AlertRunner.RunAsync(subjects, _ => Default, ledger, Collect(sent), D("2026-11-01"), Now.AddHours(1), CancellationToken.None);

        Assert.Equal(new AlertRunSummary(2, 1, 1, 0), first);
        Assert.Equal(new AlertRunSummary(2, 0, 2, 0), second);
        Assert.Single(sent);
        Assert.Equal(new AlertKey("asset.warranty", "A1", AlertKind.End, "2026-12-01"), ledger.Entries.Single().Key);
    }

    [Fact]
    public async Task Si_notificar_falla_la_excepcion_sube_para_que_el_producto_revierta()
    {
        var ledger = new InMemoryAlertLedger();
        await Assert.ThrowsAsync<InvalidOperationException>(() => AlertRunner.RunAsync([V2Subject], _ => Default, ledger,
            (_, _, _) => throw new InvalidOperationException("outbox caido"), D("2026-11-01"), Now, CancellationToken.None));
    }

    [Fact]
    public async Task Instante_que_no_es_UTC_es_error_del_programador()
    {
        var local = new DateTime(2026, 11, 1, 7, 0, 0, DateTimeKind.Local);
        await Assert.ThrowsAsync<ArgumentException>(() => AlertRunner.RunAsync([V2Subject], _ => Default,
            new InMemoryAlertLedger(), (_, _, _) => Task.CompletedTask, D("2026-11-01"), local, CancellationToken.None));
    }

    [Fact]
    public async Task Reclamo_en_memoria_es_atomico_entre_hilos()
    {
        var ledger = new InMemoryAlertLedger();
        var key = new AlertKey("asset.warranty", "A1", AlertKind.End, "2026-12-01");

        var claims = await Task.WhenAll(Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() => ledger.TryClaimAsync(key, D("2026-11-01"), Now, null, CancellationToken.None))));

        Assert.Single(claims, won => won);
    }

    private static Func<AlertDecision, AlertSubject, CancellationToken, Task> Collect(List<AlertDecision> sent)
        => (decision, _, _) =>
        {
            lock (sent) sent.Add(decision);
            return Task.CompletedTask;
        };

    // Ledger que retiene cada GetAsync hasta que todas las corridas leyeron: fuerza la carrera del reclamo.
    private sealed class BarrierLedger(IAlertLedger inner, int parties) : IAlertLedger
    {
        private readonly Barrier _barrier = new(parties);

        public async Task<IReadOnlyDictionary<AlertKey, AlertLedgerEntry>> GetAsync(IReadOnlyCollection<AlertKey> keys, CancellationToken ct)
        {
            var result = await inner.GetAsync(keys, ct);
            _barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            return result;
        }

        public Task<bool> TryClaimAsync(AlertKey key, DateOnly today, DateTime nowUtc, int? repeatEveryDays, CancellationToken ct)
            => inner.TryClaimAsync(key, today, nowUtc, repeatEveryDays, ct);

        public Task ClearAsync(string itemType, string itemId, AlertKind kind, CancellationToken ct)
            => inner.ClearAsync(itemType, itemId, kind, ct);
    }
}
