using Serilog.Core;
using Serilog.Events;
using Common.MultiTenancy;

namespace Common.Logging
{
    public sealed class TenantLogEventEnricher : ILogEventEnricher
    {
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
