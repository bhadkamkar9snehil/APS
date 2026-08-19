using System.Windows;

namespace APS.DesktopHost;

public partial class MainWindow : Window
{
    public MainWindow(IServiceProvider services)
    {
        Services = services;
        InitializeComponent();
        DataContext = this;
    }

    public IServiceProvider Services { get; }
}
