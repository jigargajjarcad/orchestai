using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Infrastructure.Tenancy;

public sealed class AsyncLocalCurrentTenantAccessor : ICurrentTenantAccessor
{
    private static readonly AsyncLocal<Guid?> Ambient = new();

    // Independent AsyncLocal from the tenant-id one above — deliberately not derived from or
    // mutually exclusive with it. See ICurrentTenantAccessor.IsSystemWriteScope.
    private static readonly AsyncLocal<bool> SystemWriteScopeFlag = new();

    public Guid? TenantId => Ambient.Value;

    public IDisposable SetTenant(Guid tenantId)
    {
        var previous = Ambient.Value;
        Ambient.Value = tenantId;
        return new RestoreScope(previous);
    }

    public bool IsSystemWriteScope => SystemWriteScopeFlag.Value;

    public IDisposable BeginSystemWriteScope()
    {
        var previous = SystemWriteScopeFlag.Value;
        SystemWriteScopeFlag.Value = true;
        return new RestoreSystemWriteScope(previous);
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

    private sealed class RestoreSystemWriteScope : IDisposable
    {
        private readonly bool _previous;
        private bool _disposed;

        public RestoreSystemWriteScope(bool previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SystemWriteScopeFlag.Value = _previous;
        }
    }
}
