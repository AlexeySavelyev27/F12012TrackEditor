using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PSSGEditor
{
    public partial class MainWindow
    {
        private void PopulateTreeView()
        {
            PssgTreeView.ItemsSource = null;
            if (rootNode != null)
            {
                PssgTreeView.ItemsSource = new List<PSSGNode> { rootNode };
            }
        }

        private (int nodes, int meshes, int textures) CollectStats(PSSGNode root)
        {
            int nodes = 0, meshes = 0, textures = 0;
            var stack = new Stack<PSSGNode>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                nodes++;
                if (string.Equals(n.Name, "MESH", StringComparison.OrdinalIgnoreCase))
                    meshes++;
                if (string.Equals(n.Name, "TEXTURE", StringComparison.OrdinalIgnoreCase))
                    textures++;
                foreach (var c in n.Children)
                    stack.Push(c);
            }
            return (nodes, meshes, textures);
        }

        private void PssgTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (PssgTreeView.SelectedItem is PSSGNode node)
            {
                currentNode = node;
                ShowNodeContent(currentNode);
            }
        }

        private void ShowNodeContent(PSSGNode node)
        {
            // Очищаем старые данные
            AttributesDataGrid.ItemsSource = null;
            isLoadingRawData = true;
            RawDataTextBox.Text = string.Empty;
            RawDataTextBox.IsReadOnly = true;
            RawDataTextBox.Background = Brushes.White;
            RawDataPanel.Visibility = Visibility.Collapsed;
            AttributesDataGrid.IsEnabled = true;
            rawDataOriginalLength = 0;
            isLoadingRawData = false;
            AttributesDataGrid.Visibility = Visibility.Collapsed;
            AttributesRow.Height = new GridLength(1, GridUnitType.Star);
            RawDataRow.Height = new GridLength(0);

            var listForGrid = new List<AttributeItem>();

            // Заполняем атрибуты (Key → Value)
            if (node.Attributes != null && node.Attributes.Count > 0)
            {
                foreach (var kv in node.Attributes)
                {
                    string valDisplay = BytesToDisplay(kv.Key, kv.Value);
                    int origLen = kv.Value?.Length ?? 0;
                    listForGrid.Add(new AttributeItem
                    {
                        Key = kv.Key,
                        Value = valDisplay,
                        OriginalLength = origLen
                    });
                }
            }

            // Если есть Raw-данные
            if (node.Data != null && node.Data.Length > 0)
            {
                isLoadingRawData = true;
                string rawDisplay = BytesToDisplay("__data__", node.Data);
                RawDataTextBox.Text = rawDisplay;
                RawDataPanel.Visibility = Visibility.Visible;
                // Disable DataGrid only when there are no attributes
                AttributesDataGrid.IsEnabled = listForGrid.Count != 0;
                rawDataOriginalLength = node.Data.Length;

                isLoadingRawData = false;
            }

            AttributesDataGrid.ItemsSource = listForGrid;

            // Настраиваем видимость и размеры строк
            if (listForGrid.Count > 0)
            {
                AttributesDataGrid.Visibility = Visibility.Visible;
                AttributesRow.Height = RawDataPanel.Visibility == Visibility.Visible
                    ? GridLength.Auto
                    : new GridLength(1, GridUnitType.Star);
            }
            else
            {
                AttributesDataGrid.Visibility = Visibility.Collapsed;
                AttributesRow.Height = RawDataPanel.Visibility == Visibility.Visible
                    ? new GridLength(0)
                    : new GridLength(1, GridUnitType.Star);
            }

            if (RawDataPanel.Visibility == Visibility.Visible)
            {
                RawDataRow.Height = new GridLength(1, GridUnitType.Star);
            }
            else
            {
                RawDataRow.Height = new GridLength(0);
            }

            // Восстанавливаем сортировку, если была
            if (!string.IsNullOrEmpty(savedSortMember) && savedSortDirection.HasValue)
            {
                foreach (var col in AttributesDataGrid.Columns)
                    col.SortDirection = null;

                var sortColumn = AttributesDataGrid.Columns
                    .FirstOrDefault(c => c.SortMemberPath == savedSortMember);
                if (sortColumn != null)
                {
                    AttributesDataGrid.Items.SortDescriptions.Clear();
                    AttributesDataGrid.Items.SortDescriptions.Add(
                        new SortDescription(savedSortMember, savedSortDirection.Value));
                    sortColumn.SortDirection = savedSortDirection.Value;
                    AttributesDataGrid.Items.Refresh();
                }
            }
        }
    }
}

