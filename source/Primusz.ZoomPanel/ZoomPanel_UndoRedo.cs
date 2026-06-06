using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Primusz.ZoomPanel.Helpers;

namespace Primusz.ZoomPanel
{
    public partial class ZoomPanel
    {
        private readonly Stack<UndoRedoStackItem> undoStack = new Stack<UndoRedoStackItem>();
        private readonly Stack<UndoRedoStackItem> redoStack = new Stack<UndoRedoStackItem>();
        private UndoRedoStackItem viewportZoomCache;

        /// <summary> 
        ///     Record the previous zoom level, so that we can return to it.
        /// </summary>
        public void SaveZoom()
        {
            viewportZoomCache = CreateUndoRedoStackItem();
            if (undoStack.Any() && viewportZoomCache.Equals(undoStack.Peek())) return;
            undoStack.Push(viewportZoomCache);
            redoStack.Clear();
            undoZoomCommand?.RaiseCanExecuteChanged();
            redoZoomCommand?.RaiseCanExecuteChanged();
        }

        /// <summary> 
        ///  Record the last saved zoom level, so that we can return to it if no activity for 750 milliseconds
        /// </summary>
        public void DelayedSaveZoom750Miliseconds()
        {
            if (timer750Miliseconds?.Running != true) viewportZoomCache = CreateUndoRedoStackItem();
            (timer750Miliseconds ?? (timer750Miliseconds = new KeepAliveTimer(TimeSpan.FromMilliseconds(740), () =>
            {
                if (undoStack.Any() && viewportZoomCache.Equals(undoStack.Peek())) return;
                undoStack.Push(viewportZoomCache);
                redoStack.Clear();
                undoZoomCommand?.RaiseCanExecuteChanged();
                redoZoomCommand?.RaiseCanExecuteChanged();
            }))).Nudge();
        }
        private KeepAliveTimer timer750Miliseconds;


        /// <summary> 
        ///  Record the last saved zoom level, so that we can return to it if no activity for 1550 milliseconds
        /// </summary>
        public void DelayedSaveZoom1500Miliseconds()
        {
            if (!timer1500Miliseconds?.Running != true) viewportZoomCache = CreateUndoRedoStackItem();
            (timer1500Miliseconds ?? (timer1500Miliseconds = new KeepAliveTimer(TimeSpan.FromMilliseconds(1500), () =>
            {
                if (undoStack.Any() && viewportZoomCache.Equals(undoStack.Peek())) return;
                undoStack.Push(viewportZoomCache);
                redoStack.Clear();
                undoZoomCommand?.RaiseCanExecuteChanged();
                redoZoomCommand?.RaiseCanExecuteChanged();
            }))).Nudge();
        }
        private KeepAliveTimer timer1500Miliseconds;

        private UndoRedoStackItem CreateUndoRedoStackItem()
        {
            return new UndoRedoStackItem(ContentOffsetX, ContentOffsetY,
                ContentViewportWidth, ContentViewportHeight, InternalViewportZoom);
        }

        /// <summary>
        ///     Jump back to the previous zoom level, saving current zoom to Redo Stack.
        /// </summary>
        private void UndoZoom()
        {
            viewportZoomCache = CreateUndoRedoStackItem();
            if (!undoStack.Any() || !viewportZoomCache.Equals(undoStack.Peek()))
                redoStack.Push(viewportZoomCache);
            viewportZoomCache = undoStack.Pop();
            AnimatedZoomTo(viewportZoomCache.Zoom, viewportZoomCache.Rect);
            SetScrollViewerFocus();
            undoZoomCommand?.RaiseCanExecuteChanged();
            redoZoomCommand?.RaiseCanExecuteChanged();
        }

        /// <summary>
        ///     Jump back to the most recent zoom level saved on redo stack.
        /// </summary>
        private void RedoZoom()
        {
            viewportZoomCache = CreateUndoRedoStackItem();
            if (!redoStack.Any() || !viewportZoomCache.Equals(redoStack.Peek()))
                undoStack.Push(viewportZoomCache);
            viewportZoomCache = redoStack.Pop();
            AnimatedZoomTo(viewportZoomCache.Zoom, viewportZoomCache.Rect);
            SetScrollViewerFocus();
            undoZoomCommand?.RaiseCanExecuteChanged();
            redoZoomCommand?.RaiseCanExecuteChanged();
        }

        private bool CanUndoZoom => undoStack.Any();
        private bool CanRedoZoom => redoStack.Any();

        /// <summary>
        ///     Command to implement Undo 
        /// </summary>
        public ICommand UndoZoomCommand => undoZoomCommand ?? (undoZoomCommand =
            new RelayCommand(UndoZoom, () => CanUndoZoom));
        private RelayCommand undoZoomCommand;

        /// <summary>
        ///     Command to implement Redo 
        /// </summary>
        public ICommand RedoZoomCommand => redoZoomCommand ?? (redoZoomCommand =
             new RelayCommand(RedoZoom, () => CanRedoZoom));
        private RelayCommand redoZoomCommand;

        private class UndoRedoStackItem
        {
            public UndoRedoStackItem(Rect rect, double zoom)
            {
                Rect = rect;
                Zoom = zoom;
            }

            public UndoRedoStackItem(double offsetX, double offsetY, double width, double height, double zoom)
            {
                Rect = new Rect(offsetX, offsetY, width, height);
                Zoom = zoom;
            }

            public Rect Rect { get; }
            public double Zoom { get; }

            public override string ToString()
            {
                return $"Rectangle {{{Rect.X},{Rect.X}}}, Zoom {Zoom}";
            }

            public bool Equals(UndoRedoStackItem obj)
            {
                return Zoom.IsWithinOnePercent(obj.Zoom) && Rect.Equals(obj.Rect);
            }
        }

        private void SetScrollViewerFocus()
        {
            var scrollViewer = content.FindParentControl<ScrollViewer>();
            if (scrollViewer != null)
            {
                Keyboard.Focus(scrollViewer);
                scrollViewer.Focus();
            }
        }
    }
}