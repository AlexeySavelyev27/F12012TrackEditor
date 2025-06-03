using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.ComponentModel;
using System.Linq;

namespace PSSGEditor
{
    public partial class MainWindow : Window
    {
        private PSSGNode rootNode;
        private Dictionary<TreeViewItem, PSSGNode> nodeMapping = new();
        private PSSGNode currentNode;

        // Чтобы сохранить вертикальный offset ScrollViewer до редактирования
        private double savedVerticalOffset = 0;

        // Для запоминания сортировки
        private string savedSortMember = null;
        private ListSortDirection? savedSortDirection = null;
        private bool isEditing = false;

        // Для установки каретки после двойного клика
        private Point? pendingCaretPoint = null;
        private DataGridCell pendingCaretCell = null;

        public MainWindow()
        {
            InitializeComponent();

            // Окончание редактирования – сохраняем данные
            AttributesDataGrid.CellEditEnding += AttributesDataGrid_CellEditEnding;

            // Запоминаем новые параметры сортировки
            AttributesDataGrid.Sorting += AttributesDataGrid_Sorting;

            // Обработчик PreparingCellForEdit привязан в XAML
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

            if (rootNode != null)
            {
                AddNodeToTree(rootNode, PssgTreeView.Items);
            }
        }

        private void AddNodeToTree(PSSGNode node, ItemCollection parentItems)
        {
            var tvi = new TreeViewItem { Header = node.Name };
            parentItems.Add(tvi);
            nodeMapping[tvi] = node;
            if (node.Children != null && node.Children.Count > 0)
            {
                // “Заглушка” для ленивой загрузки
                tvi.Items.Add(null);
                tvi.Expanded += TreeViewItem_Expanded;
            }
        }

        private void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
        {
            var tvi = (TreeViewItem)sender;
            // Если первый дочерний – null, значит нужно загрузить реальных детей
            if (tvi.Items.Count == 1 && tvi.Items[0] == null)
            {
                tvi.Items.Clear();
                var node = nodeMapping[tvi];
                foreach (var child in node.Children)
                {
                    AddNodeToTree(child, tvi.Items);
                }
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
            if (PssgTreeView.SelectedItem == null) return;
            var selectedItem = (TreeViewItem)PssgTreeView.SelectedItem;
            if (!nodeMapping.ContainsKey(selectedItem)) return;

            currentNode = nodeMapping[selectedItem];
            ShowNodeContent(currentNode);
        }

        private void ShowNodeContent(PSSGNode node)
        {
            // Очищаем старые данные
            AttributesDataGrid.ItemsSource = null;

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
                string rawDisplay = BytesToDisplay("Raw Data", node.Data);
                int origLen = node.Data.Length;
                listForGrid.Add(new AttributeItem
                {
                    Key = "Raw Data",
                    Value = rawDisplay,
                    OriginalLength = origLen
                });
            }

            // Даже если список пуст, DataGrid остаётся видим
            AttributesDataGrid.ItemsSource = listForGrid;

            // Пересчитаем ширину столбца "Attribute" под новые данные
            if (AttributesDataGrid.Columns.Count > 0)
            {
                var attrCol = AttributesDataGrid.Columns[0];
                attrCol.Width = DataGridLength.SizeToCells;
                attrCol.Width = DataGridLength.Auto;
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

            // DataGrid всегда видим
            AttributesDataGrid.Visibility = Visibility.Visible;
        }

        #endregion

        #region Converters (Bytes ↔ Display String)

        private string BytesToDisplay(string name, byte[] b)
        {
            if (b == null) return string.Empty;

            // Попробуем, есть ли 4-byte big-endian префикс длины (UTF-8)
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

            // Попробуем весь массив как UTF-8
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

            // Transform/BoundingBox: массив float
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

            // Если ровно 1, 2 или 4 байта – как число
            if (b.Length == 1)
                return b[0].ToString();
            if (b.Length == 2)
                return BitConverter.ToUInt16(b, 0).ToString();
            if (b.Length == 4)
                return BitConverter.ToUInt32(b, 0).ToString();

            // Иначе – hex-строка
            return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
        }

        private byte[] DisplayToBytes(string name, string s, int originalLength)
        {
            // Число
            if (ulong.TryParse(s, out ulong num))
            {
                try
                {
                    if (originalLength == 1)
                        return new byte[] { (byte)num };
                    if (originalLength == 2)
                        return BitConverter.GetBytes((ushort)num);
                    // По умолчанию – 4-byte UInt32
                    return BitConverter.GetBytes((uint)num);
                }
                catch { }
            }

            // Hex (например, "0A0B0C" или "0x0a0b0c")
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

            // Transform/BoundingBox: список float’ов
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

            // Иначе UTF-8 строка с 4-byte big-endian префиксом длины
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

        #region Editing Handlers

        private void AttributesDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (currentNode == null) return;
            var item = (AttributeItem)e.Row.Item;
            string attrName = item.Key;

            var element = e.EditingElement as TextBox;
            if (element == null) return;
            string newText = element.Text;

            byte[] newBytes;

            if (attrName == "Raw Data")
            {
                newBytes = DisplayToBytes(attrName, newText, item.OriginalLength);
                currentNode.Data = newBytes;
            }
            else
            {
                if (currentNode.Attributes.ContainsKey(attrName))
                {
                    newBytes = DisplayToBytes(attrName, newText, item.OriginalLength);
                    currentNode.Attributes[attrName] = newBytes;
                }
                else
                {
                    return;
                }
            }

            // Обновляем OriginalLength и Value для следующего редактирования
            item.OriginalLength = newBytes.Length;
            item.Value = newText;

            isEditing = false;
        }

        /// <summary>
        /// Клик по ячейке “Attribute”: 
        ///   1) полностью снимаем текущее выделение, 
        ///   2) переводим фокус и выбор на колонку Value; 
        ///   3) переходим в режим редактирования,
        ///   4) и отменяем выделение у первой ячейки (Attribute).
        /// </summary>
        private void AttributesDataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var depObj = (DependencyObject)e.OriginalSource;
            while (depObj != null && depObj is not DataGridCell)
                depObj = VisualTreeHelper.GetParent(depObj);

            if (depObj == null)
            {
                // Клик не по ячейке – снимаем выделение
                AttributesDataGrid.UnselectAllCells();
                Keyboard.ClearFocus();
                return;
            }

            if (depObj is DataGridCell cell)
            {
                // Если клик по первому столбцу (Attribute)
                if (cell.Column.DisplayIndex == 0)
                {
                    var item = cell.DataContext;
                    var valueColumn = AttributesDataGrid.Columns
                        .FirstOrDefault(c => c.Header.ToString() == "Value");
                    if (valueColumn != null)
                    {
                        // Снимаем всё текущее выделение
                        AttributesDataGrid.SelectedCells.Clear();

                        // Создаём DataGridCellInfo для ячейки “Value” в той же строке
                        var cellInfo = new DataGridCellInfo(item, valueColumn);
                        AttributesDataGrid.CurrentCell = cellInfo;
                        AttributesDataGrid.SelectedCells.Add(cellInfo);

                        // Перемещаем фокус на DataGrid, чтобы выделение было активным
                        AttributesDataGrid.Focus();

                        // НЕ вызываем BeginEdit() — оставляем лишь выделение
                        e.Handled = true; // предотвращаем стандартное выделение ячейки "Attribute"
                    }
                }
            }
        }

