using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace WPFCommon.DropDown
{
    /// <summary>
    /// Interaction logic for UC_DropDown.xaml
    /// </summary>
    public partial class UC_DropDown : UserControl
    {
        public StackPanel Content
        {
            get { return (StackPanel)GetValue(ContentProperty); }
            set { SetValue(ContentProperty, value); }
        }

        public static readonly DependencyProperty ContentProperty =
            DependencyProperty.Register("Content", typeof(StackPanel), typeof(UC_DropDown), new PropertyMetadata(null, UpdateOwnContent));

        public static void UpdateOwnContent(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            (d as UC_DropDown).border.Child = (d as UC_DropDown).Content;
        }

        public object Host;

        public UC_DropDown()
        {
            InitializeComponent();
        }

        public StackPanel GetStackPanel(string tag)
        {
            foreach (UserControl item in Content.Children)
            {
                if (item.GetType() == typeof(UC_DD_Header) && (item.Tag as string) == tag) return (StackPanel)(item as UC_DD_Header).bContent.Child ;
            }
            return null;
        }
    }
}