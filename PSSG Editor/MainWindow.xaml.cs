using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Buffers.Binary;
using System.Buffers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Linq;
using Pfim;

namespace PSSGEditor
{
    public partial class MainWindow : Window
    {
        private PSSGNode rootNode;
        private PSSGNode currentNode;
        private int rawDataOriginalLength = 0;
        private bool isLoadingRawData = false;

        // Чтобы сохранить вертикальный offset ScrollViewer до редактирования
        private double savedVerticalOffset = 0;

        // Для запоминания сортировки
        private string savedSortMember = null;
        private ListSortDirection? savedSortDirection = null;

        private List<TextureEntry> textureEntries = new();

        public MainWindow()
        {
            InitializeComponent();

            // Окончание редактирования – сохраняем данные
            AttributesDataGrid.CellEditEnding += AttributesDataGrid_CellEditEnding;

            // Запоминаем новые параметры сортировки
            AttributesDataGrid.Sorting += AttributesDataGrid_Sorting;

            // Обработчик PreparingCellForEdit привязан в XAML
            TexturesListBox.SelectionChanged += TexturesListBox_SelectionChanged;
        }

        #region Menu Handlers

        private async void OpenMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "PSSG/.ens files (*.pssg;*.ens)|*.pssg;*.ens",
                Title = "Open PSSG File"
            };
            if (ofd.ShowDialog() != true) return;

