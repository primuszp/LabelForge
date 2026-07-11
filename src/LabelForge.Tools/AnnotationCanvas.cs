using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LabelForge.Core;

namespace LabelForge.Tools;

public sealed class AnnotationCanvas : Canvas
{
    private Annotation? activeAnnotation;
    private Point2D? dragStart;
    private Point2D? lastMovePoint;
    private readonly ContextMenu contextMenu = new();
    private readonly Stack<List<Annotation>> undoStack = new();
    private readonly Stack<List<Annotation>> redoStack = new();
    private bool isDraggingSelection;
    private bool isPolylineDrawing;
    private bool isPolygonDrawing;
    private Annotation? hoveredAnnotation;
    private Annotation? handleAnnotation;
    private int handleIndex = -1;
    private HandleKind handleKind = HandleKind.Vertex;
    private Point2D? pendingInsertedVertex;
    private Point2D? lastDraftPoint;
    private Point2D? circleCenter;
    private Point2D? circleRadiusPoint;
    private Rect? selectionBox;
    private const double DefaultDraftPointSpacingScreen = 12;

    public AnnotationCanvas()
    {
        Background = Brushes.Transparent;
        Focusable = true;
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        KeyDown += OnKeyDown;
        CreateContextMenu();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyContextMenuTheme();
    }

    private void ApplyContextMenuTheme()
    {
        var appRes = Application.Current?.Resources;
        if (appRes is null) return;

        if (appRes[typeof(ContextMenu)] is Style ctxStyle)
            contextMenu.Style = ctxStyle;

        if (appRes[typeof(MenuItem)] is Style menuItemStyle)
        {
            foreach (var item in contextMenu.Items.OfType<MenuItem>())
                item.Style = menuItemStyle;
        }
    }

    public event EventHandler<AnnotationTool>? ActiveToolChanged;
    public event EventHandler? SelectionChanged;
    public event EventHandler<string>? InteractionHintChanged;

    /// <summary>
    /// Optional lookup: given a label name, returns per-category visual overrides.
    /// Returning null means "use defaults". Set by the host window.
    /// </summary>
    public Func<string, LabelVisualStyle?>? LabelStyleProvider { get; set; }

    public static readonly DependencyProperty DocumentProperty =
        DependencyProperty.Register(nameof(Document), typeof(ImageDocument), typeof(AnnotationCanvas),
            new FrameworkPropertyMetadata(null, OnDocumentChanged));

    public ImageDocument? Document
    {
        get => (ImageDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public static readonly DependencyProperty ActiveToolProperty =
        DependencyProperty.Register(nameof(ActiveTool), typeof(AnnotationTool), typeof(AnnotationCanvas),
            new FrameworkPropertyMetadata(AnnotationTool.Select, OnActiveToolChanged));

    public AnnotationTool ActiveTool
    {
        get => (AnnotationTool)GetValue(ActiveToolProperty);
        set => SetValue(ActiveToolProperty, value);
    }

    private static void OnActiveToolChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var canvas = (AnnotationCanvas)d;
        if (canvas.isPolylineDrawing || canvas.isPolygonDrawing)
        {
            // Tool switched externally while drawing — cancel the in-progress polyline
            if (canvas.activeAnnotation is not null)
                canvas.Document?.Annotations.Remove(canvas.activeAnnotation);
            canvas.isPolylineDrawing = false;
            canvas.isPolygonDrawing = false;
            canvas.activeAnnotation = null;
            canvas.ReleaseMouseCapture();
            canvas.InvalidateVisual();
        }

        canvas.ActiveToolChanged?.Invoke(d, (AnnotationTool)e.NewValue);
    }

    public static readonly DependencyProperty CurrentLabelProperty =
        DependencyProperty.Register(nameof(CurrentLabel), typeof(string), typeof(AnnotationCanvas),
            new FrameworkPropertyMetadata("object"));

    public string CurrentLabel
    {
        get => (string)GetValue(CurrentLabelProperty);
        set => SetValue(CurrentLabelProperty, value);
    }

    public static readonly DependencyProperty ViewportZoomProperty =
        DependencyProperty.Register(nameof(ViewportZoom), typeof(double), typeof(AnnotationCanvas),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double ViewportZoom
    {
        get => (double)GetValue(ViewportZoomProperty);
        set => SetValue(ViewportZoomProperty, value);
    }

    public static readonly DependencyProperty CurrentColorProperty =
        DependencyProperty.Register(nameof(CurrentColor), typeof(string), typeof(AnnotationCanvas),
            new FrameworkPropertyMetadata("#22c55e"));

    public string CurrentColor
    {
        get => (string)GetValue(CurrentColorProperty);
        set => SetValue(CurrentColorProperty, value);
    }

    public static readonly DependencyProperty DraftPointSpacingProperty =
        DependencyProperty.Register(nameof(DraftPointSpacing), typeof(double), typeof(AnnotationCanvas),
            new FrameworkPropertyMetadata(DefaultDraftPointSpacingScreen));

    public double DraftPointSpacing
    {
        get => (double)GetValue(DraftPointSpacingProperty);
        set => SetValue(DraftPointSpacingProperty, value);
    }

    public void DeleteSelected()
    {
        if (Document is null)
        {
            return;
        }

        var selected = Document.Annotations.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        SaveUndoState();
        foreach (var annotation in selected)
        {
            Document.Annotations.Remove(annotation);
        }

        Document.IsDirty = true;
        InvalidateVisual();
    }

    public void SelectAll()
    {
        if (Document is null)
        {
            return;
        }

        foreach (var annotation in Document.Annotations)
        {
            annotation.IsSelected = true;
        }

        InvalidateVisual();
    }

    public void UnselectAll()
    {
        if (Document is null)
        {
            return;
        }

        foreach (var annotation in Document.Annotations)
        {
            annotation.IsSelected = false;
        }

        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteAll()
    {
        if (Document is null || Document.Annotations.Count == 0)
        {
            return;
        }

        SaveUndoState();
        Document.Annotations.Clear();
        activeAnnotation = null;
        Document.IsDirty = true;
        InvalidateVisual();
    }

    public void MoveSelectedToFront()
    {
        MoveSelected(toFront: true);
    }

    public void MoveSelectedToBack()
    {
        MoveSelected(toFront: false);
    }

    public void Undo()
    {
        if (Document is null || undoStack.Count == 0)
        {
            return;
        }

        redoStack.Push(CloneAnnotations(Document.Annotations));
        RestoreAnnotations(undoStack.Pop());
    }

    public void Redo()
    {
        if (Document is null || redoStack.Count == 0)
        {
            return;
        }

        undoStack.Push(CloneAnnotations(Document.Annotations));
        RestoreAnnotations(redoStack.Pop());
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (Document is null)
        {
            return;
        }

        foreach (var annotation in Document.Annotations.Where(a => a.IsVisible))
        {
            var style = LabelStyleProvider?.Invoke(annotation.Label);
            if (style is { CategoryVisible: false }) continue;

            DrawAnnotation(
                dc,
                annotation,
                ViewportZoom,
                annotation == activeAnnotation,
                annotation == handleAnnotation && handleKind == HandleKind.InsertMidpoint ? pendingInsertedVertex : null,
                handleIndex,
                annotation == activeAnnotation && annotation.Shape is EllipseShape ? circleCenter : null,
                annotation == activeAnnotation && annotation.Shape is EllipseShape ? circleRadiusPoint : null,
                style?.FillOpacity ?? 0.22,
                style?.StrokeThickness ?? 2.0,
                annotation == hoveredAnnotation);
        }

        if (selectionBox is Rect box)
        {
            var scale = Math.Max(ViewportZoom, 0.001);
            dc.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(46, 0, 114, 255)),
                new Pen(Brushes.White, 1.25 / scale) { DashStyle = DashStyles.Dash },
                box);
        }
    }

    private static void OnDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var canvas = (AnnotationCanvas)d;
        if (e.OldValue is ImageDocument oldDocument)
        {
            oldDocument.Annotations.CollectionChanged -= ((AnnotationCanvas)d).OnAnnotationsChanged;
        }

        if (e.NewValue is ImageDocument newDocument)
        {
            newDocument.Annotations.CollectionChanged += ((AnnotationCanvas)d).OnAnnotationsChanged;
        }

        canvas.undoStack.Clear();
        canvas.redoStack.Clear();
        canvas.activeAnnotation = null;
        canvas.isPolylineDrawing = false;
        canvas.isPolygonDrawing = false;
        canvas.hoveredAnnotation = null;
        canvas.InvalidateVisual();
    }

