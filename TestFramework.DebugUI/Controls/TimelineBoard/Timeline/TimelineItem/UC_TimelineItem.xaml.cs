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
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.DebugUI.State;
using TestFrameworkDebugUI.Controls.TimelineBoard.Timeline.StatusIndicator;
using WpfStateService.Callbacks;
using WpfStateService.Common;
using WpfStateService.Graph;

namespace TestFrameworkDebugUI.Controls.TimelineBoard.Timeline.TimelineItem
{
    /// <summary>
    /// Interaction logic for UC_TimelineItem.xaml
    /// </summary>
    public partial class UC_TimelineItem : UserControl
    {
        private readonly int _id;
        private readonly string _stageName;

        public UC_TimelineItem(string stageName, int id, string name, string description)
        {
            this._id = id;
            this._stageName = stageName;

            InitializeComponent();

            lName.Content = name;
            lDescription.Content = description;

            var stepUpdateStatePath = StatePath.For(MainWindow.State).Property(MainState.ActiveRunProperty).Property(RunState.StagesProperty).PropertyKey<StageNodeState>(stageName).Property(StageNodeState.StepsProperty).PropertyKey<StepNodeState>(id + "");
            stepUpdateStatePath.Property(StepNodeState.StateProperty).CallbackAsync(OnStateChange, CallbackFlags.OnNotNull);
            stepUpdateStatePath.Property(StepNodeState.AttemptCountProperty).CallbackAsync(OnAttemptCountChange, CallbackFlags.OnNotNull);
            stepUpdateStatePath.Property(StepNodeState.OutputsProperty).CallbackAsync(OnOutputsChange, CallbackFlags.OnNotNull | CallbackFlags.OnChildChange);
        }

        private async Task OnAttemptCountChange(int attemptCount, int old)
        {
            if (attemptCount <= 0 || old > 0) return;
            gStatusHost.Children.Clear();
            gStatusHost.Children.Add(new UC_SI_InProgress());

            spInput.Children.Clear();
            foreach (IOConnectionState connection in MainWindow.State.ActiveRun!.Stages[_stageName].Steps[_id + ""].Inputs.Values.Where(x => x.HasValue).ToList())
            {
                switch (connection.Kind)
                {
                    case StepIOKind.Artifact:
                        spInput.Children.Add(new UC_TI_Artifact(connection.Name));
                        break;
                    case StepIOKind.Variable:
                        spInput.Children.Add(new UC_TI_Var(connection.Name));
                        break;
                }
            }
        }

        private async Task OnStateChange(StepState state, StepState old)
        {
            gStatusHost.Children.Clear();
            switch (state)
            {
                case StepState.NotRun:
                    gStatusHost.Children.Add(new UC_SI_NotRun());
                    break;
                case StepState.Complete:
                    gStatusHost.Children.Add(new UC_SI_Complete());
                    break;
                case StepState.Timeout:
                    gStatusHost.Children.Add(new UC_SI_Timeout());
                    break;
                case StepState.Error:
                    gStatusHost.Children.Add(new UC_SI_Error());
                    break;
                case StepState.Skipped:
                    gStatusHost.Children.Add(new UC_SI_Skipped());
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(state), state, null);
            }
        }

        private async Task OnOutputsChange(StateDictionary<IOConnectionState> outputs, StateDictionary<IOConnectionState> old)
        {
            foreach (IOConnectionState connection in outputs.Values.Where(x => x.HasValue).ToList())
            {
                if (connection.Kind == StepIOKind.Variable)
                {
                    List<UC_TI_Var> removed = new List<UC_TI_Var>();
                    foreach (var spItem in spOutput.Children)
                    {
                        if (spItem is UC_TI_Var varItem && varItem.name == connection.Name) removed.Add(varItem);
                    }
                    spOutput.Children.Add(new UC_TI_Var(connection.Name));

                    foreach (var item in removed)
                    {
                        spOutput.Children.Remove(item);
                    }
                }

                if (connection.Kind == StepIOKind.Artifact)
                {
                    List<UC_TI_Artifact> removed = new List<UC_TI_Artifact>();
                    foreach (var spItem in spOutput.Children)
                    {
                        if (spItem is UC_TI_Artifact artItem && artItem.name == connection.Name) removed.Add(artItem);
                    }
                    spOutput.Children.Add(new UC_TI_Artifact(connection.Name));

                    foreach (var item in removed)
                    {
                        spOutput.Children.Remove(item);
                    }
                }
            }
        }
    }
}
