using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Primusz.ZoomPanel.Enums;
using Primusz.ZoomPanel.Helpers;

namespace Primusz.ZoomPanel
{
    public partial class ZoomPanel
    {
        private void ZoomPanel_EventHandlers_OnApplyTemplate()
        {
            partDragZoomBorder = Template.FindName("PART_DragZoomBorder", this) as Border;
            partDragZoomCanvas = Template.FindName("PART_DragZoomCanvas", this) as Canvas;
        }

        /// <summary>
        /// The control for creating a zoom border
        /// </summary>
        private Border partDragZoomBorder;

        /// <summary>
        /// The control for containing a zoom border
        /// </summary>
        private Canvas partDragZoomCanvas;

        /// <summary>
        /// Specifies the current state of the mouse handling logic.
        /// </summary>
        private MouseHandlingModeEnum mouseHandlingMode = MouseHandlingModeEnum.None;

        /// <summary>
        /// The point that was clicked relative to the ZoomPanel.
        /// </summary>
        private Point origZoomPanelMouseDownPoint;

        /// <summary>
        /// The point that was clicked relative to the content that is contained within the ZoomPanel.
        /// </summary>
        private Point origContentMouseDownPoint;

        /// <summary>
        /// Records which mouse button clicked during mouse dragging.
        /// </summary>
        private MouseButton mouseButtonDown;

        /// <summary>
        /// Event raised on mouse down in the ZoomPanel.
        /// </summary>
        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);

            SaveZoom();
            content.Focus();
            Keyboard.Focus(content);

