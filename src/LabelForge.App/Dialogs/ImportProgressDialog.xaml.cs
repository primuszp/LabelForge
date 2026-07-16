using System.Windows;

namespace LabelForge.App.Dialogs;

public partial class ImportProgressDialog : Window, IProgress<int>
{
    public ImportProgressDialog(Window owner)
    {
        InitializeComponent();
        Owner = owner;
    }

    public void Report(int value)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Report(value));
            return;
        }

        Progress.Value = value;
        StatusText.Text = $"Dataset beolvasása... {value}%";
    }
}
