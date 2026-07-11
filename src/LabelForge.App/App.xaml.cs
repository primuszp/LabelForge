using System.Configuration;
using System.Data;
using System.Windows;

namespace LabelForge.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Localization.LocalizationService.Initialize();
        base.OnStartup(e);
    }
}

