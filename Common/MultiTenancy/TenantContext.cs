using System.Threading;

namespace Common.MultiTenancy
{
    public sealed class TenantContext
    {
        public TenantContext(string tenantId)
        {
            TenantId = tenantId;
        }

        public string TenantId { get; }
    }

    public interface ITenantContextAccessor
    {
        TenantContext? Current { get; set; }
    }

    public sealed class TenantContextAccessor : ITenantContextAccessor
    {
        private static readonly AsyncLocal<TenantContextHolder> Holder = new();

        public static string? CurrentTenantId => Holder.Value?.Context?.TenantId;

        public TenantContext? Current
        {
            get => Holder.Value?.Context;
            set
            {
                var current = Holder.Value;
                if (current is not null)
                {
                    current.Context = null;
                }

                if (value is not null)
                {
                    Holder.Value = new TenantContextHolder { Context = value };
                }
            }
        }

        private sealed class TenantContextHolder
        {
            public TenantContext? Context;
        }
    }
}
