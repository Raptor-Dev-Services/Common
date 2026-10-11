using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;

namespace Common.Workflow.Folios
{
    /// <summary>
    /// Generador de folios en memoria para pruebas unitarias de los handlers. Reproduce el contrato de la
    /// implementacion PostgreSQL: con huecos cada llamada consume un numero; sin huecos el numero se asigna
    /// dentro de una transaccion que bloquea el ambito hasta su COMMIT y un rollback lo devuelve.
    /// </summary>
    /// <remarks>
    /// Seguro entre hilos. El modo sin huecos solo acepta transacciones creadas con
    /// <see cref="BeginTransaction"/>: una <see cref="DbTransaction"/> ajena no avisa de su COMMIT ni de su
    /// rollback, asi que no se puede imitar su efecto sobre el contador.
    /// </remarks>
    public sealed class InMemoryFolioGenerator : IFolioGenerator, IGaplessFolioGenerator
    {
        private readonly ConcurrentDictionary<string, Counter> _counters = new(StringComparer.Ordinal);

        /// <inheritdoc />
        public async ValueTask<long> NextAsync(FolioKey key, CancellationToken ct)
        {
            var counter = CounterFor(key);
            // Como la fila de folio_counter: espera si una transaccion sin huecos tiene el ambito tomado.
            await counter.Row.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return Interlocked.Increment(ref counter.LastValue);
            }
            finally
            {
                counter.Row.Release();
            }
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentException">Si la transaccion no se creo con <see cref="BeginTransaction"/> de este generador.</exception>
        /// <exception cref="InvalidOperationException">Si la transaccion ya se confirmo o revirtio.</exception>
        public async ValueTask<long> NextInTransactionAsync(FolioKey key, DbTransaction transaction, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(transaction);
            if (transaction is not Transaction tx || !ReferenceEquals(tx.Owner, this))
                throw new ArgumentException(
                    "InMemoryFolioGenerator solo acepta transacciones creadas con su BeginTransaction().", nameof(transaction));
            tx.EnsureActive();

            var name = NameOf(key);
            if (!tx.Held.TryGetValue(name, out var held))
            {
                var counter = CounterFor(key);
                await counter.Row.WaitAsync(ct).ConfigureAwait(false);
                held = new Hold(counter, Interlocked.Read(ref counter.LastValue));
                tx.Held[name] = held;
            }

            held.Pending++;
            return held.Pending;
        }

        /// <summary>
        /// Abre una transaccion de negocio simulada para el modo sin huecos. <c>Commit</c> fija los numeros
        /// asignados en ella; <c>Rollback</c> o <c>Dispose</c> sin COMMIT los devuelve.
        /// </summary>
        /// <returns>La transaccion.</returns>
        public DbTransaction BeginTransaction() => new Transaction(this);

        /// <summary>
        /// Fija el ultimo numero confirmado de un contador, como el relleno de una migracion o una
        /// restauracion de respaldo. El siguiente <see cref="NextAsync"/> devolvera <c>lastValue + 1</c>.
        /// </summary>
        /// <param name="key">Contador.</param>
        /// <param name="lastValue">Ultimo numero asignado; cero o mas.</param>
        public void Seed(FolioKey key, long lastValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(lastValue);
            var counter = CounterFor(key);
            counter.Row.Wait();
            try
            {
                Interlocked.Exchange(ref counter.LastValue, lastValue);
            }
            finally
            {
                counter.Row.Release();
            }
        }

        /// <summary>
        /// Ultimo numero confirmado de un contador; cero si nunca se uso.
        /// </summary>
        /// <param name="key">Contador.</param>
        /// <returns>El ultimo numero confirmado.</returns>
        public long LastValue(FolioKey key) =>
            _counters.TryGetValue(NameOf(key), out var counter) ? Interlocked.Read(ref counter.LastValue) : 0;

        private Counter CounterFor(FolioKey key) => _counters.GetOrAdd(NameOf(key), static _ => new Counter());

        private static string NameOf(FolioKey key)
        {
            ArgumentNullException.ThrowIfNull(key);
            // Igual que la llave primaria (sequence_name, scope_key); el salto de linea no aparece en ninguna.
            var scope = key.ToScopeKey();
            return key.Sequence + "\n" + scope;
        }

        private sealed class Counter
        {
            public long LastValue;
            public readonly SemaphoreSlim Row = new(1, 1);
        }

        private sealed class Hold(Counter counter, long pending)
        {
            public Counter Counter { get; } = counter;
            public long Pending { get; set; } = pending;
        }

        private sealed class Transaction(InMemoryFolioGenerator owner) : DbTransaction
        {
            private bool _completed;

            public InMemoryFolioGenerator Owner { get; } = owner;

            public Dictionary<string, Hold> Held { get; } = new(StringComparer.Ordinal);

            public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

            protected override DbConnection? DbConnection => null;

            public void EnsureActive()
            {
                if (_completed)
                    throw new InvalidOperationException("La transaccion ya se confirmo o revirtio.");
            }

            public override void Commit() => Complete(commit: true);

            public override void Rollback() => Complete(commit: false);

            protected override void Dispose(bool disposing)
            {
                if (disposing && !_completed)
                    Complete(commit: false);
                base.Dispose(disposing);
            }

            private void Complete(bool commit)
            {
                EnsureActive();
                _completed = true;
                foreach (var hold in Held.Values)
                {
                    if (commit)
                        Interlocked.Exchange(ref hold.Counter.LastValue, hold.Pending);
                    hold.Counter.Row.Release();
                }
                Held.Clear();
            }
        }
    }
}
