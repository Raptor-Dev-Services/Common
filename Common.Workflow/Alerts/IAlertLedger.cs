namespace Common.Workflow.Alerts
{
    /// <summary>
    /// Puerto del ledger de avisos: que se aviso, por elemento, tipo y ciclo. Lo implementa el producto
    /// (tabla <c>alert_ledger</c> con reclamo atomico <c>INSERT ... ON CONFLICT ... WHERE</c>, ver SRS-CMN-C04 seccion 4).
    /// </summary>
    public interface IAlertLedger
    {
        /// <summary>
        /// Lee en lote las entradas existentes de las claves pedidas. Las que no existen no aparecen.
        /// </summary>
        /// <param name="keys">Claves a leer.</param>
        /// <param name="ct">Token de cancelacion.</param>
        Task<IReadOnlyDictionary<AlertKey, AlertLedgerEntry>> GetAsync(IReadOnlyCollection<AlertKey> keys, CancellationToken ct);

        /// <summary>
        /// Reclama el aviso de forma atomica: inserta la entrada del ciclo, o la actualiza si ya existia y
        /// toca repetir (<c>LastSentOn + repeatEveryDays &lt;= today</c>).
        /// </summary>
        /// <param name="key">Clave del aviso.</param>
        /// <param name="today">Fecha local de la sucursal.</param>
        /// <param name="nowUtc">Instante del reclamo, en UTC.</param>
        /// <param name="repeatEveryDays">Dias de repeticion de la politica; nulo = una vez por ciclo.</param>
        /// <param name="ct">Token de cancelacion.</param>
        /// <returns><c>true</c> si esta llamada gano el aviso (inserto, o repitio porque tocaba).</returns>
        Task<bool> TryClaimAsync(AlertKey key, DateOnly today, DateTime nowUtc, int? repeatEveryDays, CancellationToken ct);

        /// <summary>
        /// Borra todas las entradas de un elemento y tipo (se usa para umbrales que dejaron de cumplirse).
        /// </summary>
        /// <param name="itemType">Tipo de elemento.</param>
        /// <param name="itemId">Id publico del elemento.</param>
        /// <param name="kind">Tipo de alerta.</param>
        /// <param name="ct">Token de cancelacion.</param>
        Task ClearAsync(string itemType, string itemId, AlertKind kind, CancellationToken ct);
    }

    /// <summary>
    /// Ledger en memoria, seguro entre hilos, con la misma semantica de reclamo que el SQL de referencia.
    /// Sirve para pruebas (del producto y de la libreria); no persiste nada.
    /// </summary>
    public sealed class InMemoryAlertLedger : IAlertLedger
    {
        private readonly Dictionary<AlertKey, AlertLedgerEntry> _entries = [];
        private readonly Lock _gate = new();

        /// <summary>
        /// Crea el ledger, opcionalmente con entradas previas.
        /// </summary>
        /// <param name="seed">Entradas iniciales.</param>
        public InMemoryAlertLedger(IEnumerable<AlertLedgerEntry>? seed = null)
        {
            foreach (var entry in seed ?? [])
                _entries[entry.Key] = entry;
        }

        /// <summary>
        /// Copia de todas las entradas actuales.
        /// </summary>
        public IReadOnlyList<AlertLedgerEntry> Entries
        {
            get { lock (_gate) return [.. _entries.Values]; }
        }

        /// <inheritdoc />
        public Task<IReadOnlyDictionary<AlertKey, AlertLedgerEntry>> GetAsync(IReadOnlyCollection<AlertKey> keys, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(keys);
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                IReadOnlyDictionary<AlertKey, AlertLedgerEntry> found = keys.Distinct()
                    .Where(_entries.ContainsKey)
                    .ToDictionary(k => k, k => _entries[k]);
                return Task.FromResult(found);
            }
        }

        /// <inheritdoc />
        public Task<bool> TryClaimAsync(AlertKey key, DateOnly today, DateTime nowUtc, int? repeatEveryDays, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(key);
            if (nowUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("nowUtc debe tener Kind = Utc.", nameof(nowUtc));
            ct.ThrowIfCancellationRequested();

            lock (_gate)
            {
                if (!_entries.TryGetValue(key, out var entry))
                {
                    _entries[key] = new AlertLedgerEntry(key, today, today, 1);
                    return Task.FromResult(true);
                }

                if (repeatEveryDays is { } every && entry.LastSentOn.AddDays(every) <= today)
                {
                    _entries[key] = entry with { LastSentOn = today, SendCount = entry.SendCount + 1 };
                    return Task.FromResult(true);
                }

                return Task.FromResult(false);
            }
        }

        /// <inheritdoc />
        public Task ClearAsync(string itemType, string itemId, AlertKind kind, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                foreach (var key in _entries.Keys.Where(k => k.ItemType == itemType && k.ItemId == itemId && k.Kind == kind).ToList())
                    _entries.Remove(key);
            }
            return Task.CompletedTask;
        }
    }
}
