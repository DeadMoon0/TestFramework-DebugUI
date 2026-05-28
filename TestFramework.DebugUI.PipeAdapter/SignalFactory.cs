using Newtonsoft.Json;
using System;
using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.PipeAdapter;

internal static class SignalFactory
{
    internal static ISignal DeserializeSignal(string json)
    {
        return PipeSignalFactory.DeserializeSignal(json);
    }
}