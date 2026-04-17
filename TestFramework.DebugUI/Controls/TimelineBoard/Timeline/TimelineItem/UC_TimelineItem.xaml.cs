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

            var stepUpdateStatePath = StatePath.For(MainWindow.State).Property(MainState.ActiveRunProperty).Property(RunState.StageUpdatesProperty).PropertyKey<StageUpdateState>(stageName).Property(StageUpdateState.StepUpdatesProperty).PropertyKey<StepUpdateState>(id + "");
            stepUpdateStatePath.Property(StepUpdateState.StateProperty).CallbackAsync(OnStateChange, CallbackFlags.OnNotNull);
            stepUpdateStatePath.Property(StepUpdateState.HasStartedProperty).CallbackAsync(OnHasStartedChange, CallbackFlags.OnNotNull);
            stepUpdateStatePath.Property(StepUpdateState.OutputVariablesProperty).CallbackAsync(OnOutputVariablesChange, CallbackFlags.OnNotNull | CallbackFlags.OnChildChange);
            stepUpdateStatePath.Property(StepUpdateState.OutputArtifactsProperty).CallbackAsync(OnOutputArtifactsChange, CallbackFlags.OnNotNull | CallbackFlags.OnChildChange);
        }

        private async Task OnHasStartedChange(bool hasStarted, bool old)
        {
            if (!hasStarted) return;
            gStatusHost.Children.Clear();
            gStatusHost.Children.Add(new UC_SI_InProgress());

            spInput.Children.Clear();
            foreach (ArtifactState art in MainWindow.State.ActiveRun!.StageUpdates[_stageName].StepUpdates[_id + ""].InputArtifacts.Select(x => x.Value).ToList())
            {
                spInput.Children.Add(new UC_TI_Artifact(art.Key));
            }

            foreach (VariableState var in MainWindow.State.ActiveRun!.StageUpdates[_stageName].StepUpdates[_id + ""].InputVariables.Select(x => x.Value).ToList())
            {
                spInput.Children.Add(new UC_TI_Var(var.Key));
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

        private async Task OnOutputVariablesChange(StateDictionary<VariableState> variableStates, StateDictionary<VariableState> old)
        {
            foreach (var var in variableStates.ToList())
            {
                List<UC_TI_Var> removed = new List<UC_TI_Var>();
                foreach (var spItem in spOutput.Children)
                {
                    if (spItem is UC_TI_Var varItem && varItem.name == var.Key) removed.Add(varItem);
                }
                spOutput.Children.Add(new UC_TI_Var(var.Key));

                foreach (var item in removed)
                {
                    spOutput.Children.Remove(item);
                }
            }
        }

        private async Task OnOutputArtifactsChange(StateDictionary<ArtifactState> artifactStates, StateDictionary<ArtifactState> old)
        {
            foreach (var art in artifactStates.ToList())
            {
                List<UC_TI_Artifact> removed = new List<UC_TI_Artifact>();
                foreach (var spItem in spOutput.Children)
                {
                    if (spItem is UC_TI_Artifact artItem && artItem.name == art.Key) removed.Add(artItem);
                }
                spOutput.Children.Add(new UC_TI_Artifact(art.Key));

                foreach (var item in removed)
                {
                    spOutput.Children.Remove(item);
                }
            }
        }
    }
}
