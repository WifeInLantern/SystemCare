using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Models;
using SystemCare.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SystemCare.ViewModels;

public partial class TweakRowViewModel : ObservableObject
{
    private readonly ITweaksService _service;
    private readonly WindowsTweaksViewModel _owner;
    private bool _suppress;

    public Tweak Tweak { get; }
    public string Name => Tweak.Name;
    public string Description => Tweak.Description +
        (Tweak.RequiresExplorerRestart ? " · needs Explorer restart" : "");

    [ObservableProperty] private bool _isEnabled;

    public TweakRowViewModel(Tweak tweak, ITweaksService service, WindowsTweaksViewModel owner)
    {
        Tweak = tweak;
        _service = service;
        _owner = owner;
        _suppress = true;
        IsEnabled = service.IsEnabled(tweak.Id);
        _suppress = false;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppress) return;
        _service.SetEnabled(Tweak.Id, value);
        if (Tweak.RequiresExplorerRestart) _owner.NotifyExplorerRestartNeeded();
    }
}

public partial class TweakGroupViewModel(string name)
{
    public string Name { get; } = name;
    public ObservableCollection<TweakRowViewModel> Items { get; } = [];
}

public partial class WindowsTweaksViewModel : ObservableObject
{
    private readonly ITweaksService _tweaks;
    private readonly IPowerPlanService _power;
    private readonly ISnackbarService _snackbar;
    private bool _suppressPower;

    public ObservableCollection<TweakGroupViewModel> Groups { get; } = [];
    public ObservableCollection<PowerScheme> PowerSchemes { get; } = [];

    [ObservableProperty] private PowerScheme? _activeScheme;

    public WindowsTweaksViewModel(ITweaksService tweaks, IPowerPlanService power, ISnackbarService snackbar)
    {
        _tweaks = tweaks;
        _power = power;
        _snackbar = snackbar;

        foreach (var group in tweaks.Tweaks.GroupBy(t => t.Group))
        {
            var g = new TweakGroupViewModel(group.Key);
            foreach (var t in group) g.Items.Add(new TweakRowViewModel(t, tweaks, this));
            Groups.Add(g);
        }
    }

    public void OnNavigatedTo()
    {
        if (PowerSchemes.Count > 0) return;
        var schemes = _power.ListSchemes();
        var active = _power.GetActiveScheme();
        PowerSchemes.Clear();
        foreach (var s in schemes) PowerSchemes.Add(s);
        _suppressPower = true;
        ActiveScheme = PowerSchemes.FirstOrDefault(s => s.Guid == active);
        _suppressPower = false;
    }

    partial void OnActiveSchemeChanged(PowerScheme? value)
    {
        if (_suppressPower || value is null) return;
        if (_power.SetActiveScheme(value.Guid))
            _snackbar.Show("Power plan changed", $"Active plan is now \"{value.Name}\".",
                ControlAppearance.Success, null, TimeSpan.FromSeconds(3));
    }

    public void NotifyExplorerRestartNeeded() => RestartExplorerCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void RestartExplorer()
    {
        _tweaks.RestartExplorer();
        _snackbar.Show("Explorer restarted", "Your Explorer tweaks are now applied.",
            ControlAppearance.Success, null, TimeSpan.FromSeconds(3));
    }
}
