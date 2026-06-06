using System.Windows;
using System.Windows.Input;
using System.ComponentModel;
using System.Windows.Controls;
using Primusz.ZoomPanel.Enums;

namespace Primusz.ZoomPanel
{
    public class ZoomPanelScrollViewer : ScrollViewer
    {
        #region Constructor and overrides

        /// <summary>
        /// Static constructor to define metadata for the control (and link it to the style in Generic.xaml).
        /// </summary>
        static ZoomPanelScrollViewer()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ZoomPanelScrollViewer), new FrameworkPropertyMetadata(typeof(ZoomPanelScrollViewer)));
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            ZoomAndPanContent = Template.FindName("PART_ZoomPanel", this) as ZoomPanel;
            OnPropertyChanged(new DependencyPropertyChangedEventArgs(ZoomAndPanContentProperty, null, ZoomAndPanContent));
            RefreshProperties();
        }

        #endregion

        /// <summary>
        /// Get/set the maximum value for 'ViewportZoom'.
        /// </summary>
        public ZoomPanel ZoomAndPanContent
        {
            get => (ZoomPanel)GetValue(ZoomAndPanContentProperty);
            set => SetValue(ZoomAndPanContentProperty, value);
        }

        public static readonly DependencyProperty ZoomAndPanContentProperty = DependencyProperty.Register("ZoomAndPanContent",
            typeof(ZoomPanel), typeof(ZoomPanelScrollViewer), new FrameworkPropertyMetadata(null));

        #region DependencyProperties

        /// <summary>
        /// Get/set the maximum value for 'ViewportZoom'.
        /// </summary>
        public MinimumZoomTypeEnum MinimumZoomType
        {
            get => (MinimumZoomTypeEnum)GetValue(MinimumZoomTypeProperty);
            set => SetValue(MinimumZoomTypeProperty, value);
        }

        public static readonly DependencyProperty MinimumZoomTypeProperty = DependencyProperty.Register("MinimumZoomType",
            typeof(MinimumZoomTypeEnum), typeof(ZoomPanelScrollViewer), new FrameworkPropertyMetadata(MinimumZoomTypeEnum.MinimumZoom));

        /// <summary>
        /// Get/set the MinimumZoom value for 'ViewportZoom'.
        /// </summary>
        public Point? MousePosition
        {
            get => (Point?)GetValue(MousePositionProperty);
            set => SetValue(MousePositionProperty, value);
        }

        public static readonly DependencyProperty MousePositionProperty = DependencyProperty.Register("MousePosition",
            typeof(Point?), typeof(ZoomPanelScrollViewer), new FrameworkPropertyMetadata(null));

        /// <summary>
        /// Disables animations if set to false
        /// </summary>
        public bool UseAnimations
        {
            get => (bool)GetValue(UseAnimationsProperty);
            set => SetValue(UseAnimationsProperty, value);
        }

        public static readonly DependencyProperty UseAnimationsProperty = DependencyProperty.Register("UseAnimations",
            typeof(bool), typeof(ZoomPanelScrollViewer), new FrameworkPropertyMetadata(true));

        /// <summary>
        /// Get/set the current scale (or zoom factor) of the content.
        /// </summary>
        public double ViewportZoom
        {
            get => (double)GetValue(ViewportZoomProperty);
            set => SetValue(ViewportZoomProperty, value);
        }

        public static readonly DependencyProperty ViewportZoomProperty = DependencyProperty.Register("ViewportZoom",
            typeof(double), typeof(ZoomPanelScrollViewer), new FrameworkPropertyMetadata(1.0));

        /// <summary>
        /// The duration of the animations (in seconds) started by calling AnimatedZoomTo and the other animation methods.
        /// </summary>
        public ZoomPanelInitialPositionEnum ZoomAndPanInitialPosition
        {
            get => (ZoomPanelInitialPositionEnum)GetValue(ZoomAndPanInitialPositionProperty);
            set => SetValue(ZoomAndPanInitialPositionProperty, value);
        }

        public static readonly DependencyProperty ZoomAndPanInitialPositionProperty = DependencyProperty.Register("ZoomAndPanInitialPosition",
            typeof(ZoomPanelInitialPositionEnum), typeof(ZoomPanelScrollViewer), new FrameworkPropertyMetadata(ZoomPanelInitialPositionEnum.Default));

        #endregion

        #region Commands
        /// <summary>
        ///     Command to implement the zoom to fill 
        /// </summary>
        public ICommand FillCommand => fillCommand ?? (fillCommand =
                new RelayCommand(
                    () => ZoomAndPanContent.FillCommand.Execute(null),
                    () => ZoomAndPanContent?.FillCommand.CanExecute(null) ?? true));
        private RelayCommand fillCommand;

        /// <summary>
        ///     Command to implement the zoom to fit 
        /// </summary>
        public ICommand FitCommand => fitCommand ?? (fitCommand =
                new RelayCommand(
                    () => ZoomAndPanContent.FitCommand.Execute(null),
                    () => ZoomAndPanContent?.FitCommand.CanExecute(null) ?? true));
        private RelayCommand fitCommand;

        /// <summary>
        ///     Command to implement the zoom to 100% 
        /// </summary>
        public ICommand ZoomPercentCommand => zoomPercentCommand ?? (zoomPercentCommand =
                new RelayCommand<double>(
                    value => ZoomAndPanContent.ZoomPercentCommand.Execute(value),
                    value => ZoomAndPanContent?.ZoomPercentCommand.CanExecute(value) ?? true));
         private RelayCommand<double> zoomPercentCommand;

        /// <summary>
        ///     Command to implement the zoom to 100% 
        /// </summary>
        public ICommand ZoomRatioFromMinimumCommand => zoomRatioFromMinimumCommand ?? (zoomRatioFromMinimumCommand =
                new RelayCommand<double>(
                    value => ZoomAndPanContent.ZoomRatioFromMinimumCommand.Execute(value),
                    value => ZoomAndPanContent?.ZoomRatioFromMinimumCommand.CanExecute(value) ?? true));
        private RelayCommand<double> zoomRatioFromMinimumCommand;

        /// <summary>
        ///     Command to implement the zoom out by 110% 
        /// </summary>
        public ICommand ZoomOutCommand => zoomOutCommand ?? (zoomOutCommand =
                new RelayCommand(
                    () => ZoomAndPanContent.ZoomOutCommand.Execute(null),
                    () => ZoomAndPanContent?.ZoomOutCommand.CanExecute(null) ?? true));
        private RelayCommand zoomOutCommand;

        /// <summary>
        ///     Command to implement the zoom in by 91% 
        /// </summary>
        public ICommand ZoomInCommand => zoomInCommand ?? (zoomInCommand =
                new RelayCommand(
                    () => ZoomAndPanContent.ZoomInCommand.Execute(null),
                    () => ZoomAndPanContent?.ZoomInCommand.CanExecute(null) ?? true));
        private RelayCommand zoomInCommand;

        /// <summary>
        ///     Command to implement Undo 
        /// </summary>
        public ICommand UndoZoomCommand => undoZoomCommand ?? (undoZoomCommand =
                new RelayCommand(
                    () => ZoomAndPanContent.UndoZoomCommand.Execute(null),
                    () => ZoomAndPanContent?.UndoZoomCommand.CanExecute(null) ?? true));
        private RelayCommand undoZoomCommand;

        /// <summary>
        ///     Command to implement Redo 
        /// </summary>
        public ICommand RedoZoomCommand => redoZoomCommand ?? (redoZoomCommand =
                new RelayCommand(
                    () => ZoomAndPanContent.RedoZoomCommand.Execute(null),
                    () => ZoomAndPanContent?.RedoZoomCommand.CanExecute(null) ?? true));
        private RelayCommand redoZoomCommand;
        #endregion

        private void RefreshProperties()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FillCommand)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FitCommand)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ZoomPercentCommand)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ZoomRatioFromMinimumCommand)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ZoomInCommand)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ZoomOutCommand)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UndoZoomCommand)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RedoZoomCommand)));
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}