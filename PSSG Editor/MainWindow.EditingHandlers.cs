using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PSSGEditor
{
    public partial class MainWindow
    {
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
    }
}

