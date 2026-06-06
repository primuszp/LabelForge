namespace Primusz.ZoomPanel.Helpers
{
    internal static class DoubleHelper
    {
        public static double ToRealNumber(this double value, double defaultValue = 0)
        {
            return double.IsInfinity(value) || double.IsNaN(value) ? defaultValue : value;
        }
    }
}