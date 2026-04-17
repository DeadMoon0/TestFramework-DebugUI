using Newtonsoft.Json;
using System;
using TestFramework.DebugUI.PipeAdapter.ProtocolModels;

namespace TestFramework.DebugUI.PipeAdapter;

internal static class SignalFactory
{
    internal static ISignal DeserializeSignal(string json)
    {
        SignalKind signalKind = (JsonConvert.DeserializeAnonymousType(json, new { Kind = (SignalKind)0 }) ?? throw new InvalidOperationException("Could not Deserialize Signal: " + json)).Kind;
        switch (signalKind)
        {
            case SignalKind.ArtifactUpdate:
                return JsonConvert.DeserializeObject<ArtifactUpdateSignal>(json) ?? throw new InvalidOperationException("Could not Deserialize Signal: " + json);
            case SignalKind.InitTimelineRun:
                return JsonConvert.DeserializeObject<InitTimelineRunSignal>(json) ?? throw new InvalidOperationException("Could not Deserialize Signal: " + json);
            case SignalKind.StageBegin:
                return JsonConvert.DeserializeObject<StageBeginSignal>(json) ?? throw new InvalidOperationException("Could not Deserialize Signal: " + json);
            case SignalKind.StepBegin:
                return JsonConvert.DeserializeObject<StepBeginSignal>(json) ?? throw new InvalidOperationException("Could not Deserialize Signal: " + json);
            case SignalKind.StepResultChange:
                return JsonConvert.DeserializeObject<StepResultChangeSignal>(json) ?? throw new InvalidOperationException("Could not Deserialize Signal: " + json);
            case SignalKind.TimelineRunFinished:
                return JsonConvert.DeserializeObject<TimelineRunFinishedSignal>(json) ?? throw new InvalidOperationException("Could not Deserialize Signal: " + json);
            case SignalKind.VariableUpdate:
                return JsonConvert.DeserializeObject<VariableUpdateSignal>(json) ?? throw new InvalidOperationException("Could not Deserialize Signal: " + json);
            case SignalKind.BreakpointHitRequest:
                return JsonConvert.DeserializeObject<BreakpointHitRequestSignal>(json) ?? throw new InvalidOperationException("Could not Deserialize Signal: " + json);
            case SignalKind.BreakpointHitContinue:
                return JsonConvert.DeserializeObject<BreakpointHitContinueSignal>(json) ?? throw new InvalidOperationException("Could not Deserialize Signal: " + json);
            default: throw new ArgumentOutOfRangeException(nameof(signalKind), signalKind, null);
        }
    }
}