            mouseButtonDown = e.ChangedButton;
            origZoomPanelMouseDownPoint = e.GetPosition(this);
            origContentMouseDownPoint = e.GetPosition(content);

            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 &&
                (e.ChangedButton == MouseButton.Left ||
                 e.ChangedButton == MouseButton.Right ||
                 e.ChangedButton == MouseButton.Middle))
            {
                // Shift + left- or right-down initiates zooming mode.
                mouseHandlingMode = MouseHandlingModeEnum.Zooming;
            }
            else if (mouseButtonDown == MouseButton.Middle)
            {
                // Just a plain old left-down initiates panning mode.
                mouseHandlingMode = MouseHandlingModeEnum.Panning;
            }

            if (mouseHandlingMode != MouseHandlingModeEnum.None)
            {
                // Capture the mouse so that we eventually receive the mouse up event.
                CaptureMouse();
            }
        }

        /// <summary>
        /// Event raised on mouse up in the ZoomPanel.
        /// </summary>
        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);

            if (mouseHandlingMode != MouseHandlingModeEnum.None)
            {
                if (mouseHandlingMode == MouseHandlingModeEnum.Zooming)
                {
                    if (mouseButtonDown == MouseButton.Left)
                    {
                        // Shift + left-click zooms in on the content.
                        ZoomIn(origContentMouseDownPoint);
                    }
                    else if (mouseButtonDown == MouseButton.Right)
                    {
                        // Shift + left-click zooms out from the content.
                        ZoomOut(origContentMouseDownPoint);
                    }
                }
                else if (mouseHandlingMode == MouseHandlingModeEnum.DragZooming)
                {
                    var finalContentMousePoint = e.GetPosition(content);
                    // When drag-zooming has finished we zoom in on the rectangle that was highlighted by the user.
                    ApplyDragZoomRect(finalContentMousePoint);
                }

                ReleaseMouseCapture();
                mouseHandlingMode = MouseHandlingModeEnum.None;
            }
        }

        /// <summary>
        /// Event raised on mouse move in the ZoomPanel.
        /// </summary>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var oldContentMousePoint = MousePosition;
            var curContentMousePoint = e.GetPosition(content);
            MousePosition = curContentMousePoint.FilterClamp(content.ActualWidth - 1, content.ActualHeight - 1);
            OnPropertyChanged(new DependencyPropertyChangedEventArgs(MousePositionProperty, oldContentMousePoint,
                curContentMousePoint));

            if (mouseHandlingMode == MouseHandlingModeEnum.Panning)
            {
                //
                // The user is left-dragging the mouse.
                // Pan the viewport by the appropriate amount.
                //
                var dragOffset = curContentMousePoint - origContentMouseDownPoint;

                ContentOffsetX -= dragOffset.X;
                ContentOffsetY -= dragOffset.Y;

                e.Handled = true;
            }
            else if (mouseHandlingMode == MouseHandlingModeEnum.Zooming)
            {
                var curZoomPanelMousePoint = e.GetPosition(this);
                var dragOffset = curZoomPanelMousePoint - origZoomPanelMouseDownPoint;
                double dragThreshold = 10;
                if (mouseButtonDown == MouseButton.Left &&
                    (Math.Abs(dragOffset.X) > dragThreshold ||
                     Math.Abs(dragOffset.Y) > dragThreshold))
                {
                    //
                    // When Shift + left-down zooming mode and the user drags beyond the drag threshold,
                    // initiate drag zooming mode where the user can drag out a rectangle to select the area
                    // to zoom in on.
                    //
                    mouseHandlingMode = MouseHandlingModeEnum.DragZooming;
                    InitDragZoomRect(origContentMouseDownPoint, curContentMousePoint);
                }
            }
            else if (mouseHandlingMode == MouseHandlingModeEnum.DragZooming)
            {
                //
                // When in drag zooming mode continously update the position of the rectangle
                // that the user is dragging out.
                //
                curContentMousePoint = e.GetPosition(this);
                SetDragZoomRect(origZoomPanelMouseDownPoint, curContentMousePoint);
            }
        }

        /// <summary>
        /// Event raised on mouse wheel moved in the ZoomPanel.
        /// </summary>
        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);

            DelayedSaveZoom750Miliseconds();
            e.Handled = true;

            if (e.Delta > 0)
                ZoomIn(e.GetPosition(content));
            else if (e.Delta < 0)
                ZoomOut(e.GetPosition(content));
        }

        /// <summary>
        /// Event raised with the double click command
        /// </summary>
        protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
        {
            base.OnMouseDoubleClick(e);

            if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                SaveZoom();
                AnimatedSnapTo(e.GetPosition(content));
            }
        }

        #region private Zoom methods

        /// <summary>
        /// Zoom the viewport out, centering on the specified point (in content coordinates).
        /// </summary>
        private void ZoomOut(Point contentZoomCenter)
        {
            ZoomAboutPoint(InternalViewportZoom * 0.90909090909, contentZoomCenter);
        }

        /// <summary>
        /// Zoom the viewport in, centering on the specified point (in content coordinates).
        /// </summary>
        private void ZoomIn(Point contentZoomCenter)
        {
            ZoomAboutPoint(InternalViewportZoom * 1.1, contentZoomCenter);
        }

        /// <summary>
        /// Initialise the rectangle that the use is dragging out.
        /// </summary>
        private void InitDragZoomRect(Point pt1, Point pt2)
        {
            partDragZoomCanvas.Visibility = Visibility.Visible;
            partDragZoomBorder.Opacity = 1;
            SetDragZoomRect(pt1, pt2);
        }

        /// <summary>
        /// Update the position and size of the rectangle that user is dragging out.
        /// </summary>
        private void SetDragZoomRect(Point pt1, Point pt2)
        {
            //
            // Update the coordinates of the rectangle that is being dragged out by the user.
            // The we offset and rescale to convert from content coordinates.
            //
            var rect = ViewportHelper.Clip(pt1, pt2, new Point(0, 0),
                new Point(partDragZoomCanvas.ActualWidth, partDragZoomCanvas.ActualHeight));
            ViewportHelper.PositionBorderOnCanvas(partDragZoomBorder, rect);
        }

        /// <summary>
        /// When the user has finished dragging out the rectangle the zoom operation is applied.
        /// </summary>
        private void ApplyDragZoomRect(Point finalContentMousePoint)
        {
            var rect = ViewportHelper.Clip(finalContentMousePoint, origContentMouseDownPoint, new Point(0, 0),
                new Point(partDragZoomCanvas.ActualWidth, partDragZoomCanvas.ActualHeight));
            AnimatedZoomTo(rect);
            // new Rect(contentX, contentY, contentWidth, contentHeight));
            FadeOutDragZoomRect();
        }

        //
        // Fade out the drag zoom rectangle.
        //
        private void FadeOutDragZoomRect()
        {
            AnimationHelper.StartAnimation(partDragZoomBorder, OpacityProperty, 0.0, 0.1,
                delegate { partDragZoomCanvas.Visibility = Visibility.Collapsed; }, UseAnimations);
        }

        #endregion

        #region Commands

        /// <summary>
        ///     Command to implement the zoom to fill 
        /// </summary>
        public ICommand FillCommand => fillCommand ?? (fillCommand = new RelayCommand(() =>
        {
            SaveZoom();
            AnimatedZoomToCentered(FillZoomValue);
            RaiseCanExecuteChanged();
        }, () => !InternalViewportZoom.IsWithinOnePercent(FillZoomValue) && FillZoomValue >= MinimumZoomClamped));

        private RelayCommand fillCommand;

        /// <summary>
        ///     Command to implement the zoom to fit 
        /// </summary>
        public ICommand FitCommand => fitCommand ?? (fitCommand = new RelayCommand(() =>
        {
            SaveZoom();
            AnimatedZoomTo(FitZoomValue);
            RaiseCanExecuteChanged();
        }, () => !InternalViewportZoom.IsWithinOnePercent(FitZoomValue) && FitZoomValue >= MinimumZoomClamped));

        private RelayCommand fitCommand;

        /// <summary>
        ///     Command to implement the zoom to a percentage where 100 (100%) is the default and 
        ///     shows the image at a zoom where 1 pixel is 1 pixel. Other percentages specified
        ///     with the command parameter. 50 (i.e. 50%) would display 4 times as much of the image
        /// </summary>
        public ICommand ZoomPercentCommand
            => zoomPercentCommand ?? (zoomPercentCommand = new RelayCommand<double>(value =>
            {
                SaveZoom();
                var adjustedValue = value == 0 ? 1 : value / 100;
                AnimatedZoomTo(adjustedValue);
                RaiseCanExecuteChanged();
            }, value =>
            {
                var adjustedValue = value == 0 ? 1 : value / 100;
                return !InternalViewportZoom.IsWithinOnePercent(adjustedValue) && adjustedValue >= MinimumZoomClamped;
            }));


        // Math.Abs(InternalViewportZoom - ((value == 0) ? 1.0 : value / 100)) > .01 * InternalViewportZoom 

        private RelayCommand<double> zoomPercentCommand;

        /// <summary>
        ///     Command to implement the zoom ratio where 1 is is the the specified minimum. 2 make the image twices the size,
        ///     and is the default. Other values are specified with the CommandParameter. 
        /// </summary>
        public ICommand ZoomRatioFromMinimumCommand
            => zoomRatioFromMinimumCommand ?? (zoomRatioFromMinimumCommand = new RelayCommand<double>(value =>
            {
                SaveZoom();
                var adjustedValue = (value == 0 ? 2 : value) * MinimumZoomClamped;
                AnimatedZoomTo(adjustedValue);
                RaiseCanExecuteChanged();
            }, value =>
            {
                var adjustedValue = (value == 0 ? 2 : value) * MinimumZoomClamped;
                return !InternalViewportZoom.IsWithinOnePercent(adjustedValue) && adjustedValue >= MinimumZoomClamped;
            }));

        private RelayCommand<double> zoomRatioFromMinimumCommand;


        /// <summary>
        ///     Command to implement the zoom out by 110% 
        /// </summary>
        public ICommand ZoomOutCommand => zoomOutCommand ?? (zoomOutCommand = new RelayCommand(() =>
             {
                 DelayedSaveZoom1500Miliseconds();
                 ZoomOut(new Point(ContentZoomFocusX, ContentZoomFocusY));
             }, () => InternalViewportZoom > MinimumZoomClamped));
        private RelayCommand zoomOutCommand;

        /// <summary>
        ///     Command to implement the zoom in by 91% 
        /// </summary>
        public ICommand ZoomInCommand => zoomInCommand ?? (zoomInCommand = new RelayCommand(() =>
            {
                DelayedSaveZoom1500Miliseconds();
                ZoomIn(new Point(ContentZoomFocusX, ContentZoomFocusY));
            }, () => InternalViewportZoom < MaximumZoom));
        private RelayCommand zoomInCommand;

        private void RaiseCanExecuteChanged()
        {
            zoomPercentCommand?.RaiseCanExecuteChanged();
            zoomOutCommand?.RaiseCanExecuteChanged();
            zoomInCommand?.RaiseCanExecuteChanged();
            fitCommand?.RaiseCanExecuteChanged();
            fillCommand?.RaiseCanExecuteChanged();
        }
        #endregion

        /// <summary>
        /// When content is renewed, set event to set the initial position as specified
        /// </summary>
        /// <param name="oldContent"></param>
        /// <param name="newContent"></param>
        protected override void OnContentChanged(object oldContent, object newContent)
        {
            base.OnContentChanged(oldContent, newContent);
            if (oldContent != null)
                ((FrameworkElement)oldContent).SizeChanged -= SetZoomAndPanInitialPosition;
            ((FrameworkElement)newContent).SizeChanged += SetZoomAndPanInitialPosition;
        }

        /// <summary>
        /// When content is renewed, set the initial position as specified
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SetZoomAndPanInitialPosition(object sender, SizeChangedEventArgs e)
        {
            switch (ZoomAndPanInitialPosition)
            {
                case ZoomPanelInitialPositionEnum.Default:
                    break;
                case ZoomPanelInitialPositionEnum.FitScreen:
                    InternalViewportZoom = FitZoomValue;
                    break;
                case ZoomPanelInitialPositionEnum.FillScreen:
                    InternalViewportZoom = FillZoomValue;
                    ContentOffsetX = (content.ActualWidth - ViewportWidth / InternalViewportZoom) / 2;
                    ContentOffsetY = (content.ActualHeight - ViewportHeight / InternalViewportZoom) / 2;
                    break;
                case ZoomPanelInitialPositionEnum.OneHundredPercentCentered:
                    InternalViewportZoom = 1.0;
                    ContentOffsetX = (content.ActualWidth - ViewportWidth) / 2;
                    ContentOffsetY = (content.ActualHeight - ViewportHeight) / 2;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}