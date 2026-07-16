using System.Windows.Controls;
using LabelForge.App.Services;

namespace LabelForge.App.Controls;

public partial class AiJobsPanel : UserControl
{
    public AiJobsPanel() => InitializeComponent();
    public void SetService(AiJobService service) => JobList.ItemsSource = service.Jobs;
}
