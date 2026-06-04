using Serilog.Context;

namespace Common.MultiTenancy
{
    public interface ITenantExecutionContextRunner
    {
        Task RunAsync(string tenantId, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);
        Task<T> RunAsync<T>(string tenantId, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default);
    }

    public sealed class TenantExecutionContextRunner : ITenantExecutionContextRunner
    {
        private readonly ITenantContextAccessor _tenantContextAccessor;

        public TenantExecutionContextRunner(ITenantContextAccessor tenantContextAccessor)
        {
            _tenantContextAccessor = tenantContextAccessor;
        }

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
