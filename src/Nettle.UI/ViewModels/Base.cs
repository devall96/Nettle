using CommunityToolkit.Mvvm.ComponentModel;
using Nettle.Core.Api;

namespace Nettle.UI.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _error;

    /// <summary>Runs work with busy state + friendly error capture; never throws to the UI thread.</summary>
    protected async Task RunAsync(Func<Task> work, bool showBusy = true)
    {
        if (showBusy) IsBusy = true;
        Error = null;
        try { await work(); }
        catch (OperationCanceledException ex) when (ex.InnerException is TimeoutException) { Error = "The server took too long to respond."; }
        catch (OperationCanceledException) { }
        catch (ApiException ex) { Error = ex.Message; }
        catch (Exception ex) { Error = "Something went wrong: " + ex.Message; }
        finally { if (showBusy) IsBusy = false; }
    }
}

/// <summary>A screen inside the signed-in shell.</summary>
public abstract partial class PageViewModel : ViewModelBase
{
    public virtual string Title => "";
    /// <summary>Which rail entry is highlighted while this page is showing.</summary>
    public virtual string NavKey => "";
    private bool _loaded;
    private bool _active;

    public async Task ActivateAsync()
    {
        _active = true;
        if (!_loaded)
        {
            _loaded = true;
            await LoadAsync();
        }
        else await RefreshAsync();
        // Left while it was loading: starting its timers now would leave them running for good.
        if (_active) OnActivated();
    }

    /// <summary>The page is no longer on screen (navigated away, covered by the player, signed out).</summary>
    public void Deactivate()
    {
        if (!_active) return;
        _active = false;
        OnDeactivated();
    }

    /// <summary>Back / Escape pressed: return true if the page handled it itself (e.g. closed a dialog).</summary>
    public virtual bool HandleBack() => false;

    protected virtual Task LoadAsync() => Task.CompletedTask;
    protected virtual Task RefreshAsync() => Task.CompletedTask;
    protected virtual void OnActivated() { }
    protected virtual void OnDeactivated() { }
}
