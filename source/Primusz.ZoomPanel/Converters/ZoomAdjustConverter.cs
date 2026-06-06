using System;
using System.Windows.Data;
using System.Globalization;
using System.Windows.Markup;

namespace Primusz.ZoomPanel.Converters
{
    public class ZoomAdjustConverter : MarkupExtension, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var dvalue = 0.0;

            if (value != null)
            {
                dvalue = (double)value;
            }
            return Math.Log(dvalue);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var dvalue = 0.0;

            if (value != null)
            {
                dvalue = (double)value;
            }
            return Math.Exp(dvalue);
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return this;
        }
    }
}