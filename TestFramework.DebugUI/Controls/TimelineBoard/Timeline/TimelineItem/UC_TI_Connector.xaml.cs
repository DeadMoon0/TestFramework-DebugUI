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

namespace TestFrameworkDebugUI.Controls.TimelineBoard.Timeline.TimelineItem
{
    /// <summary>
    /// Interaction logic for UC_TI_Connector.xaml
    /// </summary>
    public partial class UC_TI_Connector : UserControl
    {
        public bool IsSend
        {
            get { return (bool)GetValue(IsSendProperty); }
            set { SetValue(IsSendProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IsSend.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IsSendProperty =
            DependencyProperty.Register(nameof(IsSend), typeof(bool), typeof(UC_TI_Connector), new PropertyMetadata(false, (s, e) => ((UC_TI_Connector)s).OnIsSendChange()));

        public UC_TI_Connector()
        {
            InitializeComponent();
        }

        private void OnIsSendChange()
        {
            bRecv.Visibility = IsSend ? Visibility.Collapsed : Visibility.Visible;
            bSend.Visibility = IsSend ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
