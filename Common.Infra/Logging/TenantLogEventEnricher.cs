using Serilog.Core;
using Serilog.Events;
using Common.MultiTenancy;

namespace Common.Logging
{
    /// <summary>
    /// Enriquecedor de Serilog que agrega la propiedad <c>TenantId</c> con el tenant actual
    /// (<see cref="TenantContextAccessor.CurrentTenantId"/>), si hay uno y el evento no la trae ya.
    /// </summary>
    public sealed class TenantLogEventEnricher : ILogEventEnricher
    {
        /// <summary>
        /// Agrega <c>TenantId</c> al evento si hay tenant actual.
        /// </summary>
        /// <param name="logEvent">Evento de log.</param>
        /// <param name="propertyFactory">Fabrica de propiedades de Serilog.</param>
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            var tenantId = TenantContextAccessor.CurrentTenantId;
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return;
            }

            var property = propertyFactory.CreateProperty("TenantId", tenantId);
            logEvent.AddPropertyIfAbsent(property);
        }
    }
}
