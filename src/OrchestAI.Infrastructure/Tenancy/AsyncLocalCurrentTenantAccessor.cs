using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Infrastructure.Tenancy;

public sealed class AsyncLocalCurrentTenantAccessor : ICurrentTenantAccessor
{
    private static readonly AsyncLocal<Guid?> Ambient = new();

    public Guid? TenantId => Ambient.Value;

    public IDisposable SetTenant(Guid tenantId)
    {
        var previous = Ambient.Value;
        Ambient.Value = tenantId;
        return new RestoreScope(previous);
    }

    private sealed class RestoreScope : IDisposable
    {
        private readonly Guid? _previous;
        private bool _disposed;

        public RestoreScope(Guid? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Ambient.Value = _previous;
        }
    }
}
