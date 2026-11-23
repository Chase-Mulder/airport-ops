using Airport.Core.Simulation;
using Microsoft.AspNetCore.Components;

namespace Airport.UI.Components;

/// <summary>
/// Base class for pages that show live engine data. Subscribes to <see cref="AirportEngine.Changed"/>,
/// throttles re-renders to <see cref="RefreshMs"/> and calls <see cref="Refresh"/> to rebuild the
/// page's snapshot (always taken through <see cref="AirportEngine.Read{T}"/>).
/// </summary>
public abstract class EngineComponent : ComponentBase, IDisposable
{
    [Inject] protected AirportEngine Engine { get; set; } = default!;

    /// <summary>Minimum milliseconds between automatic refreshes.</summary>
    protected virtual int RefreshMs => 1000;

    private DateTime _lastRefresh = DateTime.MinValue;
    private bool _disposed;

    protected override void OnInitialized()
    {
        Refresh();
        Engine.Changed += OnEngineChanged;
    }

    private void OnEngineChanged()
    {
        if (_disposed || (DateTime.UtcNow - _lastRefresh).TotalMilliseconds < RefreshMs) return;
        _lastRefresh = DateTime.UtcNow;
        _ = InvokeAsync(() =>
        {
            if (_disposed) return;
            Refresh();
            StateHasChanged();
        });
    }

    /// <summary>Rebuild the data this page renders.</summary>
    protected abstract void Refresh();

    /// <summary>Refresh immediately (after a command).</summary>
    protected void RefreshNow()
    {
        Refresh();
        StateHasChanged();
    }

    public virtual void Dispose()
    {
        _disposed = true;
        Engine.Changed -= OnEngineChanged;
        GC.SuppressFinalize(this);
    }
}