        /// <summary>
        /// Двойной клик по ячейке “Value”:
        ///   1) перед переходом в edit-mode сохраняем scroll‐offset из CellTemplate,
        ///   2) начинаем редактирование (BeginEdit),
        ///   3) в PreparingCellForEdit восстановим scroll внутри TextBox.
        /// </summary>
        private void AttributesDataGrid_CellMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var cell = sender as DataGridCell;
            if (cell != null && cell.Column.DisplayIndex == 1)
            {
                // 1) Найти ScrollViewer из CellTemplate и сохранить offset
                var contentPresenter = FindVisualChild<ContentPresenter>(cell);
                if (contentPresenter != null)
                {
                    var sv = FindVisualChild<ScrollViewer>(contentPresenter);
                    if (sv != null)
                    {
                        savedVerticalOffset = sv.VerticalOffset;
                    }
                }

                // Запомним позицию двойного клика для установки каретки
                pendingCaretPoint = e.GetPosition(cell);
                pendingCaretCell = cell;

                // 2) Снимаем текущее выделение и переводим на эту же ячейку, но в режим редактирования
                AttributesDataGrid.UnselectAllCells();
                var cellInfo = new DataGridCellInfo(cell.DataContext, cell.Column);
                AttributesDataGrid.CurrentCell = cellInfo;
                AttributesDataGrid.BeginEdit();
                isEditing = true;

                e.Handled = true;
            }
        }

