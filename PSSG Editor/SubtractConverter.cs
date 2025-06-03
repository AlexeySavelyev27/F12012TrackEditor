using System;
using System.Globalization;
using System.Windows.Data;

namespace PSSGEditor
{
    /// <summary>
    /// Converter that subtracts the second value from the first.
    /// Expects two double values and returns the difference.
    /// </summary>
    public class SubtractConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return 0d;

            if (values[0] is double a && values[1] is double b)
                return Math.Max(a - b, 0d);

            double da = 0d, db = 0d;
            if (values[0] != null)
                double.TryParse(values[0].ToString(), out da);
            if (values[1] != null)
                double.TryParse(values[1].ToString(), out db);
            return Math.Max(da - db, 0d);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
