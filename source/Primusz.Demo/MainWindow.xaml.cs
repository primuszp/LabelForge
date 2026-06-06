using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Primusz.DrawTools;

namespace Primusz.Demo
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            InitializeDrawingCanvas();
        }

        private void LineToolOnClick(object sender, RoutedEventArgs e)
        {
            DrawingCanvas.Tool = (ToolType)Enum.Parse(typeof(ToolType), "Line");
        }

        void InitializeDrawingCanvas()
        {
            DrawingCanvas.LineWidth = 2.0;
            DrawingCanvas.ObjectColor = Colors.Black;

            //DrawingCanvas.TextFontSize = 10.0;
            //DrawingCanvas.TextFontFamilyName = "Arial";
            //DrawingCanvas.TextFontStyle = FontConversions.FontStyleFromString(SettingsManager.ApplicationSettings.TextFontStyle);
            //DrawingCanvas.TextFontWeight = FontConversions.FontWeightFromString(SettingsManager.ApplicationSettings.TextFontWeight);
            //DrawingCanvas.TextFontStretch = FontConversions.FontStretchFromString(SettingsManager.ApplicationSettings.TextFontStretch);
        }

        private void DrawingCanvas_OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            //DrawingCanvas.Focus();
            //Keyboard.Focus(DrawingCanvas);

            //e.Handled = true;
        }

        private void PolyLineToolOnClick(object sender, RoutedEventArgs e)
        {
            DrawingCanvas.Tool = (ToolType)Enum.Parse(typeof(ToolType), "PolyLine");
        }
    }
}
