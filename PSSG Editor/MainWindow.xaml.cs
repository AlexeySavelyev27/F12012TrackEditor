// MainWindow.xaml.cs
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.ComponentModel;
using System.Linq;
using System.Windows.Threading;

namespace PSSGEditor
{
    public partial class MainWindow : Window
    {
        private PSSGNode rootNode;
        private Dictionary<TreeViewItem, PSSGNode> nodeMapping = new();
        private PSSGNode currentNode;

        // To preserve vertical offset of ScrollViewer inside TextBox
        private double savedVerticalOffset = 0;

        // To remember sorting
        private string savedSortMember = null;
        private ListSortDirection? savedSortDirection = null;

        // For placing caret after double-click
        private Point? pendingCaretPoint = null;
        private DataGridCell pendingCaretCell = null;

        // Allow editing only on double-click
        private bool allowEdit = false;

        public MainWindow()
        {
            InitializeComponent();

            // Handle end of cell edit to save data
            AttributesDataGrid.CellEditEnding += AttributesDataGrid_CellEditEnding;

            // Remember new sorting parameters
            AttributesDataGrid.Sorting += AttributesDataGrid_Sorting;

            // Restrict beginning of edit and allow scrolling with wheel
            AttributesDataGrid.BeginningEdit += AttributesDataGrid_BeginningEdit;
            AttributesDataGrid.PreviewMouseWheel += AttributesDataGrid_PreviewMouseWheel;
        }

        #region Menu Handlers

        private void OpenMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "PSSG/.ens files (*.pssg;*.ens)|*.pssg;*.ens",
                Title = "Open PSSG File"
            };
            if (ofd.ShowDialog() != true) return;

