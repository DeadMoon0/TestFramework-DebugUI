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

namespace TestFrameworkDebugUI.Controls.TimelineBoard.Timeline
{
    /// <summary>
    /// Interaction logic for UC_StageMarker.xaml
    /// </summary>
    public partial class UC_StageMarker : UserControl
    {
        public UC_StageMarker(string name, string description)
        {
            InitializeComponent();
            lName.Content = name;
            lDescription.Content = description;
        }
    }
}
