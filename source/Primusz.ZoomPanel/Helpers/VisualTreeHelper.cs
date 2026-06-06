using System.Windows;

namespace Primusz.ZoomPanel.Helpers
{
    public static class VisualTreeHelper
    {
        /// <summary>
        /// Find first paretn of type T in VisualTree.
        /// </summary>
        public static T FindParentControl<T>(this DependencyObject control) where T : DependencyObject
        {
            DependencyObject parent = System.Windows.Media.VisualTreeHelper.GetParent(control);
            while (parent != null && !(parent is T))
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
            return parent as T;
        }

        /// <summary>
        /// Find first child of type T in VisualTree.
        /// </summary>
        public static T FindChildControl<T>(this DependencyObject control) where T : DependencyObject
        {
            int childNumber = System.Windows.Media.VisualTreeHelper.GetChildrenCount(control);
            for (var i = 0; i < childNumber; i++)
            {
                DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(control, i);
                return (child is T)
                    ? (T)child : FindChildControl<T>(child);
            }
            return null;
        }
    }
}