using System.Windows;
using Wpf.Ui;

namespace SystemCare.Services;

/// <summary>Resolves navigation pages from the DI container for Wpf.Ui's NavigationView.</summary>
public class PageService(IServiceProvider serviceProvider) : IPageService
{
    public T? GetPage<T>() where T : class => serviceProvider.GetService(typeof(T)) as T;

    public FrameworkElement? GetPage(Type pageType) => serviceProvider.GetService(pageType) as FrameworkElement;
}
