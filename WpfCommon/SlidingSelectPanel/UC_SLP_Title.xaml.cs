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

namespace WPFCommon.SlidingSelectPanel
{
    /// <summary>
    /// Interaction logic for UC_SLP_Title.xaml
    /// </summary>
    public partial class UC_SLP_Title : UserControl
    {
        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(UC_SLP_Title), new PropertyMetadata(""));

        public RoutedEventHandler Click
        {
            get { return (RoutedEventHandler)GetValue(ClickProperty); }
            set { SetValue(ClickProperty, value); }
        }

        public static readonly DependencyProperty ClickProperty =
            DependencyProperty.Register("Click", typeof(RoutedEventHandler), typeof(UC_SLP_Title), new PropertyMetadata(null));

        public UC_SLP_Title()
        {
            InitializeComponent();
        }

        private void button_Click(object sender, RoutedEventArgs e) => Click?.Invoke(this, e);
    }
}