    private void OnAnnotationsChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        if (Document?.Image is null)
        {
            return;
        }

        var point = ClampToImage(ToPoint2D(e.GetPosition(this)));

        if (e.ChangedButton == MouseButton.Right)
        {
            if (isPolylineDrawing || isPolygonDrawing)
            {
                FinishActiveShape();
                e.Handled = true;
                return;
            }

            var handleHit = HitTestHandle(point);
            if (handleHit is not null)
            {
                if (handleHit.Value.Kind == HandleKind.Vertex)
                {
                    DeleteHandle(handleHit.Value.Annotation, handleHit.Value.HandleIndex);
                }

                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                SelectAt(point, false);
                OpenContextMenu();
            }
            e.Handled = true;
            return;
        }

        if (ActiveTool == AnnotationTool.Select)
        {
            var handleHit = HitTestHandle(point);
            if (handleHit is not null)
            {
                handleAnnotation = handleHit.Value.Annotation;
                handleIndex = handleHit.Value.HandleIndex;
                handleKind = handleHit.Value.Kind;
                if (handleKind != HandleKind.InsertMidpoint)
                {
                    SaveUndoState();
                }

                foreach (var annotation in Document.Annotations)
                {
                    annotation.IsSelected = annotation == handleAnnotation;
                }

                CaptureMouse();
                InvalidateVisual();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            var selectedBefore = Document.Annotations.Any(a => a.IsSelected);
            SelectAt(point, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            dragStart = point;
            lastMovePoint = point;
            isDraggingSelection = Document.Annotations.Any(a => a.IsSelected && AnnotationGeometry.Contains(a, point));
            if (isDraggingSelection)
            {
                SaveUndoState();
            }
            else
            {
                if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || !selectedBefore)
                {
                    UnselectAll();
                }

                selectionBox = new Rect(new Point(point.X, point.Y), new Size(0, 0));
            }

            CaptureMouse();
            return;
        }

        // In any drawing tool: if user clicks on a handle of a selected annotation → hijack to select+move
        // Skip this when actively drawing (isPolylineDrawing) because the preview vertex sits exactly
        // under the cursor and would always be detected as a handle hit, cancelling the in-progress draw.
        if (!isPolylineDrawing && !isPolygonDrawing)
        {
            var handleHit = HitTestHandle(point);
            if (handleHit is not null)
            {
                ActiveTool = AnnotationTool.Select;
                handleAnnotation = handleHit.Value.Annotation;
                handleIndex = handleHit.Value.HandleIndex;
                handleKind = handleHit.Value.Kind;
                if (handleKind != HandleKind.InsertMidpoint)
                    SaveUndoState();
                foreach (var annotation in Document.Annotations)
                    annotation.IsSelected = annotation == handleAnnotation;
                CaptureMouse();
                InvalidateVisual();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                return;
            }
        }

        if (ActiveTool == AnnotationTool.Polygon)
        {
            // WPF reports the user's quick second click as ClickCount=2 as well.
            // Only treat a double-click as finish when at least two fixed vertices
            // already exist, and commit the clicked endpoint before closing.
            if (e.ClickCount == 2 && isPolygonDrawing &&
                activeAnnotation?.Shape is PolygonShape finishingPolygon &&
                finishingPolygon.Vertices.Count >= 3)
            {
                FinishPolygon();
                e.Handled = true;
                return;
            }

            if (!isPolygonDrawing)
            {
                SaveUndoState();
                activeAnnotation = CreateAnnotation(new PolygonShape());
                var polygon = (PolygonShape)activeAnnotation.Shape;
                polygon.Vertices.Add(point);
                polygon.Vertices.Add(point);
                Document.Annotations.Add(activeAnnotation);
                isPolygonDrawing = true;
                CaptureMouse();
            }
            else if (activeAnnotation?.Shape is PolygonShape polygon)
            {
                polygon.Vertices[^1] = point;
                polygon.Vertices.Add(point);
            }
            Document.IsDirty = true;
            InvalidateVisual();
            return;
        }

        if (ActiveTool == AnnotationTool.FreehandPolygon)
        {
            SaveUndoState();
            activeAnnotation = CreateAnnotation(new PolygonShape());
            var polygon = (PolygonShape)activeAnnotation.Shape;
            polygon.Vertices.Add(point);
            polygon.Vertices.Add(point);
            Document.Annotations.Add(activeAnnotation);
            lastDraftPoint = point;
            CaptureMouse();
            Document.IsDirty = true;
            InvalidateVisual();
            return;
        }

        if (ActiveTool == AnnotationTool.Polyline)
        {
            if (e.ClickCount == 2 && isPolylineDrawing)
            {
                FinishPolyline();
                e.Handled = true;
                return;
            }

            if (!isPolylineDrawing)
            {
                SaveUndoState();
                activeAnnotation = CreateAnnotation(new LineShape());
                var line = (LineShape)activeAnnotation.Shape;
                line.Vertices.Add(point); // fixed anchor
                line.Vertices.Add(point); // floating preview
                Document.Annotations.Add(activeAnnotation);
                isPolylineDrawing = true;
                CaptureMouse();
                Document.IsDirty = true;
                InvalidateVisual();
            }
            else if (activeAnnotation?.Shape is LineShape existingLine)
            {
                existingLine.Vertices[^1] = point; // fix current floating vertex
                existingLine.Vertices.Add(point);  // new floating vertex
                Document.IsDirty = true;
                InvalidateVisual();
            }

            return;
        }

        if (ActiveTool == AnnotationTool.Point)
        {
            SaveUndoState();
            AddAnnotation(new PointShape { Point = point });
            ActiveTool = AnnotationTool.Select;
            return;
        }

        if (ActiveTool == AnnotationTool.Rectangle || ActiveTool == AnnotationTool.Circle)
        {
            SaveUndoState();
            activeAnnotation = CreateAnnotation(ActiveTool == AnnotationTool.Rectangle
                ? new RectangleShape(point, point)
                : new EllipseShape(point, point));
            Document.Annotations.Add(activeAnnotation);
            dragStart = point;
            circleCenter = ActiveTool == AnnotationTool.Circle ? point : null;
            circleRadiusPoint = ActiveTool == AnnotationTool.Circle ? point : null;
            CaptureMouse();
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (Document is null)
        {
            return;
        }

        var point = ClampToImage(ToPoint2D(e.GetPosition(this)));

        if (ActiveTool == AnnotationTool.Polygon && isPolygonDrawing && activeAnnotation?.Shape is PolygonShape clickPolygon)
        {
            clickPolygon.Vertices[^1] = ClampToImage(point);
            InteractionHintChanged?.Invoke(this, $"Polygon • {Math.Max(1, clickPolygon.Vertices.Count - 1)} vertices • Enter to finish");
            InvalidateVisual();
            return;
        }

        if (ActiveTool == AnnotationTool.Polyline && isPolylineDrawing && activeAnnotation?.Shape is LineShape draftLine)
        {
            draftLine.Vertices[^1] = point; // update floating preview vertex
            InvalidateVisual();
            return;
        }

        if (ActiveTool == AnnotationTool.FreehandPolygon &&
            activeAnnotation?.Shape is PolygonShape draftPolygon &&
            e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateDraftPolygon(draftPolygon, point);
            Document.IsDirty = true;
            InvalidateVisual();
            InteractionHintChanged?.Invoke(this, $"Freehand polygon • {draftPolygon.Vertices.Count} vertices");
            return;
        }

        if (ActiveTool == AnnotationTool.Select && e.LeftButton == MouseButtonState.Released)
        {
            var hoverHandle = HitTestHandle(point);
            if (hoverHandle is not null)
            {
                hoveredAnnotation = hoverHandle.Value.Annotation;
                Cursor = CursorForHandle(hoverHandle.Value.Annotation.Shape, hoverHandle.Value.HandleIndex,
                    hoverHandle.Value.Kind);
                InvalidateVisual();
                return;
            }

            var hover = Document.Annotations.LastOrDefault(a => a.IsVisible && AnnotationGeometry.Contains(a, point, 6 / Math.Max(ViewportZoom, .001)));
            if (!ReferenceEquals(hoveredAnnotation, hover))
            {
                hoveredAnnotation = hover;
                Cursor = hover is null ? Cursors.Arrow : Cursors.SizeAll;
                InvalidateVisual();
            }
        }

        if (ActiveTool == AnnotationTool.Select &&
            handleAnnotation is not null &&
            handleIndex >= 0 &&
            e.LeftButton == MouseButtonState.Pressed)
        {
            if (handleKind == HandleKind.InsertMidpoint)
            {
                pendingInsertedVertex = point;
            }
            else
            {
                MoveHandleTo(handleAnnotation, handleIndex, point);
            }

            Document.IsDirty = true;
            InvalidateVisual();
            return;
        }

        if ((ActiveTool == AnnotationTool.Rectangle || ActiveTool == AnnotationTool.Circle) &&
            activeAnnotation is not null &&
            dragStart is Point2D start &&
            e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateBoxShape(start, point);
            Document.IsDirty = true;
            InvalidateVisual();
            return;
        }

        if (ActiveTool == AnnotationTool.Select &&
            lastMovePoint is Point2D previous &&
            e.LeftButton == MouseButtonState.Pressed)
        {
            if (selectionBox is not null && dragStart is Point2D selectionStart)
            {
                selectionBox = NormalizeRect(selectionStart, point);
                SelectInside(selectionBox.Value, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                InvalidateVisual();
                return;
            }

            var selected = Document.Annotations.Where(a => a.IsSelected).ToList();
            if (selected.Count == 0)
            {
                return;
            }

            foreach (var annotation in selected)
            {
                AnnotationGeometry.Move(annotation, point.X - previous.X, point.Y - previous.Y);
            }

            lastMovePoint = point;
            Document.IsDirty = true;
            InvalidateVisual();
        }
    }

    private static Cursor CursorForHandle(AnnotationShape shape, int index, HandleKind kind)
    {
        if (kind == HandleKind.InsertMidpoint) return Cursors.Cross;
        if (shape is RectangleShape)
        {
            return index switch
            {
                0 or 4 => Cursors.SizeNWSE,
                2 or 6 => Cursors.SizeNESW,
                1 or 5 => Cursors.SizeNS,
                3 or 7 => Cursors.SizeWE,
                _ => Cursors.Cross
            };
        }
        return shape is EllipseShape ? Cursors.SizeAll : Cursors.Cross;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if ((ActiveTool == AnnotationTool.Rectangle || ActiveTool == AnnotationTool.Circle || ActiveTool == AnnotationTool.FreehandPolygon) && activeAnnotation is not null)
        {
            if (!IsValidShape(activeAnnotation.Shape))
                Document?.Annotations.Remove(activeAnnotation);
            activeAnnotation = null;
            dragStart = null;
            circleCenter = null;
            circleRadiusPoint = null;
            lastDraftPoint = null;
            ReleaseMouseCapture();
            ActiveTool = AnnotationTool.Select;
        }

        if (ActiveTool == AnnotationTool.Select)
        {
            dragStart = null;
            lastMovePoint = null;
            isDraggingSelection = false;
            if (handleAnnotation is not null && handleKind == HandleKind.InsertMidpoint && pendingInsertedVertex is Point2D insertedVertex)
            {
                SaveUndoState();
                InsertVertexAtMidpoint(handleAnnotation, handleIndex, insertedVertex);
                Document!.IsDirty = true;
            }

            handleAnnotation = null;
            handleIndex = -1;
            handleKind = HandleKind.Vertex;
            pendingInsertedVertex = null;
            selectionBox = null;
            ReleaseMouseCapture();
            InvalidateVisual();
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            DeleteSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (isPolylineDrawing || isPolygonDrawing)
            {
                if (activeAnnotation is not null)
                    Document?.Annotations.Remove(activeAnnotation);
                isPolylineDrawing = false;
                isPolygonDrawing = false;
                activeAnnotation = null;
                ReleaseMouseCapture();
                InvalidateVisual();
            }
            else
            {
                activeAnnotation = null;
                InvalidateVisual();
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Return && (isPolylineDrawing || isPolygonDrawing))
        {
            FinishActiveShape();
            e.Handled = true;
        }
        else if (e.Key == Key.Back && (isPolylineDrawing || isPolygonDrawing))
        {
            RemoveLastDraftVertex();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.D)
        {
            DuplicateSelected();
            e.Handled = true;
        }
        else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            NudgeSelected(e.Key, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
        {
            Undo();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y)
        {
            Redo();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = TrySetToolFromKey(e.Key);
        }
    }

    private bool TrySetToolFromKey(Key key)
    {
        switch (key)
        {
            case Key.V:
                ActiveTool = AnnotationTool.Select;
                return true;
            case Key.R:
                ActiveTool = AnnotationTool.Rectangle;
                return true;
            case Key.E:
            case Key.C:
                ActiveTool = AnnotationTool.Circle;
                return true;
            case Key.P:
                ActiveTool = AnnotationTool.Polygon;
                return true;
            case Key.B:
                ActiveTool = AnnotationTool.FreehandPolygon;
                return true;
            case Key.L:
                ActiveTool = AnnotationTool.Polyline;
                return true;
            case Key.O:
                ActiveTool = AnnotationTool.Point;
                return true;
            default:
                return false;
        }
    }

    private void SelectAt(Point2D point, bool additive)
    {
        if (Document is null)
        {
            return;
        }

        var hit = Document.Annotations.Reverse().FirstOrDefault(a => a.IsVisible && AnnotationGeometry.Contains(a, point));
        if (!additive)
        {
            foreach (var annotation in Document.Annotations)
            {
                annotation.IsSelected = annotation == hit;
            }
        }
        else if (hit is not null)
        {
            hit.IsSelected = true;
        }

        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SelectInside(Rect box, bool additive)
    {
        if (Document is null)
        {
            return;
        }

        foreach (var annotation in Document.Annotations.Where(a => a.IsVisible))
        {
            var intersects = GetHandlePoints(annotation.Shape).Any(p => box.Contains(new Point(p.X, p.Y))) ||
                             annotation.Shape.Points.Any(p => box.Contains(new Point(p.X, p.Y)));
            annotation.IsSelected = additive ? annotation.IsSelected || intersects : intersects;
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Rect NormalizeRect(Point2D start, Point2D end) =>
        new(new Point(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y)),
            new Point(Math.Max(start.X, end.X), Math.Max(start.Y, end.Y)));

    private void UpdateBoxShape(Point2D start, Point2D end)
    {
        switch (activeAnnotation?.Shape)
        {
            case RectangleShape rectangle:
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    var size = Math.Max(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
                    end = new Point2D(start.X + Math.Sign(end.X - start.X) * size,
                        start.Y + Math.Sign(end.Y - start.Y) * size);
                }
                var updatedRectangle = new RectangleShape(start, end);
                rectangle.X = updatedRectangle.X;
                rectangle.Y = updatedRectangle.Y;
                rectangle.Width = updatedRectangle.Width;
                rectangle.Height = updatedRectangle.Height;
                break;
            case EllipseShape ellipse:
                if (ActiveTool == AnnotationTool.Circle)
                {
                    circleRadiusPoint = end;
                }

                var updatedEllipse = ActiveTool == AnnotationTool.Circle ? CreateCircleFromCenter(start, end) : new EllipseShape(start, end);
                ellipse.X = updatedEllipse.X;
                ellipse.Y = updatedEllipse.Y;
                ellipse.Width = updatedEllipse.Width;
                ellipse.Height = updatedEllipse.Height;
                ellipse.RadiusPoint = updatedEllipse.RadiusPoint;
                break;
        }
    }

    private static bool IsValidShape(AnnotationShape shape) => shape switch
    {
        RectangleShape r => r.Width >= 2 && r.Height >= 2,
        EllipseShape e => e.Width >= 2 && e.Height >= 2,
        PolygonShape p => p.Vertices.Count >= 3,
        LineShape l => l.Vertices.Count >= 2,
        _ => true
    };

    private static EllipseShape CreateCircleFromCenter(Point2D center, Point2D edge)
    {
        var radius = AnnotationGeometry.Distance(center, edge);
        return new EllipseShape
        {
            X = center.X - radius,
            Y = center.Y - radius,
            Width = radius * 2,
            Height = radius * 2,
            RadiusPoint = edge
        };
    }

    private (Annotation Annotation, int HandleIndex, HandleKind Kind)? HitTestHandle(Point2D point)
    {
        if (Document is null)
        {
            return null;
        }

        var tolerance = 10.0 / Math.Max(ViewportZoom, 0.001);
        foreach (var annotation in Document.Annotations.Reverse().Where(a => a.IsSelected && a.IsVisible))
        {
            var handles = GetHandles(annotation.Shape);
            for (var i = 0; i < handles.Count; i++)
            {
                if (AnnotationGeometry.Distance(handles[i].Point, point) <= tolerance)
                {
                    return (annotation, handles[i].Index, handles[i].Kind);
                }
            }
        }

        return null;
    }

    private static int InsertVertexAtMidpoint(Annotation annotation, int edgeStartIndex, Point2D point)
    {
        switch (annotation.Shape)
        {
            case PolygonShape polygon when polygon.Vertices.Count >= 2:
                var polygonInsertIndex = edgeStartIndex + 1;
                if (polygonInsertIndex > polygon.Vertices.Count)
                {
                    polygonInsertIndex = polygon.Vertices.Count;
                }

                polygon.Vertices.Insert(polygonInsertIndex, point);
                return polygonInsertIndex;
            case LineShape line when line.Vertices.Count >= 2:
                var lineInsertIndex = Math.Min(edgeStartIndex + 1, line.Vertices.Count);
                line.Vertices.Insert(lineInsertIndex, point);
                return lineInsertIndex;
            default:
                return edgeStartIndex;
        }
    }

    private void DeleteHandle(Annotation annotation, int handleIndex)
    {
        switch (annotation.Shape)
        {
            case PolygonShape polygon when polygon.Vertices.Count > 3 && handleIndex >= 0 && handleIndex < polygon.Vertices.Count:
                SaveUndoState();
                polygon.Vertices.RemoveAt(handleIndex);
                Document!.IsDirty = true;
                InvalidateVisual();
                break;
            case LineShape line when line.Vertices.Count > 2 && handleIndex >= 0 && handleIndex < line.Vertices.Count:
                SaveUndoState();
                line.Vertices.RemoveAt(handleIndex);
                Document!.IsDirty = true;
                InvalidateVisual();
                break;
        }

    }

    private static void DrawAnnotationBadge(DrawingContext dc, Annotation annotation, double scale, Color color)
    {
        var points = annotation.Shape.Points;
        if (points.Count == 0) return;
        var label = annotation.IsSuggestion
            ? $"AI  {annotation.Label}{(annotation.Confidence is double confidence ? $"  {confidence:P0}" : string.Empty)}"
            : annotation.Label;
        var text = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"), 11 / scale, Brushes.White, 1);
        var x = points.Min(p => p.X);
        var y = points.Min(p => p.Y) - text.Height - 4 / scale;
        var rect = new Rect(x, Math.Max(0, y), text.Width + 10 / scale, text.Height + 4 / scale);
        var background = new SolidColorBrush(Color.FromArgb(225, color.R, color.G, color.B));
        dc.DrawRoundedRectangle(background, null, rect, 3 / scale, 3 / scale);
        dc.DrawText(text, new Point(rect.X + 5 / scale, rect.Y + 2 / scale));
    }

    private static void MoveHandleTo(Annotation annotation, int handleIndex, Point2D point)
    {
        switch (annotation.Shape)
        {
            case RectangleShape rectangle:
                ResizeRectangle(rectangle, handleIndex, point);
                break;
            case EllipseShape ellipse:
                ResizeCircleFromCardinalHandle(ellipse, point);
                break;
            case PolygonShape polygon when handleIndex < polygon.Vertices.Count:
                polygon.Vertices[handleIndex] = point;
                break;
            case LineShape line when handleIndex < line.Vertices.Count:
                line.Vertices[handleIndex] = point;
                break;
            case PointShape pointShape:
                pointShape.Point = point;
                break;
        }
    }

    private static void ResizeRectangle(RectangleShape rectangle, int handleIndex, Point2D point)
    {
        var left = rectangle.X;
        var top = rectangle.Y;
        var right = rectangle.X + rectangle.Width;
        var bottom = rectangle.Y + rectangle.Height;

        switch (handleIndex)
        {
            case 0:
                left = point.X;
                top = point.Y;
                break;
            case 1:
                top = point.Y;
                break;
            case 2:
                right = point.X;
                top = point.Y;
                break;
            case 3:
                right = point.X;
                break;
            case 4:
                right = point.X;
                bottom = point.Y;
                break;
            case 5:
                bottom = point.Y;
                break;
            case 6:
                left = point.X;
                bottom = point.Y;
                break;
            case 7:
                left = point.X;
                break;
        }

        rectangle.X = Math.Min(left, right);
        rectangle.Y = Math.Min(top, bottom);
        rectangle.Width = Math.Abs(right - left);
        rectangle.Height = Math.Abs(bottom - top);
    }

    private static void ResizeCircleFromCardinalHandle(EllipseShape ellipse, Point2D point)
    {
        var centerX = ellipse.X + ellipse.Width / 2;
        var centerY = ellipse.Y + ellipse.Height / 2;
        var radius = Math.Max(1, AnnotationGeometry.Distance(new Point2D(centerX, centerY), point));
        var diameter = radius * 2;
        ellipse.X = centerX - radius;
        ellipse.Y = centerY - radius;
        ellipse.Width = diameter;
        ellipse.Height = diameter;
    }

    private void MoveSelected(bool toFront)
    {
        if (Document is null)
        {
            return;
        }

        var selected = Document.Annotations.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        SaveUndoState();
        foreach (var annotation in selected)
        {
            Document.Annotations.Remove(annotation);
        }

        if (toFront)
        {
            foreach (var annotation in selected)
            {
                Document.Annotations.Add(annotation);
            }
        }
        else
        {
            for (var i = selected.Count - 1; i >= 0; i--)
            {
                Document.Annotations.Insert(0, selected[i]);
            }
        }

        Document.IsDirty = true;
        InvalidateVisual();
    }

    private void CreateContextMenu()
    {
        AddContextMenuItem("Select All", (_, _) => SelectAll());
        AddContextMenuItem("Unselect All", (_, _) => UnselectAll());
        AddContextMenuItem("Delete", (_, _) => DeleteSelected());
        AddContextMenuItem("Delete All", (_, _) => DeleteAll());
        contextMenu.Items.Add(new Separator());
        AddContextMenuItem("Move to Front", (_, _) => MoveSelectedToFront());
        AddContextMenuItem("Move to Back", (_, _) => MoveSelectedToBack());
        contextMenu.Items.Add(new Separator());
        AddContextMenuItem("Undo", (_, _) => Undo());
        AddContextMenuItem("Redo", (_, _) => Redo());
        contextMenu.Items.Add(new Separator());
        AddContextMenuItem("Finish Polygon/Line", (_, _) => FinishActiveShape());
        ContextMenu = contextMenu;
        contextMenu.Opened += (_, _) => UpdateContextMenuState();
    }

    private void AddContextMenuItem(string header, RoutedEventHandler click)
    {
        var item = new MenuItem { Header = header };
        item.Click += click;
        contextMenu.Items.Add(item);
    }

    private void OpenContextMenu()
    {
        UpdateContextMenuState();
        contextMenu.PlacementTarget = this;
        contextMenu.IsOpen = true;
    }

    private void UpdateContextMenuState()
    {
        var hasDocument = Document is not null;
        var hasAnnotations = Document?.Annotations.Count > 0;
        var hasSelection = Document?.Annotations.Any(a => a.IsSelected) == true;
        var canFinish = activeAnnotation?.Shape is PolygonShape or LineShape || isPolylineDrawing;

        SetMenuItemEnabled("Select All", hasDocument && hasAnnotations);
        SetMenuItemEnabled("Unselect All", hasSelection);
        SetMenuItemEnabled("Delete", hasSelection);
        SetMenuItemEnabled("Delete All", hasDocument && hasAnnotations);
        SetMenuItemEnabled("Move to Front", hasSelection);
        SetMenuItemEnabled("Move to Back", hasSelection);
        SetMenuItemEnabled("Undo", undoStack.Count > 0);
        SetMenuItemEnabled("Redo", redoStack.Count > 0);
        SetMenuItemEnabled("Finish Polygon/Line", canFinish);
    }

    private void SetMenuItemEnabled(string header, bool isEnabled)
    {
        foreach (var item in contextMenu.Items.OfType<MenuItem>())
        {
            if (Equals(item.Header, header))
            {
                item.IsEnabled = isEnabled;
                return;
            }
        }
    }

    private void FinishPolyline()
    {
        if (activeAnnotation?.Shape is LineShape line)
        {
            if (line.Vertices.Count > 2)
                line.Vertices.RemoveAt(line.Vertices.Count - 1); // remove floating preview vertex
            else
                Document?.Annotations.Remove(activeAnnotation); // too short, discard
        }

        isPolylineDrawing = false;
        activeAnnotation = null;
        lastDraftPoint = null;
        ReleaseMouseCapture();
        InvalidateVisual();
        ActiveTool = AnnotationTool.Select;
    }

    private void FinishPolygon()
    {
        if (activeAnnotation?.Shape is PolygonShape polygon)
        {
            if (polygon.Vertices.Count > 1) polygon.Vertices.RemoveAt(polygon.Vertices.Count - 1);
            if (polygon.Vertices.Count < 3) Document?.Annotations.Remove(activeAnnotation);
        }
        isPolygonDrawing = false;
        activeAnnotation = null;
        ReleaseMouseCapture();
        InteractionHintChanged?.Invoke(this, string.Empty);
        InvalidateVisual();
        ActiveTool = AnnotationTool.Select;
    }

    private void RemoveLastDraftVertex()
    {
        if (activeAnnotation?.Shape is PolygonShape polygon && polygon.Vertices.Count > 2)
            polygon.Vertices.RemoveAt(polygon.Vertices.Count - 2);
        else if (activeAnnotation?.Shape is LineShape line && line.Vertices.Count > 2)
            line.Vertices.RemoveAt(line.Vertices.Count - 2);
        InvalidateVisual();
    }

    private void DuplicateSelected()
    {
        if (Document is null) return;
        var selected = Document.Annotations.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0) return;
        SaveUndoState();
        foreach (var annotation in Document.Annotations) annotation.IsSelected = false;
        foreach (var source in selected)
        {
            var clone = CloneAnnotation(source);
            AnnotationGeometry.Move(clone, 10, 10);
            clone.IsSelected = true;
            Document.Annotations.Add(clone);
        }
        Document.IsDirty = true;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void NudgeSelected(Key key, double amount)
    {
        if (Document is null || !Document.Annotations.Any(a => a.IsSelected)) return;
        SaveUndoState();
        var (dx, dy) = key switch
        {
            Key.Left => (-amount, 0d), Key.Right => (amount, 0d),
            Key.Up => (0d, -amount), _ => (0d, amount)
        };
        foreach (var annotation in Document.Annotations.Where(a => a.IsSelected))
            AnnotationGeometry.Move(annotation, dx, dy);
        Document.IsDirty = true;
        InvalidateVisual();
    }

    private Point2D ClampToImage(Point2D point) => Document?.Image is { } image
        ? new Point2D(Math.Clamp(point.X, 0, image.Width), Math.Clamp(point.Y, 0, image.Height))
        : point;

    private void FinishActiveShape()
    {
        if (isPolylineDrawing)
        {
            FinishPolyline();
            return;
        }

        if (isPolygonDrawing)
        {
            FinishPolygon();
            return;
        }

        activeAnnotation = null;
        InvalidateVisual();
    }

    private void SaveUndoState()
    {
        if (Document is null)
        {
            return;
        }

        undoStack.Push(CloneAnnotations(Document.Annotations));
        redoStack.Clear();
    }

    private void RestoreAnnotations(List<Annotation> annotations)
    {
        if (Document is null)
        {
            return;
        }

        Document.Annotations.Clear();
        foreach (var annotation in annotations)
        {
            Document.Annotations.Add(annotation);
        }

        activeAnnotation = null;
        Document.IsDirty = true;
        InvalidateVisual();
    }

    private static List<Annotation> CloneAnnotations(IEnumerable<Annotation> annotations) =>
        annotations.Select(CloneAnnotation).ToList();

    private static Annotation CloneAnnotation(Annotation annotation)
    {
        var clone = new Annotation
        {
            Id = annotation.Id,
            Label = annotation.Label,
            Color = annotation.Color,
            IsSelected = annotation.IsSelected,
            IsVisible = annotation.IsVisible,
            Occluded = annotation.Occluded,
            Truncated = annotation.Truncated,
            Crowd = annotation.Crowd,
            Confidence = annotation.Confidence,
            IsSuggestion = annotation.IsSuggestion,
            Shape = CloneShape(annotation.Shape)
        };
        foreach (var (k, v) in annotation.Attributes)
            clone.Attributes[k] = v;
        return clone;
    }

    private static AnnotationShape CloneShape(AnnotationShape shape)
    {
        switch (shape)
        {
            case RectangleShape rectangle:
                return new RectangleShape
                {
                    X = rectangle.X,
                    Y = rectangle.Y,
                    Width = rectangle.Width,
                    Height = rectangle.Height
                };
            case EllipseShape ellipse:
                return new EllipseShape
                {
                    X = ellipse.X,
                    Y = ellipse.Y,
                    Width = ellipse.Width,
                    Height = ellipse.Height,
                    RadiusPoint = ellipse.RadiusPoint
                };
            case PointShape point:
                return new PointShape { Point = point.Point };
            case PolygonShape polygon:
                var polygonClone = new PolygonShape();
                foreach (var vertex in polygon.Vertices)
                {
                    polygonClone.Vertices.Add(vertex);
                }

                return polygonClone;
            case LineShape line:
                var lineClone = new LineShape();
                foreach (var vertex in line.Vertices)
                {
                    lineClone.Vertices.Add(vertex);
                }

                return lineClone;
            default:
                throw new NotSupportedException($"Unsupported shape type: {shape.GetType().Name}");
        }
    }

    private void HandlePolygonClick(Point2D point, bool finish)
    {
        if (Document is null)
        {
            return;
        }

        if (finish)
        {
            activeAnnotation = null;
            InvalidateVisual();
            return;
        }

        if (activeAnnotation?.Shape is not PolygonShape polygon)
        {
            SaveUndoState();
            activeAnnotation = CreateAnnotation(new PolygonShape());
            polygon = (PolygonShape)activeAnnotation.Shape;
            Document.Annotations.Add(activeAnnotation);
        }
        else
        {
            SaveUndoState();
        }

        polygon.Vertices.Add(point);
        Document.IsDirty = true;
        InvalidateVisual();
    }

    private void UpdateDraftPolygon(PolygonShape polygon, Point2D point)
    {
        if (polygon.Vertices.Count == 0)
        {
            polygon.Vertices.Add(point);
            lastDraftPoint = point;
            return;
        }

        var minDistance = Math.Max(2, DraftPointSpacing) / Math.Max(ViewportZoom, 0.001);
        if (lastDraftPoint is null || AnnotationGeometry.Distance(lastDraftPoint.Value, point) >= minDistance)
        {
            polygon.Vertices.Add(point);
            lastDraftPoint = point;
        }
        else
        {
            polygon.Vertices[^1] = point;
        }
    }

    private void AddAnnotation(AnnotationShape shape)
    {
        Document?.Annotations.Add(CreateAnnotation(shape));
        if (Document is not null)
        {
            Document.IsDirty = true;
        }

        InvalidateVisual();
    }

    private Annotation CreateAnnotation(AnnotationShape shape)
    {
        if (Document is not null)
        {
            foreach (var annotation in Document.Annotations)
            {
                annotation.IsSelected = false;
            }
        }

        return new Annotation
        {
            Label = string.IsNullOrWhiteSpace(CurrentLabel) ? "object" : CurrentLabel.Trim(),
            Color = string.IsNullOrWhiteSpace(CurrentColor) ? "#22c55e" : CurrentColor.Trim(),
            Shape = shape,
            IsSelected = true
        };
    }

    private static void DrawAnnotation(
        DrawingContext dc,
        Annotation annotation,
        double zoom,
        bool isDraft,
        Point2D? pendingMidpointVertex,
        int pendingMidpointEdgeIndex,
        Point2D? draftCircleCenter,
        Point2D? draftCircleRadiusPoint,
        double fillOpacity = 0.22,
        double baseStrokeThickness = 2.0,
        bool isHovered = false)
    {
        var objectColor = ParseColor(annotation.Color);
        var strokeColor = annotation.IsSelected
            ? Color.FromRgb(0x79, 0xC0, 0xFF)
            : objectColor;
        // Suggestions render identically to manual annotations
        double effectiveFillOpacity = fillOpacity;
        var alpha = annotation.IsSelected
            ? (byte)Math.Clamp((effectiveFillOpacity + 0.08) * 255, 0, 255)
            : (byte)Math.Clamp(effectiveFillOpacity * 255, 0, 255);
        var fillColor = Color.FromArgb(alpha, objectColor.R, objectColor.G, objectColor.B);
        var brush = new SolidColorBrush(fillColor);
        var scale = Math.Max(zoom, 0.001);
        var strokeWidth = annotation.IsSelected
            ? (baseStrokeThickness + 1.0) / scale
            : (baseStrokeThickness + (isHovered ? 1.0 : 0.0)) / scale;
        var pen = new Pen(new SolidColorBrush(strokeColor), strokeWidth);
        if (annotation.IsSuggestion)
            pen.DashStyle = DashStyles.Dash;
        pen.Freeze();

        switch (annotation.Shape)
        {
            case RectangleShape rectangle:
                dc.DrawRectangle(brush, pen, new Rect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height));
                break;
            case EllipseShape ellipse:
                var center = new Point(ellipse.X + ellipse.Width / 2, ellipse.Y + ellipse.Height / 2);
                dc.DrawEllipse(brush, pen, new Point(ellipse.X + ellipse.Width / 2, ellipse.Y + ellipse.Height / 2),
                    ellipse.Width / 2, ellipse.Height / 2);
                if (isDraft && draftCircleCenter is { } radiusCenter && draftCircleRadiusPoint is { } radiusEnd)
                {
                    dc.DrawLine(new Pen(new SolidColorBrush(strokeColor), 1.75 / scale) { DashStyle = DashStyles.Dash },
                        new Point(radiusCenter.X, radiusCenter.Y),
                        new Point(radiusEnd.X, radiusEnd.Y));
                }
                break;
            case PolygonShape polygon:
                DrawPolyline(dc, polygon.Vertices, !isDraft, brush, pen);
                break;
            case LineShape line:
                DrawPolyline(dc, line.Vertices, false, null, pen);
                break;
            case PointShape point:
                dc.DrawEllipse(brush, pen, new Point(point.Point.X, point.Point.Y), 4 / scale, 4 / scale);
                break;
        }

        if (annotation.IsSelected || annotation.IsSuggestion || isHovered)
            DrawAnnotationBadge(dc, annotation, scale, objectColor);

        if (annotation.IsSelected)
        {
            if (pendingMidpointVertex is not null)
            {
                DrawPendingInsertedVertex(dc, annotation.Shape, pendingMidpointEdgeIndex, pendingMidpointVertex.Value, scale, strokeColor);
            }

            foreach (var handle in GetHandles(annotation.Shape))
            {
                var (sizePixels, fill, outline) = HandleVisual(handle.Kind);
                var size = sizePixels / scale;
                dc.DrawEllipse(fill, new Pen(outline, 1.5 / scale),
                    new Point(handle.Point.X, handle.Point.Y), size / 2, size / 2);
            }
        }
    }

    private static (double Size, Brush Fill, Brush Outline) HandleVisual(HandleKind kind)
    {
        return kind switch
        {
            HandleKind.RectangleSide => (8, Brushes.Cyan, Brushes.Black),
            HandleKind.CircleRadius => (9, Brushes.White, Brushes.Black),
            HandleKind.InsertMidpoint => (7, Brushes.Yellow, Brushes.Black),
            _ => (9, Brushes.White, Brushes.Black)
        };
    }

    private static void DrawPendingInsertedVertex(DrawingContext dc, AnnotationShape shape, int edgeStartIndex, Point2D inserted, double scale, Color strokeColor)
    {
        IReadOnlyList<Point2D> points = shape switch
        {
            PolygonShape polygon => polygon.Vertices,
            LineShape line => line.Vertices,
            _ => []
        };

        if (points.Count < 2 || edgeStartIndex < 0 || edgeStartIndex >= points.Count)
        {
            return;
        }

        var nextIndex = edgeStartIndex + 1;
        if (nextIndex >= points.Count)
        {
            if (shape is PolygonShape)
            {
                nextIndex = 0;
            }
            else
            {
                return;
            }
        }

        var pen = new Pen(new SolidColorBrush(strokeColor), 1.5 / scale) { DashStyle = DashStyles.Dash };
        dc.DrawLine(pen, new Point(points[edgeStartIndex].X, points[edgeStartIndex].Y), new Point(inserted.X, inserted.Y));
        dc.DrawLine(pen, new Point(inserted.X, inserted.Y), new Point(points[nextIndex].X, points[nextIndex].Y));
    }

    private static IReadOnlyList<Point2D> GetHandlePoints(AnnotationShape shape)
    {
        return shape switch
        {
            RectangleShape rectangle =>
            [
                new(rectangle.X, rectangle.Y),
                new(rectangle.X + rectangle.Width / 2, rectangle.Y),
                new(rectangle.X + rectangle.Width, rectangle.Y),
                new(rectangle.X + rectangle.Width, rectangle.Y + rectangle.Height / 2),
                new(rectangle.X + rectangle.Width, rectangle.Y + rectangle.Height),
                new(rectangle.X + rectangle.Width / 2, rectangle.Y + rectangle.Height),
                new(rectangle.X, rectangle.Y + rectangle.Height),
                new(rectangle.X, rectangle.Y + rectangle.Height / 2)
            ],
            EllipseShape ellipse =>
            [
                new(ellipse.X + ellipse.Width / 2, ellipse.Y),
                new(ellipse.X + ellipse.Width, ellipse.Y + ellipse.Height / 2),
                new(ellipse.X + ellipse.Width / 2, ellipse.Y + ellipse.Height),
                new(ellipse.X, ellipse.Y + ellipse.Height / 2)
            ],
            _ => shape.Points
        };
    }

    private enum HandleKind
    {
        Vertex,
        RectangleSide,
        CircleRadius,
        InsertMidpoint
    }

    private readonly record struct ShapeHandle(Point2D Point, int Index, HandleKind Kind);

    private static IReadOnlyList<ShapeHandle> GetHandles(AnnotationShape shape)
    {
        if (shape is PolygonShape polygon)
        {
            return GetVertexAndMidpointHandles(polygon.Vertices, close: true);
        }

        if (shape is LineShape line)
        {
            return GetVertexAndMidpointHandles(line.Vertices, close: false);
        }

        return GetHandlePoints(shape).Select((p, i) =>
            new ShapeHandle(p, i, shape is RectangleShape && i % 2 == 1
                ? HandleKind.RectangleSide
                : shape is EllipseShape
                    ? HandleKind.CircleRadius
                    : HandleKind.Vertex)).ToList();
    }

    private static IReadOnlyList<ShapeHandle> GetVertexAndMidpointHandles(IReadOnlyList<Point2D> points, bool close)
    {
        var handles = new List<ShapeHandle>();
        for (var i = 0; i < points.Count; i++)
        {
            handles.Add(new ShapeHandle(points[i], i, HandleKind.Vertex));

            var hasNext = i < points.Count - 1;
            if (!hasNext && !close)
            {
                continue;
            }

            var nextIndex = hasNext ? i + 1 : 0;
            var next = points[nextIndex];
            handles.Add(new ShapeHandle(new Point2D((points[i].X + next.X) / 2, (points[i].Y + next.Y) / 2), i, HandleKind.InsertMidpoint));
        }

        return handles;
    }

    private static Color ParseColor(string value)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(value);
        }
        catch
        {
            return Colors.LimeGreen;
        }
    }

    private static void DrawPolyline(DrawingContext dc, IReadOnlyList<Point2D> points, bool close, Brush? fill, Pen pen)
    {
        if (points.Count == 0)
        {
            return;
        }

        if (points.Count == 1)
        {
            dc.DrawEllipse(fill, pen, new Point(points[0].X, points[0].Y), 3, 3);
            return;
        }

        var figure = new PathFigure(new Point(points[0].X, points[0].Y), points.Skip(1).Select(p => new LineSegment(new Point(p.X, p.Y), true)), close);
        var geometry = new PathGeometry([figure]);
        dc.DrawGeometry(fill, pen, geometry);
    }

    private static Point2D ToPoint2D(Point point) => new(point.X, point.Y);

}
