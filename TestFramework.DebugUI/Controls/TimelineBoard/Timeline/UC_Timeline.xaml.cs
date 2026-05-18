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
using TestFramework.DebugUI.State;
using TestFrameworkDebugUI.Controls.TimelineBoard.Timeline.TimelineItem;
using WpfStateService.Callbacks;
using WpfStateService.Common;
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

            StatePath.For(MainWindow.State).Property(MainState.ActiveRunProperty).Property(RunState.StagesProperty).CallbackAsync(LoadStages, CallbackFlags.OnNotNull);
        }

        private async Task LoadStages(StateDictionary<StageNodeState> stages, StateDictionary<StageNodeState> old)
        {
            spContent.Children.Clear();
            foreach (StageNodeState stage in DebugRunStateQueries.GetOrderedStages(stages))
            {
                spContent.Children.Add(new UC_StageMarker(stage.Name, stage.Description));
                foreach (StepNodeState step in DebugRunStateQueries.GetOrderedSteps(stage))
                {
                    spContent.Children.Add(new UC_TimelineItem(stage.Name, step.StepId, step.Name, step.Description));
                }
            }
        }
    }
}
