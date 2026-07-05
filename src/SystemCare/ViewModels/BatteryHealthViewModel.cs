using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Models;
using SystemCare.Services;

namespace SystemCare.ViewModels;

public partial class BatteryHealthViewModel : ObservableObject
{
    private readonly IBatteryHealthService _battery;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportReportCommand))]
    private bool _isBusy;

    [ObservableProperty] private bool _hasBattery = true;
    [ObservableProperty] private double _healthScore;
    [ObservableProperty] private string _healthBand = "";
    [ObservableProperty] private string _summaryText = "Reading battery health…";
    [ObservableProperty] private string _batteryName = "—";
    [ObservableProperty] private string _chemistryText = "—";
    [ObservableProperty] private string _designCapacityText = "—";
    [ObservableProperty] private string _fullChargeCapacityText = "—";
    [ObservableProperty] private string _wearText = "—";
    [ObservableProperty] private string _cycleCountText = "—";
    [ObservableProperty] private string _chargeText = "—";
    [ObservableProperty] private string _powerStateText = "—";
    [ObservableProperty] private string _exportStatus = "";

    private bool _hasLoaded;

    public BatteryHealthViewModel(IBatteryHealthService battery) => _battery = battery;

    public async void OnNavigatedTo()
    {
        if (_hasLoaded || IsBusy) return;
        _hasLoaded = true;
        await RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var r = await _battery.GetReportAsync();
            HasBattery = r.HasBattery;

            if (!r.HasBattery)
            {
                SummaryText = "No battery detected — this looks like a desktop PC.";
                HealthScore = 0;
                HealthBand = "";
                return;
            }

            HealthScore = r.HealthPercent;
            HealthBand = r.HealthBand;
            BatteryName = string.IsNullOrWhiteSpace(r.Manufacturer) ? r.Name : $"{r.Manufacturer} {r.Name}";
            ChemistryText = string.IsNullOrWhiteSpace(r.Chemistry) ? "Unknown" : r.Chemistry;
            DesignCapacityText = FormatCapacity(r.DesignCapacityMilliWattHours);
            FullChargeCapacityText = FormatCapacity(r.FullChargeCapacityMilliWattHours);
            WearText = $"{r.WearPercent:0.#}%";
            CycleCountText = r.CycleCount > 0 ? r.CycleCount.ToString() : "Not reported";
            ChargeText = r.ChargePercent >= 0 ? $"{r.ChargePercent}%" : "—";
            PowerStateText = r.OnAcPower ? "Plugged in (AC)" : "On battery";

            SummaryText = r.DesignCapacityMilliWattHours > 0
                ? $"Battery health is {HealthScore:0}% ({r.HealthBand}). It holds {r.WearPercent:0.#}% less than when new."
                : $"Battery detected. Capacity data isn't reported by this device — use the detailed report for more.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task ExportReportAsync()
    {
        IsBusy = true;
        ExportStatus = "Generating detailed report…";
        try
        {
            string? path = await _battery.ExportDetailedReportAsync();
            ExportStatus = path is null
                ? "Couldn't generate the report on this device."
                : "Opened the detailed battery report in your browser.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool NotBusy() => !IsBusy;

    private static string FormatCapacity(long mWh) =>
        mWh > 0 ? $"{mWh:N0} mWh ({mWh / 1000.0:0.0} Wh)" : "Not reported";
}
