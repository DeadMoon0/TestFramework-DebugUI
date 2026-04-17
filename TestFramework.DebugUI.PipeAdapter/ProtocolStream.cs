using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.DebugUI.PipeAdapter.ProtocolModels;

namespace TestFramework.DebugUI.PipeAdapter;

internal class ProtocolStream(PipeStream stream)
{
    internal bool PipeIsDead = false;
    private static readonly Encoding Encoding = Encoding.Unicode;

    internal async Task SendSignalAsync(ISignal signal)
    {
        CancellationTokenSource cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(2));

        try
        {
            string json = JsonConvert.SerializeObject(signal);
            byte[] buffer = [.. BitConverter.GetBytes(Encoding.GetByteCount(json)), .. Encoding.GetBytes(json)];
            await stream.WriteAsync(buffer, 0, buffer.Length, cts.Token);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            PipeIsDead = true;
        }
    }

    internal async Task<ISignal?> WaitSignalAsync()
    {
        try
        {
            byte[] lenBuf = new byte[sizeof(Int32)];
            await stream.ReadExactlyAsync(lenBuf, 0, lenBuf.Length);
            byte[] jsonBuf = new byte[BitConverter.ToInt32(lenBuf)];
            await stream.ReadExactlyAsync(jsonBuf, 0, jsonBuf.Length);
            return SignalFactory.DeserializeSignal(Encoding.GetString(jsonBuf));
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            PipeIsDead = true;
            return null;
        }
    }
}