using System.Windows;
using System.Windows.Media.Animation;

namespace SystemCare;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    /// <summary>Fades the splash out, then closes it. Completes once it's gone.</summary>
    public Task FadeOutAndCloseAsync()
    {
        var tcs = new TaskCompletionSource();
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        fade.Completed += (_, _) =>
        {
            Close();
            tcs.TrySetResult();
        };
        BeginAnimation(OpacityProperty, fade);
        return tcs.Task;
    }
}