            try
            {
                var parser = new PSSGParser(ofd.FileName);
                rootNode = parser.Parse();

                var stats = CollectStats(rootNode);
                StatusText.Text = $"Nodes: {stats.nodes}, Meshes: {stats.meshes}, Textures: {stats.textures}";

                PopulateTreeView();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open file:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Error opening file";
            }
        }

        private void SaveAsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (rootNode == null) return;

            var sfd = new SaveFileDialog
            {
                Filter = "PSSG files (*.pssg)|*.pssg",
                Title = "Save as PSSG"
            };
            if (sfd.ShowDialog() != true) return;

            try
            {
                var writer = new PSSGWriter(rootNode);
                writer.Save(sfd.FileName);
                StatusText.Text = $"Saved: {sfd.FileName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save file:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Error saving file";
            }
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        #endregion

        #region TreeView & Display Logic

        private void PopulateTreeView()
        {
            PssgTreeView.Items.Clear();
            nodeMapping.Clear();

            void Recurse(PSSGNode node, ItemCollection parentItems)
            {
                var tvi = new TreeViewItem { Header = node.Name };
                parentItems.Add(tvi);
                nodeMapping[tvi] = node;
                foreach (var child in node.Children)
                {
                    Recurse(child, tvi.Items);
                }
            }

            if (rootNode != null)
                Recurse(rootNode, PssgTreeView.Items);
        }

        private void PssgTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (PssgTreeView.SelectedItem == null) return;
            var selectedItem = (TreeViewItem)PssgTreeView.SelectedItem;
            if (!nodeMapping.ContainsKey(selectedItem)) return;

            currentNode = nodeMapping[selectedItem];
            ShowNodeContent(currentNode);
        }

        private void ShowNodeContent(PSSGNode node)
        {
            // Clear old data
            AttributesDataGrid.ItemsSource = null;

            var listForGrid = new List<AttributeItem>();

            // Fill attributes (Key → Value)
            if (node.Attributes != null && node.Attributes.Count > 0)
            {
                foreach (var attr in node.Attributes)
                {
                    string valDisplay = BytesToDisplay(attr.Name, attr.Value);
                    int origLen = attr.Value?.Length ?? 0;
                    listForGrid.Add(new AttributeItem
                    {
                        Key = attr.Name,
                        Value = valDisplay,
                        OriginalLength = origLen,
                        Source = attr
                    });
                }
            }

            // If there is Raw data
            if (node.Data != null && node.Data.Length > 0)
            {
                string rawDisplay = BytesToDisplay("__data__", node.Data);
                int origLen = node.Data.Length;
                listForGrid.Add(new AttributeItem
                {
                    Key = "Raw Data",
                    Value = rawDisplay,
                    OriginalLength = origLen
                });
            }

            // Even if list is empty, keep DataGrid visible
            AttributesDataGrid.ItemsSource = listForGrid;
            AdjustAttributeColumnWidth();

            // Restore sorting if existed
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

            // Ensure DataGrid is visible
            AttributesDataGrid.Visibility = Visibility.Visible;
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

        #endregion

        #region Editing Handlers

        private void AttributesDataGrid_CellMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var cell = sender as DataGridCell;
            if (cell != null && cell.Column.DisplayIndex == 1)
            {
                // 1) Find ScrollViewer inside cell template and save offset
                var contentPresenter = FindVisualChild<ContentPresenter>(cell);
                if (contentPresenter != null)
                {
                    var sv = FindVisualChild<ScrollViewer>(contentPresenter);
                    if (sv != null)
                    {
                        savedVerticalOffset = sv.VerticalOffset;
                    }
                }

                // Remember double-click point for caret positioning
                pendingCaretPoint = e.GetPosition(cell);
                pendingCaretCell = cell;

                // 2) Clear any selection and go into edit mode on this cell
                AttributesDataGrid.UnselectAllCells();
                var cellInfo = new DataGridCellInfo(cell.DataContext, cell.Column);
                AttributesDataGrid.CurrentCell = cellInfo;
                allowEdit = true;
                AttributesDataGrid.BeginEdit();
                allowEdit = false;

                e.Handled = true;
            }
        }

        /// <summary>
        /// PreparingCellForEdit: when DataGrid creates TextBox, restore scroll inside TextBox
        /// and attach click handler to position caret without selecting all text.
        /// </summary>
        private void AttributesDataGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            if (e.Column.DisplayIndex == 1)
            {
                // e.EditingElement is the generated TextBox
                if (e.EditingElement is TextBox tb)
                {
                    // Find parent ScrollViewer in VisualTree (from our template) and restore offset
                    var sv = FindVisualParent<ScrollViewer>(tb);
                    if (sv != null)
                    {
                        sv.ScrollToVerticalOffset(savedVerticalOffset);
                    }

                    // If we stored a click point, set caret there
                    if (pendingCaretPoint.HasValue && pendingCaretCell != null)
                    {
                        Point pt = pendingCaretCell.TranslatePoint(pendingCaretPoint.Value, tb);
                        int charIndex = tb.GetCharacterIndexFromPoint(pt, false);
                        if (charIndex < 0)
                        {
                            charIndex = tb.Text.Length;
                        }
                        else if (charIndex == tb.Text.Length - 1)
                        {
                            var edge = tb.GetRectFromCharacterIndex(charIndex, true);
                            if (pt.X >= edge.X)
                                charIndex = tb.Text.Length;
                        }
                        tb.CaretIndex = charIndex;
                        tb.SelectionLength = 0;
                        pendingCaretPoint = null;
                        pendingCaretCell = null;
                    }

                    // ALWAYS intercept first mouse down to place caret manually
                    tb.PreviewMouseLeftButtonDown += ValueTextBox_PreviewMouseLeftButtonDown;

                    // After TextBox processes the click, stop the event so the
                    // DataGrid does not reselect the cell while editing
                    tb.AddHandler(UIElement.MouseLeftButtonDownEvent,
                        new MouseButtonEventHandler(ValueTextBox_MouseLeftButtonDown),
                        handledEventsToo: true);
                }
            }
        }

        private void AttributesDataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (!allowEdit)
            {
                e.Cancel = true;
            }
        }

        private void AttributesDataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = FindVisualParent<ScrollViewer>((DependencyObject)sender);
            if (sv != null)
            {
                sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
                e.Handled = true;
            }
        }

        private void AttributesDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (AttributesDataGrid.CurrentCell.IsValid)
                {
                    AttributesDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape)
            {
                AttributesDataGrid.CancelEdit();
                AttributesDataGrid.UnselectAllCells();
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        }

        private void AttributesDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            ListSortDirection newDirection = e.Column.SortDirection != ListSortDirection.Ascending
                ? ListSortDirection.Ascending
                : ListSortDirection.Descending;

            savedSortMember = e.Column.SortMemberPath;
            savedSortDirection = newDirection;
            // Let WPF perform the sort automatically
        }

        /// <summary>
        /// If click occurs outside TextBox (i.e., not in the Value field), clear selection to remove any focus border.
        /// </summary>
        private void AttributesDataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var depObj = (DependencyObject)e.OriginalSource;
            while (depObj != null && !(depObj is DataGridCell))
                depObj = VisualTreeHelper.GetParent(depObj);

            if (depObj == null)
            {
                // Click not on a cell – clear all selection
                AttributesDataGrid.UnselectAllCells();
                Keyboard.ClearFocus();
                return;
            }

            if (depObj is DataGridCell cell)
            {
                // When the clicked cell is already in edit mode, do not let the
                // DataGrid process the click. This prevents the cell from being
                // re-selected or losing the current caret position while editing.
                if (cell.IsEditing)
                {
                    // Skip custom selection logic when already editing so the
                    // TextBox inside the cell receives mouse events normally
                    return;
                }

                // If click on the "Attribute" column, immediately jump to "Value" cell in the same row
                if (cell.Column.DisplayIndex == 0)
                {
                    var item = cell.DataContext;
                    var valueColumn = AttributesDataGrid.Columns
                        .FirstOrDefault(c => c.Header.ToString() == "Value");
                    if (valueColumn != null)
                    {
                        // Clear existing selection
                        AttributesDataGrid.SelectedCells.Clear();

                        // Create DataGridCellInfo for the "Value" cell in the same row
                        var cellInfo = new DataGridCellInfo(item, valueColumn);
                        AttributesDataGrid.CurrentCell = cellInfo;
                        AttributesDataGrid.SelectedCells.Add(cellInfo);

                        // Focus DataGrid to make selection active
                        AttributesDataGrid.Focus();

                        // Do NOT call BeginEdit() — only select
                        e.Handled = true; // prevent default selection of "Attribute" cell
                    }
                }
            }
        }

        /// <summary>
        /// When user clicks inside the editing TextBox (Value field),
        /// we intercept and place caret at click position without selecting all text
        /// or causing cell to be re-selected.
        /// </summary>
        private void ValueTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var tb = (TextBox)sender;
            if (!tb.IsKeyboardFocusWithin)
            {
                // First click when TextBox not focused: place caret manually
                e.Handled = true;
                tb.Focus();

                // Compute character index from click position
                Point clickPos = e.GetPosition(tb);
                int charIndex = tb.GetCharacterIndexFromPoint(clickPos, false);
                if (charIndex < 0)
                {
                    charIndex = tb.Text.Length;
                }
                else if (charIndex == tb.Text.Length - 1)
                {
                    var edge = tb.GetRectFromCharacterIndex(charIndex, true);
                    if (clickPos.X >= edge.X)
                        charIndex = tb.Text.Length;
                }
                tb.CaretIndex = charIndex;
            }
        }

        // Stop click events from bubbling to the DataGrid when editing so
        // the cell is not reselected while moving the caret or selecting text
        private void ValueTextBox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private void AttributesDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (currentNode == null) return;
            var item = (AttributeItem)e.Row.Item;
            string attrName = item.Key;

            if (!(e.EditingElement is TextBox element))
                return;
            string newText = element.Text;

            byte[] newBytes;

            if (attrName == "Raw Data")
            {
                newBytes = DisplayToBytes("__data__", newText, item.OriginalLength);
                currentNode.Data = newBytes;
            }
            else
            {
                if (item.Source != null)
                {
                    newBytes = DisplayToBytes(attrName, newText, item.OriginalLength);
                    item.Source.Value = newBytes;
                }
                else
                {
                    return;
                }
            }

            // Update OriginalLength and Value for next edit
            item.OriginalLength = newBytes.Length;
            item.Value = newText;

            // After changing text, recalc column width asynchronously
            Dispatcher.BeginInvoke(new Action(AdjustAttributeColumnWidth), DispatcherPriority.Background);
        }

        // Recalculate width of the "Attribute" column based on content
        private void AdjustAttributeColumnWidth()
        {
            var col = AttributesDataGrid.Columns.FirstOrDefault(c => c.Header?.ToString() == "Attribute");
            if (col != null)
            {
                col.Width = new DataGridLength(1, DataGridLengthUnitType.SizeToCells);
                col.Width = DataGridLength.Auto;
            }
        }

        #endregion

        #region Helper Methods: finding visual children/parents

        // Find first visual child of type T
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

        // Find first visual parent of type T
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

        #endregion

        #region Converters (Bytes ↔ Display String)

        private string BytesToDisplay(string name, byte[] b)
        {
            if (b == null) return string.Empty;

            // Try big-endian 4-byte length prefix (UTF-8)
            if (b.Length >= 4)
            {
                uint sz = ReadUInt32FromBytes(b, 0);
                if (sz <= b.Length - 4)
                {
                    try
                    {
                        return Encoding.UTF8.GetString(b, 4, (int)sz);
                    }
                    catch { }
                }
            }

            // Try full array as UTF-8 text
            try
            {
                string txt = Encoding.UTF8.GetString(b);
                bool printable = true;
                foreach (char c in txt)
                {
                    if (c < 32 || c >= 127) { printable = false; break; }
                }
                if (printable) return txt;
            }
            catch { }

            // If Transform/BoundingBox (multiple floats)
            if ((name.Equals("Transform", StringComparison.OrdinalIgnoreCase) ||
                 name.Equals("BoundingBox", StringComparison.OrdinalIgnoreCase))
                && b.Length % 4 == 0)
            {
                int count = b.Length / 4;
                var sb = new StringBuilder();
                for (int i = 0; i < count; i++)
                {
                    float v = ReadFloatFromBytes(b, i * 4);
                    sb.AppendLine(v.ToString("F6"));
                }
                return sb.ToString().TrimEnd();
            }

            // If length 1, 2, 4 — show as number
            if (b.Length == 1)
                return b[0].ToString();
            if (b.Length == 2)
            {
                var temp = new byte[2];
                Array.Copy(b, 0, temp, 0, 2);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(temp);
                return BitConverter.ToUInt16(temp, 0).ToString();
            }
            if (b.Length == 4)
            {
                var temp = new byte[4];
                Array.Copy(b, 0, temp, 0, 4);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(temp);
                return BitConverter.ToUInt32(temp, 0).ToString();
            }

            // Otherwise — hex string
            return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
        }

        private byte[] DisplayToBytes(string name, string s, int originalLength)
        {
            // Try parse as number
            if (ulong.TryParse(s, out ulong num))
            {
                try
                {
                    byte[] bytes;
                    if (originalLength == 1)
                        bytes = new byte[] { (byte)num };
                    else if (originalLength == 2)
                        bytes = BitConverter.GetBytes((ushort)num);
                    else // Default 4-byte UInt32
                        bytes = BitConverter.GetBytes((uint)num);

                    if (BitConverter.IsLittleEndian && bytes.Length > 1)
                        Array.Reverse(bytes);

                    return bytes;
                }
                catch { }
            }

            // Try hex (e.g. "0A0B0C" or "0x0a0b0c")
            string hex = s;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hex = s.Substring(2);
            bool isHex = hex.Length % 2 == 0;
            foreach (char c in hex)
            {
                if (!Uri.IsHexDigit(c)) { isHex = false; break; }
            }
            if (isHex)
            {
                try
                {
                    int byteLen = hex.Length / 2;
                    var result = new byte[byteLen];
                    for (int i = 0; i < byteLen; i++)
                        result[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                    return result;
                }
                catch { }
            }

            // For Transform/BoundingBox: list of floats
            if ((name.Equals("Transform", StringComparison.OrdinalIgnoreCase) ||
                 name.Equals("BoundingBox", StringComparison.OrdinalIgnoreCase)))
            {
                var parts = s.Replace(",", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var floats = new List<float>();
                foreach (var p in parts)
                {
                    if (float.TryParse(p, out float vv))
                        floats.Add(vv);
                }
                using var ms = new MemoryStream();
                foreach (var f in floats)
                {
                    var bytes = BitConverter.GetBytes(f);
                    if (BitConverter.IsLittleEndian)
                        Array.Reverse(bytes);
                    ms.Write(bytes, 0, 4);
                }
                return ms.ToArray();
            }

            // Otherwise: UTF-8 with 4-byte big-endian length prefix
            var strBytes = Encoding.UTF8.GetBytes(s);
            using var msLen = new MemoryStream();
            {
                uint len = (uint)strBytes.Length;
                var lenBytes = BitConverter.GetBytes(len);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(lenBytes);
                msLen.Write(lenBytes, 0, 4);
                msLen.Write(strBytes, 0, strBytes.Length);
            }
            return msLen.ToArray();
        }

        private uint ReadUInt32FromBytes(byte[] arr, int offset)
        {
            var temp = new byte[4];
            Array.Copy(arr, offset, temp, 0, 4);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(temp);
            return BitConverter.ToUInt32(temp, 0);
        }

        private float ReadFloatFromBytes(byte[] arr, int offset)
        {
            var temp = new byte[4];
            Array.Copy(arr, offset, temp, 0, 4);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(temp);
            return BitConverter.ToSingle(temp, 0);
        }

        #endregion

        // Simple class to bind (Key, Value) with original length
        private class AttributeItem
        {
            public string Key { get; set; }
            public string Value { get; set; }
            public int OriginalLength { get; set; }
            public PSSGAttribute Source { get; set; }
        }
    }
}
