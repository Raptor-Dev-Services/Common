using Serilog.Context;

namespace Common.MultiTenancy
{
    /// <summary>
    /// Ejecuta codigo en nombre de un tenant fuera de una peticion HTTP (tareas en segundo
    /// plano, consumidores de colas).
    /// </summary>
    public interface ITenantExecutionContextRunner
    {
        /// <summary>
        /// Ejecuta <paramref name="action"/> con <paramref name="tenantId"/> como tenant actual.
        /// </summary>
        /// <param name="tenantId">Id del tenant.</param>
        /// <param name="action">Trabajo a ejecutar.</param>
        /// <param name="cancellationToken">Token que se pasa a <paramref name="action"/>.</param>
        /// <returns>Tarea que termina cuando termina <paramref name="action"/>.</returns>
        Task RunAsync(string tenantId, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);

        /// <summary>
        /// Ejecuta <paramref name="action"/> con <paramref name="tenantId"/> como tenant actual y devuelve su resultado.
        /// </summary>
        /// <typeparam name="T">Tipo del resultado.</typeparam>
        /// <param name="tenantId">Id del tenant.</param>
        /// <param name="action">Trabajo a ejecutar.</param>
        /// <param name="cancellationToken">Token que se pasa a <paramref name="action"/>.</param>
        /// <returns>El resultado de <paramref name="action"/>.</returns>
        Task<T> RunAsync<T>(string tenantId, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Implementacion de <see cref="ITenantExecutionContextRunner"/>. Durante la ejecucion fija
    /// el tenant en <see cref="ITenantContextAccessor"/>, la etiqueta <c>tenant.id</c> de la
    /// <see cref="System.Diagnostics.Activity"/> actual y la propiedad <c>TenantId</c> del
    /// <see cref="LogContext"/> de Serilog; al terminar restaura el tenant y la etiqueta anteriores.
    /// </summary>
    /// <remarks>
    /// No valida que el tenant exista ni que este habilitado. Ver la nota de
    /// <see cref="TenantContextAccessor.Current"/>: si se llama desde un flujo que ya tenia
    /// tenant, al volver ese flujo puede quedarse sin el.
    /// </remarks>
    public sealed class TenantExecutionContextRunner : ITenantExecutionContextRunner
    {
        private readonly ITenantContextAccessor _tenantContextAccessor;

        /// <summary>
        /// Crea el ejecutor.
        /// </summary>
        /// <param name="tenantContextAccessor">Accesor donde se fija el tenant.</param>
        public TenantExecutionContextRunner(ITenantContextAccessor tenantContextAccessor)
        {
            _tenantContextAccessor = tenantContextAccessor;
        }

        /// <summary>
        /// Ejecuta <paramref name="action"/> con <paramref name="tenantId"/> como tenant actual.
        /// Delega en <see cref="RunAsync{T}"/>.
        /// </summary>
        /// <param name="tenantId">Id del tenant.</param>
        /// <param name="action">Trabajo a ejecutar.</param>
        /// <param name="cancellationToken">Token que se pasa a <paramref name="action"/>.</param>
        /// <returns>Tarea que termina cuando termina <paramref name="action"/>.</returns>
        /// <exception cref="ArgumentException">Si <paramref name="tenantId"/> es nulo, vacio o solo espacios.</exception>
        public Task RunAsync(string tenantId, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
        {
            return RunAsync<object?>(
                tenantId,
                async ct =>
                {
                    await action(ct).ConfigureAwait(false);
                    return null;
                },
                cancellationToken);
        }

        /// <summary>
        /// Ejecuta <paramref name="action"/> con <paramref name="tenantId"/> como tenant actual y devuelve su resultado.
        /// Las excepciones de <paramref name="action"/> se propagan tal cual.
        /// </summary>
        /// <typeparam name="T">Tipo del resultado.</typeparam>
        /// <param name="tenantId">Id del tenant.</param>
        /// <param name="action">Trabajo a ejecutar.</param>
        /// <param name="cancellationToken">Token que se pasa a <paramref name="action"/>.</param>
        /// <returns>El resultado de <paramref name="action"/>.</returns>
        /// <exception cref="ArgumentException">Si <paramref name="tenantId"/> es nulo, vacio o solo espacios.</exception>
        public async Task<T> RunAsync<T>(string tenantId, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                throw new ArgumentException("Tenant id is required.", nameof(tenantId));
            }

            var previousContext = _tenantContextAccessor.Current;
            _tenantContextAccessor.Current = new TenantContext(tenantId);
            var previousTenantTag = System.Diagnostics.Activity.Current?.GetTagItem("tenant.id");
            System.Diagnostics.Activity.Current?.SetTag("tenant.id", tenantId);

            using var _ = LogContext.PushProperty("TenantId", tenantId);
            try
            {
                return await action(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (System.Diagnostics.Activity.Current is not null)
                {
                    if (previousTenantTag is null)
                    {
                        System.Diagnostics.Activity.Current.SetTag("tenant.id", null);
                    }
                    else
                    {
                        System.Diagnostics.Activity.Current.SetTag("tenant.id", previousTenantTag);
                    }
                }

                _tenantContextAccessor.Current = previousContext;
            }
        }
    }
}
