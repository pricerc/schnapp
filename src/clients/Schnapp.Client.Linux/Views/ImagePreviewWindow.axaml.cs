namespace Schnapp.Client.Linux.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;

using System;
using System.IO;

/// <summary>
/// Modal resizable image preview with zoom, fit, Ctrl+wheel zoom, and drag-to-pan.
/// </summary>
public partial class ImagePreviewWindow : Window
{
    private Bitmap? _bitmap;
    private double _zoom = 1.0;
    private const double MinZoom = 0.05;
    private const double MaxZoom = 8.0;

    private bool _panning;
    private Point _panStart;
    private Vector _offsetStart;

    public ImagePreviewWindow()
    {
        InitializeComponent();
        PointerWheelChanged += OnWheel;
        Scroller.SizeChanged += (_, _) =>
        {
            // Keep fit correct if user had fitted and resizes the window — only when near fit
        };
    }

    /// <summary>
    /// Shows a modal preview of the given image bytes.
    /// </summary>
    /// <param name="owner">Owner window.</param>
    /// <param name="imageBytes">Full image payload from the spool.</param>
    /// <param name="title">Window title (e.g. timestamp).</param>
    /// <param name="subtitle">Optional detail line (journal, etc.).</param>
    public static async System.Threading.Tasks.Task ShowPreviewAsync(
        Window owner,
        byte[] imageBytes,
        string title,
        string? subtitle = null)
    {
        var w = new ImagePreviewWindow
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Preview" : title
        };
        w.InfoLabel.Text = subtitle ?? "";
        w.LoadImage(imageBytes);
        await w.ShowDialog(owner);
    }

    private void LoadImage(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            _bitmap = new Bitmap(ms);
            PreviewImage.Source = _bitmap;
            // Start fitted to the viewport after layout
            Opened += (_, _) => ZoomFit();
            UpdateZoomUi();
        }
        catch (Exception ex)
        {
            InfoLabel.Text = "Could not load image: " + ex.Message;
        }
    }

    private void OnZoomIn(object? sender, RoutedEventArgs e) => SetZoom(_zoom * 1.25, preserveCenter: true);
    private void OnZoomOut(object? sender, RoutedEventArgs e) => SetZoom(_zoom / 1.25, preserveCenter: true);
    private void OnZoomActual(object? sender, RoutedEventArgs e) => SetZoom(1.0, preserveCenter: true);

    private void OnZoomFit(object? sender, RoutedEventArgs e) => ZoomFit();

    /// <summary>
    /// Scales the image so the entire frame is visible (letterbox), no cropping.
    /// </summary>
    private void ZoomFit()
    {
        if (_bitmap == null)
        {
            return;
        }


        var viewport = GetViewportSize();
        var imgW = Math.Max(1, _bitmap.PixelSize.Width);
        var imgH = Math.Max(1, _bitmap.PixelSize.Height);

        var sx = viewport.Width / imgW;
        var sy = viewport.Height / imgH;
        // Fit entire image inside viewport (min scale) — never crop
        var fit = Math.Min(sx, sy);
        SetZoom(fit, preserveCenter: false);
        // Center after fit
        Scroller.Offset = new Vector(0, 0);
    }

    private Size GetViewportSize()
    {
        var vw = Math.Max(1, Scroller.Viewport.Width);
        var vh = Math.Max(1, Scroller.Viewport.Height);
        if (vw < 32 || vh < 32)
        {
            vw = Math.Max(1, Scroller.Bounds.Width - 8);
            vh = Math.Max(1, Scroller.Bounds.Height - 8);
        }
        if (vw < 32 || vh < 32)
        {
            vw = Math.Max(1, Bounds.Width - 40);
            vh = Math.Max(1, Bounds.Height - 80);
        }
        return new Size(vw, vh);
    }

    /// <summary>
    /// Applies zoom by sizing the Image control; Stretch=Fill scales the bitmap into that box.
    /// </summary>
    private void SetZoom(double zoom, bool preserveCenter)
    {
        if (_bitmap == null)
        {
            return;
        }


        var oldZoom = _zoom;
        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);

        Point? centerBefore = null;
        if (preserveCenter && oldZoom > 0)
        {
            var vp = Scroller.Viewport;
            centerBefore = new Point(
                Scroller.Offset.X + vp.Width / 2,
                Scroller.Offset.Y + vp.Height / 2);
        }

        var w = _bitmap.PixelSize.Width * _zoom;
        var h = _bitmap.PixelSize.Height * _zoom;
        PreviewImage.Width = w;
        PreviewImage.Height = h;
        ImageHost.Width = w;
        ImageHost.Height = h;

        if (centerBefore is { } c && oldZoom > 0)
        {
            var scale = _zoom / oldZoom;
            var newCenter = new Point(c.X * scale, c.Y * scale);
            var vp = Scroller.Viewport;
            Scroller.Offset = new Vector(
                Math.Max(0, newCenter.X - vp.Width / 2),
                Math.Max(0, newCenter.Y - vp.Height / 2));
        }

        UpdateZoomUi();
    }

    private void UpdateZoomUi()
    {
        ZoomLabel.Text = $"{_zoom * 100:0}%";
        if (_bitmap != null)
        {
            Cursor = new Cursor(StandardCursorType.SizeAll);
        }

    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            SetZoom(e.Delta.Y > 0 ? _zoom * 1.15 : _zoom / 1.15, preserveCenter: true);
            e.Handled = true;
        }
        // without Ctrl, ScrollViewer pans/scrolls normally
    }

    private void OnPanPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_bitmap == null)
        {
            return;
        }


        var props = e.GetCurrentPoint(Scroller).Properties;
        // Left button or middle button drag to pan
        if (!props.IsLeftButtonPressed && !props.IsMiddleButtonPressed)
        {
            return;
        }


        _panning = true;
        _panStart = e.GetPosition(Scroller);
        _offsetStart = Scroller.Offset;
        e.Pointer.Capture(Scroller);
        e.Handled = true;
    }

    private void OnPanPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_panning)
        {
            return;
        }


        var pos = e.GetPosition(Scroller);
        var delta = pos - _panStart;
        // Drag content with the pointer (grab-hand style)
        var next = new Vector(
            Math.Max(0, _offsetStart.X - delta.X),
            Math.Max(0, _offsetStart.Y - delta.Y));
        Scroller.Offset = next;
        e.Handled = true;
    }

    private void OnPanPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_panning)
        {
            return;
        }


        EndPan(e.Pointer);
        e.Handled = true;
    }

    private void OnPanPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _panning = false;
    }

    private void EndPan(IPointer pointer)
    {
        _panning = false;
        pointer.Capture(null);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _bitmap?.Dispose();
        _bitmap = null;
        base.OnClosed(e);
    }
}