            await LoadFileAsync(ofd.FileName);
        }

        public async Task LoadFileAsync(string fileName)
        {
            StatusText.Text = "Loading...";
            try
            {
                var node = await Task.Run(() =>
                {
                    var parser = new PSSGParser(fileName);
                    return parser.Parse();
                });

                rootNode = node;

                var stats = CollectStats(rootNode);
                StatusText.Text = $"Nodes: {stats.nodes}, Meshes: {stats.meshes}, Textures: {stats.textures}";

                PopulateTreeView();
                PopulateTexturesList();
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

        private void PopulateTexturesList()
        {
            textureEntries.Clear();
            TexturesListBox.ItemsSource = null;
            if (rootNode == null)
                return;

            foreach (var tex in EnumerateTextures(rootNode))
            {
                string id = GetStringAttr(tex, "id");
                textureEntries.Add(new TextureEntry { Id = id, Node = tex });
            }

            TexturesListBox.ItemsSource = textureEntries;
            TexturesListBox.DisplayMemberPath = nameof(TextureEntry.Id);
        }

        private IEnumerable<PSSGNode> EnumerateTextures(PSSGNode root)
        {
            var stack = new Stack<PSSGNode>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (string.Equals(n.Name, "TEXTURE", StringComparison.OrdinalIgnoreCase))
                    yield return n;
                foreach (var c in n.Children)
                    stack.Push(c);
            }
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

        #endregion

        #region Converters (Bytes ↔ Display String)

        private string BytesToDisplay(string name, byte[] b)
        {
            if (b == null) return string.Empty;

            // Special handling for raw bytes shown in the Raw Data panel
            if (name == "__data__" && currentNode != null)
            {
                // Raw data for TRANSFORM or BOUNDINGBOX is treated as a float array
                if ((currentNode.Name.Equals("TRANSFORM", StringComparison.OrdinalIgnoreCase) ||
                     currentNode.Name.Equals("BOUNDINGBOX", StringComparison.OrdinalIgnoreCase)) &&
                    b.Length % 4 == 0)
                {
                    int count = b.Length / 4;
                    var sb = new StringBuilder();
                    for (int i = 0; i < count; i++)
                    {
                        float v = ReadFloatFromBytes(b, i * 4);
                        if (i > 0)
                            sb.Append(' ');
                        sb.Append(v.ToString("F6"));
                    }
                    return sb.ToString();
                }

                // All other raw data blocks are displayed as uppercase hex
                return BitConverter.ToString(b).Replace("-", " ").ToUpperInvariant();
            }

            // 1) Числа маленькой длины
            if (b.Length == 1)
                return b[0].ToString();
            if (b.Length == 2)
                return ReadUInt16FromBytes(b, 0).ToString();
            if (b.Length == 4)
            {
                uint intVal = ReadUInt32FromBytes(b, 0);
                float fVal = BitConverter.Int32BitsToSingle((int)intVal);
                uint exp = (intVal >> 23) & 0xFF;
                if (exp != 0 && exp != 0xFF && !float.IsNaN(fVal) && !float.IsInfinity(fVal) &&
                    Math.Abs(fVal) > 1e-6f && Math.Abs(fVal) < 1e6f)
                {
                    return fVal.ToString("F6");
                }
                return intVal.ToString();
            }

            // 2) length-prefixed UTF-8 string
            if (b.Length > 4)
            {
                uint sz = ReadUInt32FromBytes(b, 0);
                if (sz == b.Length - 4)
                {
                    try
                    {
                        return Encoding.UTF8.GetString(b, 4, (int)sz);
                    }
                    catch { }
                }
            }

            // 3) Transform/BoundingBox attribute: массив float
            if ((name.Equals("Transform", StringComparison.OrdinalIgnoreCase) ||
                 name.Equals("BoundingBox", StringComparison.OrdinalIgnoreCase)) &&
                b.Length % 4 == 0)
            {
                int count = b.Length / 4;
                var sb = new StringBuilder();
                for (int i = 0; i < count; i++)
                {
                    float v = ReadFloatFromBytes(b, i * 4);
                    if (i > 0)
                        sb.Append(' ');
                    sb.Append(v.ToString("F6"));
                }
                return sb.ToString();
            }

            // 4) Попытка трактовать как печатаемую UTF-8 строку
            try
            {
                string txt = Encoding.UTF8.GetString(b);
                if (txt.All(c => c >= 32 && c < 127))
                    return txt;
            }
            catch { }

            // 5) fallback – hex-строка
            return Convert.ToHexString(b).ToLowerInvariant();
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
                        return ToBigEndian((ushort)num);
                    // По умолчанию – 4-byte UInt32
                    return ToBigEndian((uint)num);
                }
                catch { }
            }

            // Hex (например, "0A 0B 0C" или "0x0a0b0c")
            string hex = s.Trim();
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hex = hex.Substring(2);
            hex = hex.Replace(" ", string.Empty)
                     .Replace("\n", string.Empty)
                     .Replace("\r", string.Empty);
            bool isHex = hex.Length % 2 == 0 && hex.All(Uri.IsHexDigit);
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

            // Single float value when original length is 4 bytes
            if (originalLength == 4 && float.TryParse(s, out float singleVal))
            {
                uint bits = BitConverter.SingleToUInt32Bits(singleVal);
                if (BitConverter.IsLittleEndian)
                    bits = BinaryPrimitives.ReverseEndianness(bits);
                var arr = new byte[4];
                BinaryPrimitives.WriteUInt32BigEndian(arr, bits);
                return arr;
            }

            // Transform/BoundingBox: список float’ов
            if (name.Equals("Transform", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("BoundingBox", StringComparison.OrdinalIgnoreCase) ||
                (name == "__data__" && currentNode != null &&
                 (currentNode.Name.Equals("TRANSFORM", StringComparison.OrdinalIgnoreCase) ||
                  currentNode.Name.Equals("BOUNDINGBOX", StringComparison.OrdinalIgnoreCase))))
            {
                var parts = s.Replace(",", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var floats = new List<float>();
                foreach (var p in parts)
                {
                    if (float.TryParse(p, out float vv))
                        floats.Add(vv);
                }
                var result = new byte[floats.Count * 4];
                for (int i = 0; i < floats.Count; i++)
                {
                    uint bits = BitConverter.SingleToUInt32Bits(floats[i]);
                    if (BitConverter.IsLittleEndian)
                        bits = BinaryPrimitives.ReverseEndianness(bits);
                    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(i * 4), bits);
                }
                return result;
            }

            // Иначе UTF-8 строка с 4-byte big-endian префиксом длины
            var strBytes = Encoding.UTF8.GetBytes(s);
            var rented = ArrayPool<byte>.Shared.Rent(strBytes.Length + 4);
            BinaryPrimitives.WriteUInt32BigEndian(rented.AsSpan(), (uint)strBytes.Length);
            strBytes.CopyTo(rented.AsSpan(4));
            var final = rented.AsSpan(0, strBytes.Length + 4).ToArray();
            ArrayPool<byte>.Shared.Return(rented);
            return final;
        }

        private uint ReadUInt32FromBytes(byte[] arr, int offset)
        {
            return BinaryPrimitives.ReadUInt32BigEndian(arr.AsSpan(offset));
        }

        private ushort ReadUInt16FromBytes(byte[] arr, int offset)
        {
            return BinaryPrimitives.ReadUInt16BigEndian(arr.AsSpan(offset));
        }

        private float ReadFloatFromBytes(byte[] arr, int offset)
        {
            uint intVal = BinaryPrimitives.ReadUInt32BigEndian(arr.AsSpan(offset));
            return BitConverter.Int32BitsToSingle((int)intVal);
        }

        private byte[] ToBigEndian(ushort value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return bytes;
        }

        private byte[] ToBigEndian(uint value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return bytes;
        }

        private string GetStringAttr(PSSGNode node, string name)
        {
            if (node.Attributes.TryGetValue(name, out var b) && b.Length > 4)
            {
                uint len = ReadUInt32FromBytes(b, 0);
                if (len <= b.Length - 4)
                    return Encoding.UTF8.GetString(b, 4, (int)len);
            }
            return string.Empty;
        }

        private int GetIntAttr(PSSGNode node, string name)
        {
            if (node.Attributes.TryGetValue(name, out var b) && b.Length >= 4)
                return (int)ReadUInt32FromBytes(b, 0);
            return 0;
        }

        private byte[]? GetTextureData(PSSGNode texture)
        {
            var imgBlock = texture.Children.FirstOrDefault(c => c.Name.Equals("TEXTUREIMAGEBLOCK", StringComparison.OrdinalIgnoreCase));
            var dataNode = imgBlock?.Children.FirstOrDefault(c => c.Name.Equals("TEXTUREIMAGEBLOCKDATA", StringComparison.OrdinalIgnoreCase));
            return dataNode?.Data;
        }

        #endregion

        #region Editing Handlers

        private void AttributesDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (currentNode == null) return;
            // If the edit was cancelled (e.g. by pressing Esc), do not update the data
            if (e.EditAction != DataGridEditAction.Commit)
                return;
            var item = (AttributeItem)e.Row.Item;
            string attrName = item.Key;

            var element = e.EditingElement as TextBox;
            if (element == null) return;
            string newText = element.Text;

            byte[] newBytes;

            if (attrName == "__data__")
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
            while (depObj != null && !(depObj is DataGridCell))
                depObj = VisualTreeHelper.GetParent(depObj);

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

                // 2) Снимаем текущее выделение и переводим на эту же ячейку, но в режим редактирования
                AttributesDataGrid.UnselectAllCells();
                var cellInfo = new DataGridCellInfo(cell.DataContext, cell.Column);
                AttributesDataGrid.CurrentCell = cellInfo;
                AttributesDataGrid.BeginEdit();

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
                // EditingElement – это уже сгенерированный TextBox
                if (e.EditingElement is TextBox tb)
                {
                    // Ищем родительский ScrollViewer в VisualTree (тот, что мы задали в шаблоне)
                    var sv = FindVisualParent<ScrollViewer>(tb);
                    if (sv != null)
                    {
                        sv.ScrollToVerticalOffset(savedVerticalOffset);
                    }
                }
            }
        }

        /// <summary>
        /// Обработка Enter/Escape во время редактирования.
        /// Enter – сохранить, Escape – отменить или снять выделение.
        /// </summary>
        private void AttributesDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Escape)
            {
                var tb = Keyboard.FocusedElement as TextBox;
                if (tb != null)
                {
                    if (e.Key == Key.Enter)
                    {
                        if (AttributesDataGrid.CurrentCell.IsValid)
                            AttributesDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                    }
                    else // Escape
                    {
                        AttributesDataGrid.CancelEdit(DataGridEditingUnit.Cell);
                    }
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    AttributesDataGrid.UnselectAllCells();
                    Keyboard.ClearFocus();
                    e.Handled = true;
                }
            }
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
                if (charIndex < 0)
                    charIndex = tb.Text.Length;
                tb.CaretIndex = charIndex;
            }
        }


        #endregion

        #region Textures Tab

        private void TexturesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TexturesListBox.SelectedItem is TextureEntry entry)
                ShowTexture(entry);
        }

        private void ShowTexture(TextureEntry entry)
        {
            var data = GetTextureData(entry.Node);
            if (data == null)
            {
                TexturePreview.Source = null;
                return;
            }

            int width = GetIntAttr(entry.Node, "width");
            int height = GetIntAttr(entry.Node, "height");
            int mipLevels = GetIntAttr(entry.Node, "numberMipMapLevels");
            if (mipLevels == 0) mipLevels = 1;
            string format = GetStringAttr(entry.Node, "texelFormat").ToUpperInvariant();

            uint fourCC = format switch
            {
                "DXT1" => 0x31545844u,
                "DXT3" => 0x33545844u,
                "DXT5" => 0x35545844u,
                _ => 0u
            };

            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, Encoding.ASCII, true))
            {
                bw.Write(Encoding.ASCII.GetBytes("DDS "));
                bw.Write(124u);
                uint flags = 0x0002100Fu; // CAPS | HEIGHT | WIDTH | PIXELFORMAT | LINEARSIZE
                if (mipLevels > 1)
                    flags |= 0x00020000u;
                bw.Write(flags);
                bw.Write((uint)height);
                bw.Write((uint)width);
                uint topSize;
                if (fourCC != 0)
                {
                    int block = fourCC == 0x31545844u ? 8 : 16;
                    topSize = (uint)(((width + 3) / 4) * ((height + 3) / 4) * block);
                }
                else
                {
                    topSize = (uint)data.Length;
                }
                bw.Write(topSize);
                bw.Write(0u);
                bw.Write((uint)mipLevels);
                for (int i = 0; i < 11; i++) bw.Write(0u);
                bw.Write(32u);
                bw.Write(0x00000004u);
                bw.Write(fourCC);
                bw.Write(0u); bw.Write(0u); bw.Write(0u); bw.Write(0u);
                bw.Write(0x1000u);
                bw.Write(0u); bw.Write(0u); bw.Write(0u); bw.Write(0u);
            }
            ms.Write(data, 0, data.Length);
            ms.Position = 0;

            try
            {
                using var img = Pfimage.FromStream(ms);
                if (img.Compressed)
                    img.Decompress();

                byte[] data = img.Data;
                System.Windows.Media.PixelFormat pf;

                switch (img.Format)
                {
                    case ImageFormat.Rgba32:
                        SwapRedBlue(data, 4);
                        pf = PixelFormats.Bgra32;
                        break;
                    case ImageFormat.Rgb24:
                        SwapRedBlue(data, 3);
                        pf = PixelFormats.Bgr24;
                        break;
                    case ImageFormat.Rgb8:
                        pf = PixelFormats.Gray8;
                        break;
                    default:
                        pf = PixelFormats.Bgra32;
                        break;
                }

                var bmp = BitmapSource.Create(img.Width, img.Height, 96, 96, pf, null, data, img.Stride);
                bmp.Freeze();
                TexturePreview.Source = bmp;
            }
            catch
            {
                TexturePreview.Source = null;
            }
        }

        private static void SwapRedBlue(byte[] buffer, int bpp)
        {
            for (int i = 0; i < buffer.Length - (bpp - 1); i += bpp)
            {
                byte tmp = buffer[i];
                buffer[i] = buffer[i + 2];
                buffer[i + 2] = tmp;
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

        private class TextureEntry
        {
            public string Id { get; set; }
            public PSSGNode Node { get; set; }
        }
    }
}
