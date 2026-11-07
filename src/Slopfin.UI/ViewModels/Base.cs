using CommunityToolkit.Mvvm.ComponentModel;
using Slopfin.Core.Api;

namespace Slopfin.UI.ViewModels;

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

    public async Task ActivateAsync()
    {
        if (!_loaded)
        {
            _loaded = true;
            await LoadAsync();
        }
        else await RefreshAsync();
        OnActivated();
    }

    /// <summary>Back / Escape pressed: return true if the page handled it itself (e.g. closed a dialog).</summary>
    public virtual bool HandleBack() => false;

    protected virtual Task LoadAsync() => Task.CompletedTask;
    protected virtual Task RefreshAsync() => Task.CompletedTask;
    protected virtual void OnActivated() { }
    public virtual void OnDeactivated() { }
}
