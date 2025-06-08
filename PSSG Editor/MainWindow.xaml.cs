using Microsoft.Win32;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

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

        public MainWindow()
        {
            InitializeComponent();

            // Окончание редактирования – сохраняем данные
            AttributesDataGrid.CellEditEnding += AttributesDataGrid_CellEditEnding;

            // Запоминаем новые параметры сортировки
            AttributesDataGrid.Sorting += AttributesDataGrid_Sorting;

            // Обработчик PreparingCellForEdit привязан в XAML
        }
    }
}

