using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LabelForge.ImageControl;

public sealed class ImageViewport : ScrollViewer
{
    private readonly ScaleTransform scaleTransform = new(1, 1);
    private Point? panStart;
    private Vector scrollOrigin;
    private bool isSpacePressed;

    public ImageViewport()
    {
        ClipToBounds = true;
        Background = Brushes.Transparent;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        CanContentScroll = false;

        PreviewMouseWheel += OnPreviewMouseWheel;
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Space) { isSpacePressed = true; Cursor = Cursors.Hand; e.Handled = true; } };
        PreviewKeyUp += (_, e) => { if (e.Key == Key.Space) { isSpacePressed = false; Cursor = Cursors.Arrow; e.Handled = true; } };
    }

    public static readonly DependencyProperty ZoomProperty =
        DependencyProperty.Register(nameof(Zoom), typeof(double), typeof(ImageViewport),
            new PropertyMetadata(1.0, OnZoomChanged));

    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, Math.Clamp(value, 0.05, 50));
    }

    public Point ImageToViewport(Point imagePoint) =>
        new(imagePoint.X * Zoom - HorizontalOffset, imagePoint.Y * Zoom - VerticalOffset);

    public Point ViewportToImage(Point viewportPoint) =>
        new((viewportPoint.X + HorizontalOffset) / Zoom, (viewportPoint.Y + VerticalOffset) / Zoom);

    public void ResetView()
    {
        Zoom = 1;
        ApplyTransforms();
        ScrollToHorizontalOffset(0);
        ScrollToVerticalOffset(0);
    }

    public void FitTo(Size contentSize, Size viewportSize)
    {
        if (contentSize.Width <= 0 || contentSize.Height <= 0 || viewportSize.Width <= 0 || viewportSize.Height <= 0)
        {
            return;
        }

        Zoom = Math.Min(viewportSize.Width / contentSize.Width, viewportSize.Height / contentSize.Height);
        ApplyTransforms();
        ScrollToHorizontalOffset(Math.Max(0, (ExtentWidth - ViewportWidth) / 2));
        ScrollToVerticalOffset(Math.Max(0, (ExtentHeight - ViewportHeight) / 2));
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);

        if (oldContent is FrameworkElement oldElement)
        {
            oldElement.RenderTransform = Transform.Identity;
        }

        if (newContent is FrameworkElement newElement)
        {
            newElement.LayoutTransform = scaleTransform;
            newElement.SnapsToDevicePixels = true;
            RenderOptions.SetBitmapScalingMode(newElement, BitmapScalingMode.HighQuality);
            ApplyTransforms();
        }
    }

    private static void OnZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ImageViewport)d).ApplyTransforms();
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var mouse = e.GetPosition(this);
        var before = ViewportToImage(mouse);
        Zoom *= e.Delta > 0 ? 1.15 : 1 / 1.15;
        ApplyTransforms();
        UpdateLayout();
        ScrollToHorizontalOffset(before.X * Zoom - mouse.X);
        ScrollToVerticalOffset(before.Y * Zoom - mouse.Y);
        if (Content is FrameworkElement element)
            RenderOptions.SetBitmapScalingMode(element, Zoom >= 2 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        e.Handled = true;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.MiddleButton == MouseButtonState.Pressed || (isSpacePressed && e.ChangedButton == MouseButton.Left))
        {
            panStart = e.GetPosition(this);
            scrollOrigin = new Vector(HorizontalOffset, VerticalOffset);
            CaptureMouse();
            e.Handled = true;
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (panStart is null || !IsMouseCaptured)
        {
            return;
        }

        var current = e.GetPosition(this);
        ScrollToHorizontalOffset(scrollOrigin.X + panStart.Value.X - current.X);
        ScrollToVerticalOffset(scrollOrigin.Y + panStart.Value.Y - current.Y);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (panStart is not null)
        {
            panStart = null;
            ReleaseMouseCapture();
        }
    }

    private void ApplyTransforms()
    {
        scaleTransform.ScaleX = Zoom;
        scaleTransform.ScaleY = Zoom;
    }
}
