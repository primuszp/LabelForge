using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using Primusz.ZoomPanel.Enums;
using Primusz.ZoomPanel.Helpers;

namespace Primusz.ZoomPanel
{
    /// <summary>
    /// A class that wraps up zooming and panning of it's content.
    /// </summary>
    public class ZoomPanelViewBox : ContentControl
    {
        #region Local Fields
        /// <summary>
        /// The control for creating a drag border
        /// </summary>
        private Border dragBorder;

        /// <summary>
        /// The control for creating a drag border
        /// </summary>
        private Border sizingBorder;

        /// <summary>
        /// The control for containing a zoom border
        /// </summary>
        private Canvas viewportCanvas;

        /// <summary>
        /// Specifies the current state of the mouse handling logic.
        /// </summary>
        private MouseHandlingModeEnum mouseHandlingMode = MouseHandlingModeEnum.None;

        /// <summary>
        /// The point that was clicked relative to the content that is contained within the ZoomPanel.
        /// </summary>
        private Point origContentMouseDownPoint;

        #endregion

        #region Constructors and overrides

        /// <summary>
        /// Static constructor to define metadata for the control (and link it to the style in Generic.xaml).
        /// </summary>
        static ZoomPanelViewBox()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ZoomPanelViewBox), new FrameworkPropertyMetadata(typeof(ZoomPanelViewBox)));
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            dragBorder = Template.FindName("PART_DraggingBorder", this) as Border;
            sizingBorder = Template.FindName("PART_SizingBorder", this) as Border;
            viewportCanvas = Template.FindName("PART_Content", this) as Canvas;
            SetBackground(Visual);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            if (ActualWidth > 0 && viewportCanvas != null)
            {
                sizingBorder.BorderThickness = dragBorder.BorderThickness = new Thickness(
                   viewportCanvas.ActualWidth / ActualWidth * BorderThickness.Left,
                   viewportCanvas.ActualWidth / ActualWidth * BorderThickness.Top,
                   viewportCanvas.ActualWidth / ActualWidth * BorderThickness.Right,
                   viewportCanvas.ActualWidth / ActualWidth * BorderThickness.Bottom);
            }
        }

        #endregion

        #region Mouse Event Handlers

        /// <summary>
        /// Event raised on mouse down in the ZoomPanel.
        /// </summary>
        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            OnMouseLeftButtonDown(e);

            GetZoomPanel().SaveZoom();
            mouseHandlingMode = MouseHandlingModeEnum.Panning;
            origContentMouseDownPoint = e.GetPosition(viewportCanvas);

            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                // Shift + left- or right-down initiates zooming mode.
                mouseHandlingMode = MouseHandlingModeEnum.DragZooming;
                dragBorder.Visibility = Visibility.Hidden;
                sizingBorder.Visibility = Visibility.Visible;
                Canvas.SetLeft(sizingBorder, origContentMouseDownPoint.X);
                Canvas.SetTop(sizingBorder, origContentMouseDownPoint.Y);
                sizingBorder.Width = 0;
                sizingBorder.Height = 0;
            }
            else
            {
                // Just a plain old left-down initiates panning mode.
                mouseHandlingMode = MouseHandlingModeEnum.Panning;
            }

            if (mouseHandlingMode != MouseHandlingModeEnum.None)
            {
                // Capture the mouse so that we eventually receive the mouse up event.
                viewportCanvas.CaptureMouse();
                e.Handled = true;
            }
        }

        /// <summary>
        /// Event raised on mouse up in the ZoomPanel.
        /// </summary>
        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            OnMouseLeftButtonUp(e);

            if (mouseHandlingMode == MouseHandlingModeEnum.DragZooming)
            {
                var zoomPanel = GetZoomPanel();
                var curContentPoint = e.GetPosition(viewportCanvas);
                var rect = ViewportHelper.Clip(curContentPoint, origContentMouseDownPoint, new Point(0, 0),
                    new Point(viewportCanvas.Width, viewportCanvas.Height));
                zoomPanel.AnimatedZoomTo(rect);
                dragBorder.Visibility = Visibility.Visible;
                sizingBorder.Visibility = Visibility.Hidden;
            }
            mouseHandlingMode = MouseHandlingModeEnum.None;
            viewportCanvas.ReleaseMouseCapture();
            e.Handled = true;
        }

        /// <summary>
        /// Event raised on mouse move in the ZoomPanel.
        /// </summary>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (mouseHandlingMode == MouseHandlingModeEnum.Panning)
            {
                var curContentPoint = e.GetPosition(viewportCanvas);
                var rectangleDragVector = curContentPoint - origContentMouseDownPoint;
                //
                // When in 'dragging rectangles' mode update the position of the rectangle as the user drags it.
                //
                origContentMouseDownPoint = e.GetPosition(viewportCanvas).Clamp();
                Canvas.SetLeft(dragBorder, Canvas.GetLeft(dragBorder) + rectangleDragVector.X);
                Canvas.SetTop(dragBorder, Canvas.GetTop(dragBorder) + rectangleDragVector.Y);
            }
            else if (mouseHandlingMode == MouseHandlingModeEnum.DragZooming)
            {
                var curContentPoint = e.GetPosition(viewportCanvas);
                var rect = ViewportHelper.Clip(curContentPoint, origContentMouseDownPoint, new Point(0, 0), new Point(viewportCanvas.Width, viewportCanvas.Height));
                ViewportHelper.PositionBorderOnCanvas(sizingBorder, rect);
            }

            e.Handled = true;
        }

        /// <summary>
        /// Event raised with the double click command
        /// </summary>
        protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
        {
            base.OnMouseDoubleClick(e);

            if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                var zoomPanel = GetZoomPanel();
                zoomPanel.SaveZoom();
                zoomPanel.AnimatedSnapTo(e.GetPosition(viewportCanvas));
            }
        }

        #endregion

        #region Background-Visual Brush

        /// <summary>
        /// The X coordinate of the content focus, this is the point that we are focusing on when zooming.
        /// </summary>
        public FrameworkElement Visual
        {
            get => (FrameworkElement)GetValue(VisualProperty);
            set => SetValue(VisualProperty, value);
        }
        public static readonly DependencyProperty VisualProperty = DependencyProperty.Register("Visual",
            typeof(FrameworkElement), typeof(ZoomPanelViewBox), new FrameworkPropertyMetadata(null, OnVisualChanged));

        private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (ZoomPanelViewBox)d;
            c.SetBackground(e.NewValue as FrameworkElement);
        }

        private void SetBackground(FrameworkElement frameworkElement)
        {
            frameworkElement = frameworkElement ?? (DataContext as ContentControl)?.Content as FrameworkElement;
            var visualBrush = new VisualBrush
            {
                Visual = frameworkElement,
                ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
                TileMode = TileMode.None,
                Stretch = Stretch.Fill
            };

            if (frameworkElement != null) frameworkElement.SizeChanged += (s, e) =>
            {
                viewportCanvas.Height = frameworkElement.ActualHeight;
                viewportCanvas.Width = frameworkElement.ActualWidth;
                viewportCanvas.Background = visualBrush;
            };
        }
        #endregion

        private ZoomPanel GetZoomPanel()
        {
            var zoomPanel = DataContext as ZoomPanel ?? (DataContext as ZoomPanelScrollViewer)?.ZoomAndPanContent;

            if (zoomPanel == null)
            {
                throw new NullReferenceException("DataContext is not of type ZoomPanel");
            }

            return zoomPanel;
        }
    }
}