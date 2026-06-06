using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using LabelForge.ImageControl;

namespace LabelForge.App.Controls;

public partial class MiniMapOverlay : UserControl
{
    private ImageViewport? viewport;
    private double imgW, imgH;

    public MiniMapOverlay()
    {
        InitializeComponent();
        MouseLeftButtonDown += OnMiniMapClick;
        MouseMove += OnMiniMapDrag;
    }

    public void SetImage(BitmapSource? bitmap)
    {
        ThumbImage.Source = bitmap;
        imgW = bitmap?.PixelWidth  ?? 0;
        imgH = bitmap?.PixelHeight ?? 0;
        Visibility = bitmap is not null ? Visibility.Visible : Visibility.Collapsed;
        UpdateViewRect();
    }

    public void SetViewport(ImageViewport vp)
    {
        if (viewport is not null)
            viewport.ScrollChanged -= OnScrollChanged;

        viewport = vp;
        viewport.ScrollChanged += OnScrollChanged;
        UpdateViewRect();
    }

    public void Refresh() => UpdateViewRect();

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateViewRect();

    private void UpdateViewRect()
    {
        if (viewport is null || imgW <= 0 || imgH <= 0)
        {
            ViewRect.Visibility = Visibility.Collapsed;
            return;
        }

        // Use the control's own size (Width/Height set in XAML) as fallback
        double mapW = ViewCanvas.ActualWidth  > 0 ? ViewCanvas.ActualWidth  : Width;
        double mapH = ViewCanvas.ActualHeight > 0 ? ViewCanvas.ActualHeight : Height;
        if (mapW <= 0 || mapH <= 0)
        {
            ViewRect.Visibility = Visibility.Collapsed;
            return;
        }

        // Scale from image coords to minimap coords (letterbox-aware)
        double scaleX = mapW / imgW;
        double scaleY = mapH / imgH;
        double scale  = Math.Min(scaleX, scaleY);
        double offsetX = (mapW - imgW * scale) / 2;
        double offsetY = (mapH - imgH * scale) / 2;

        double zoom = Math.Max(viewport.Zoom, 0.001);
        double visW = viewport.ViewportWidth  / zoom;
        double visH = viewport.ViewportHeight / zoom;
        double sX   = viewport.HorizontalOffset / zoom;
        double sY   = viewport.VerticalOffset   / zoom;

        double rx = sX * scale + offsetX;
        double ry = sY * scale + offsetY;
        double rw = Math.Min(visW * scale, imgW * scale);
        double rh = Math.Min(visH * scale, imgH * scale);

        Canvas.SetLeft(ViewRect, rx);
        Canvas.SetTop(ViewRect,  ry);
        ViewRect.Width  = Math.Max(4, rw);
        ViewRect.Height = Math.Max(4, rh);
        ViewRect.Visibility = Visibility.Visible;
    }

    private void OnMiniMapClick(object sender, MouseButtonEventArgs e)
    {
        if (viewport is null || imgW <= 0) return;
        PanTo(e.GetPosition(ViewCanvas));
        CaptureMouse();
    }

    private void OnMiniMapDrag(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            PanTo(e.GetPosition(ViewCanvas));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        base.OnMouseLeftButtonUp(e);
    }

    private void PanTo(Point mapPoint)
    {
        if (viewport is null || imgW <= 0) return;

        double mapW = ViewCanvas.ActualWidth;
        double mapH = ViewCanvas.ActualHeight;
        double scale  = Math.Min(mapW / imgW, mapH / imgH);
        double offsetX = (mapW - imgW * scale) / 2;
        double offsetY = (mapH - imgH * scale) / 2;

        double zoom = viewport.Zoom;
        double imgX = (mapPoint.X - offsetX) / scale;
        double imgY = (mapPoint.Y - offsetY) / scale;

        viewport.ScrollToHorizontalOffset((imgX - viewport.ViewportWidth  / zoom / 2) * zoom);
        viewport.ScrollToVerticalOffset  ((imgY - viewport.ViewportHeight / zoom / 2) * zoom);
    }
}