        /// <summary>
        /// PreparingCellForEdit: когда DataGrid создаёт TextBox, здесь восстанавливаем scroll в TextBox.
        /// </summary>
        private void AttributesDataGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            if (e.Column.DisplayIndex == 1)
            {
                isEditing = true;
                // EditingElement – это уже сгенерированный TextBox
                if (e.EditingElement is TextBox tb)
                {
                    // Ищем родительский ScrollViewer в VisualTree (тот, что мы задали в шаблоне)
                    var sv = FindVisualParent<ScrollViewer>(tb);

                    // Если запомнили точку двойного клика – ставим каретку туда
                    if (pendingCaretPoint.HasValue && pendingCaretCell != null)
                    {
                        Point pt = pendingCaretCell.TranslatePoint(pendingCaretPoint.Value, tb);
                        int charIndex = tb.GetCharacterIndexFromPoint(pt, true);
                        if (charIndex < 0 || charIndex >= tb.Text.Length - 1)
                            charIndex = tb.Text.Length;
                        tb.CaretIndex = charIndex;
                        tb.SelectionLength = 0;
                        pendingCaretPoint = null;
                        pendingCaretCell = null;
                    }

                    if (sv != null)
                    {
                        // Восстанавливаем скролл уже после установки каретки
                        tb.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            sv.ScrollToVerticalOffset(savedVerticalOffset);
                        }), DispatcherPriority.Background);
                    }

                    // Разрешаем клики ставить курсор без выделения
                    tb.PreviewMouseLeftButtonDown += ValueTextBox_PreviewMouseLeftButtonDown;
                }
            }
        }

        /// <summary>
        /// При нажатии Enter – коммитим редактирование ячейки.
        /// </summary>
        private void AttributesDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (isEditing && AttributesDataGrid.CurrentCell.IsValid)
                {
                    AttributesDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                    AttributesDataGrid.UnselectAllCells();
                    Keyboard.ClearFocus();
                    isEditing = false;
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape)
            {
                if (isEditing)
                {
                    AttributesDataGrid.CancelEdit();
                    isEditing = false;
                }

                AttributesDataGrid.UnselectAllCells();
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        }

        /// <summary>
        /// Клик по правой панели вне DataGrid – завершаем редактирование и снимаем выделение.
        /// </summary>
        private void RightPanel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var depObj = (DependencyObject)e.OriginalSource;
            if (FindVisualParent<DataGrid>(depObj) == AttributesDataGrid)
                return;

            if (isEditing && AttributesDataGrid.CurrentCell.IsValid)
            {
                AttributesDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                isEditing = false;
            }

            AttributesDataGrid.UnselectAllCells();
            Keyboard.ClearFocus();
        }

        /// <summary>
        /// При сортировке – сохраняем текущий столбец и направление.
        /// </summary>
        private void AttributesDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            ListSortDirection newDirection = e.Column.SortDirection != ListSortDirection.Ascending
                ? ListSortDirection.Ascending
                : ListSortDirection.Descending;

            savedSortMember = e.Column.SortMemberPath;
            savedSortDirection = newDirection;
            // Возвращаем фокус на дерево нодов, чтобы сохраниться выделение
            PssgTreeView.Focus();
            // Даем WPF выполнить сортировку самостоятельно
        }

        /// <summary>
        /// Если клик происходит в правой панели НЕ по TextBox (то есть вне поля Value),
        /// снимаем все выделения и очищаем фокус, чтобы не оставался “чёрный” контур.
        /// </summary>
        private void AttributesDataGrid_PreviewMouseLeftButtonDown_OutsideValue(object sender, MouseButtonEventArgs e)
        {
            var depObj = (DependencyObject)e.OriginalSource;
            // Если клик в TextBox (режим редактирования Value) → выходим, не снимая выделение
            while (depObj != null)
            {
                if (depObj is TextBox)
                    return;
                if (depObj is DataGridCell)
                    break;
                depObj = VisualTreeHelper.GetParent(depObj);
            }

            // Клик не в TextBox → снимаем выделение и очищаем фокус
            AttributesDataGrid.UnselectAllCells();
            Keyboard.ClearFocus();
        }

        /// <summary>
        /// В TextBox (режим редактирования Value) при клике ставим курсор в позицию клика, 
        /// не выделяя весь текст. 
        /// </summary>
        private void ValueTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var tb = (TextBox)sender;
            if (!tb.IsKeyboardFocusWithin)
            {
                e.Handled = true; // предотвращаем автоматическое выделение всего текста
                tb.Focus();

                // Вычисляем индекс символа по позиции клика
                Point clickPos = e.GetPosition(tb);
                int charIndex = tb.GetCharacterIndexFromPoint(clickPos, true);
                if (charIndex < 0 || charIndex >= tb.Text.Length - 1)
                    charIndex = tb.Text.Length;
                tb.CaretIndex = charIndex;
            }
        }

        private void ValueScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var dep = (DependencyObject)e.OriginalSource;
            if (FindVisualParent<ScrollBar>(dep) != null)
            {
                // Нажатие на скроллбар не должно выделять ячейку, при этом сохраняя прокрутку
                e.Handled = true;
            }
        }

        #endregion

        #region Helper Methods: поиск визуальных потомков/родителей

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

        #endregion

        // Класс для привязки пары (Key, Value) с оригинальной длиной
        private class AttributeItem
        {
            public string Key { get; set; }
            public string Value { get; set; }
            public int OriginalLength { get; set; }
        }
    }
}
