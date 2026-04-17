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
    /// Interaction logic for UC_TI_Artifact.xaml
    /// </summary>
    public partial class UC_TI_Artifact : UserControl
    {
        internal string name;

        public UC_TI_Artifact(string name)
        {
            this.name = name;

            InitializeComponent();

            lName.Content = name;
        }
    }
}
