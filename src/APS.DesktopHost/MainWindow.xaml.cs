using System.Windows;

namespace APS.DesktopHost;

public partial class MainWindow : Window
{
    public MainWindow(IServiceProvider services)
    {
        InitializeComponent();
        BlazorHost.Services = services;
        SourceInitialized += (_, _) => NativeWindowTheme.Apply(this);
    }
}