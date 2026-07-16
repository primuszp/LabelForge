using System.Windows.Controls;
using LabelForge.Core;

namespace LabelForge.App.Controls;

public partial class ReviewPanel : UserControl
{
    private bool updating;

    public ReviewPanel()
    {
        InitializeComponent();
        StatusBox.ItemsSource = Enum.GetValues<AnnotationWorkflowStatus>();
        SplitBox.ItemsSource = Enum.GetValues<DatasetSplit>();
        QualityBox.ItemsSource = Enum.GetValues<ImageQualityStatus>();
    }

    public event EventHandler? ReviewChanged;

    public void SetDocument(ImageDocument document)
    {
        updating = true;
        StatusBox.SelectedItem = document.WorkflowStatus;
        SplitBox.SelectedItem = document.Split;
        QualityBox.SelectedItem = document.QualityStatus;
        ReviewerBox.Text = document.Reviewer ?? Environment.UserName;
        updating = false;
    }

    public void SetStatus(AnnotationWorkflowStatus status)
    {
        StatusBox.SelectedItem = status;
    }

    public void ApplyTo(ImageDocument document)
    {
        if (StatusBox.SelectedItem is AnnotationWorkflowStatus status) document.WorkflowStatus = status;
        if (SplitBox.SelectedItem is DatasetSplit split) document.Split = split;
        if (QualityBox.SelectedItem is ImageQualityStatus quality) document.QualityStatus = quality;
        document.Reviewer = ReviewerBox.Text.Trim();
        document.ReviewedAt = DateTimeOffset.UtcNow;
        foreach (var annotation in document.Annotations)
        {
            annotation.WorkflowStatus = document.WorkflowStatus;
            annotation.Reviewer = document.Reviewer;
            annotation.ReviewedAt = document.ReviewedAt;
        }
        document.IsDirty = true;
    }

    private void ValueOnChanged(object sender, EventArgs e)
    {
        if (!updating) ReviewChanged?.Invoke(this, EventArgs.Empty);
    }
}
