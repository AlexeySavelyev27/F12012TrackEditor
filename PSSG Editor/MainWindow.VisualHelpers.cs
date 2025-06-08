using System.Windows;
using System.Windows.Media;

namespace PSSGEditor
{
    public partial class MainWindow
    {
        // Ищет первого визуального потомка типа T
        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T correctlyTyped)
                    return correctlyTyped;
                var desc = FindVisualChild<T>(child);
                if (desc != null)
                    return desc;
            }
            return null;
        }

        // Ищет первого визуального родителя типа T
        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            if (child == null) return null;
            DependencyObject parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T correctlyTyped)
                    return correctlyTyped;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }
    }
}

