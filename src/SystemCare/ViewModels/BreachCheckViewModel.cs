using CommunityToolkit.Mvvm.ComponentModel;
using SystemCare.Services;

namespace SystemCare.ViewModels;

public partial class BreachCheckViewModel : ObservableObject
{
    private readonly IBreachCheckService _breach;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string _resultText = "";
    /// <summary>"Success", "Danger", or "Caution" — drives the result banner colour.</summary>
    [ObservableProperty] private string _resultSeverity = "Informational";

    public BreachCheckViewModel(IBreachCheckService breach) => _breach = breach;

    /// <summary>Called from the page code-behind (PasswordBox.Password can't be data-bound safely).</summary>
    public async Task CheckAsync(string password)
    {
        if (IsBusy) return;
        IsBusy = true;
        HasResult = false;
        try
        {
            var r = await _breach.CheckPasswordAsync(password);
            ResultText = r.Message;
            ResultSeverity = !r.Ok ? "Caution" : r.Found ? "Danger" : "Success";
            HasResult = true;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
