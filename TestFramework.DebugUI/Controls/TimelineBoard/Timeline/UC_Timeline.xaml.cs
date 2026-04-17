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
using TestFramework.Core.Debugger;
using TestFramework.Core.Stages;
using TestFramework.Core.Steps;
using TestFramework.DebugUI.State;
using TestFrameworkDebugUI.Controls.TimelineBoard.Timeline.TimelineItem;
using WpfStateService.Graph;

namespace TestFrameworkDebugUI.Controls.TimelineBoard.Timeline
{
    /// <summary>
    /// Interaction logic for UC_Timeline.xaml
    /// </summary>
    public partial class UC_Timeline : UserControl
    {
        public UC_Timeline()
        {
            InitializeComponent();

            StatePath.For(MainWindow.State).Property(MainState.ActiveRunProperty).Property(RunState.StructureProperty).CallbackAsync(LoadStructure, WpfStateService.Callbacks.CallbackFlags.OnNotNull);
        }

        private async Task LoadStructure(TimelineRunStructure structure, TimelineRunStructure old)
        {
            spContent.Children.Clear();
            foreach (DebugStageState stage in structure.Stages)
            {
                spContent.Children.Add(new UC_StageMarker(stage.Name, stage.Description));
                int id = 0;
                foreach (DebugStepState step in stage.Steps)
                {
                    spContent.Children.Add(new UC_TimelineItem(stage.Name, id, step.Name, step.Description));
                    id++;
                }
            }
        }
    }
}
