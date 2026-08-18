using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace TestFramework.DebugUI;

/// <summary>
/// Makes sure there is one debugger, and gives a second launch somewhere to hand its work to.
/// </summary>
/// <remarks>
/// <para>
/// Not a nicety. This tool owns a named pipe that test hosts connect to, and only one process can hold it: a
/// second instance loses that race and then sits there looking like it is listening while every run goes to the
/// first. Electing an owner turns a silent failure into an arrangement.
/// </para>
/// <para>
/// The second launch is not wasted. Double-clicking a shared run starts the application with a file path, and
/// that path is what gets handed over — so opening a bundle while the debugger is already running does what a
/// person expects instead of failing to start a second copy.
/// </para>
/// </remarks>
public sealed class SingleInstance : IDisposable
{
    /// <summary>
    /// The name the election is held under.
    /// </summary>
    /// <remarks>
    /// Local rather than global, so the one-instance rule is per logged-in user. Two people on one machine are
    /// two people, and each of them has their own pipe, their own journal and their own window.
    /// </remarks>
    private const string MutexName = @"Local\TestFramework.DebugUI.instance";

    /// <summary>The pipe a second launch hands its argument to.</summary>
    private const string PipeName = "TestFramework.DebugUI.activation";

    private readonly Mutex? mutex;
    private readonly SynchronizationContext? context;

    private bool listening;
    private bool disposed;

    private SingleInstance(Mutex? mutex, bool isOwner)
    {
        this.mutex = mutex;
        IsOwner = isOwner;
        context = SynchronizationContext.Current;
    }

    /// <summary>Raised on the owner when another launch hands over its argument, which may be nothing.</summary>
    public event Action<string?>? Activated;

    /// <summary>Whether this process is the one that runs.</summary>
    public bool IsOwner { get; }

    /// <summary>
    /// Holds the election.
    /// </summary>
    /// <remarks>
    /// A mutex that cannot be created at all is treated as "I am the owner", so a machine policy that blocks
    /// them leaves the tool working as it did before any of this existed rather than refusing to start.
    /// </remarks>
    public static SingleInstance Acquire()
    {
        try
        {
            Mutex mutex = new(initiallyOwned: true, MutexName, out bool createdNew);

            return new SingleInstance(mutex, createdNew);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);

            return new SingleInstance(null, isOwner: true);
        }
    }

    /// <summary>
    /// Starts answering later launches.
    /// </summary>
    /// <remarks>
    /// One connection at a time, on a background thread, for as long as the process lives. There is nothing to
    /// scale here: the traffic is one message per double-click.
    /// </remarks>
    public void Listen()
    {
        if (!IsOwner || listening || disposed)
            return;

        listening = true;

        Thread thread = new(Serve) { IsBackground = true, Name = "activation" };

        thread.Start();
    }

    /// <summary>
    /// Hands an argument to the instance that is already running.
    /// </summary>
    /// <remarks>
    /// Returns false when nobody answered, which the caller should treat as "carry on and be the owner" — the
    /// owner may have died between the election and this call.
    /// </remarks>
    public static bool TrySend(string? payload, TimeSpan timeout)
    {
        try
        {
            using NamedPipeClientStream client = new(
                ".",
                PipeName,
                PipeDirection.Out,
                PipeOptions.CurrentUserOnly);

            client.Connect((int)timeout.TotalMilliseconds);

            byte[] bytes = Encoding.UTF8.GetBytes(payload ?? string.Empty);

            client.Write(bytes, 0, bytes.Length);
            client.Flush();

            return true;
        }
        catch (Exception e)
        {
            // A timeout, or no pipe at all. Either way the caller has to get on with starting.
            Debug.WriteLine(e);

            return false;
        }
    }

    private void Serve()
    {
        while (!disposed)
        {
            try
            {
                // CurrentUserOnly on both ends, so nothing outside this account can push a file path into the
                // window — the same rule the debug transport uses.
                using NamedPipeServerStream server = new(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.CurrentUserOnly);

                server.WaitForConnection();

                using StreamReader reader = new(server, Encoding.UTF8);

                string payload = reader.ReadToEnd();

                if (disposed)
                    return;

                Raise(string.IsNullOrWhiteSpace(payload) ? null : payload);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);

                if (disposed)
                    return;

                // A malformed or abandoned connection must not end the loop, or the next double-click would
                // silently do nothing for the rest of the session.
                Thread.Sleep(200);
            }
        }
    }

    /// <summary>Raises the event on the thread that created this, which for the application is the UI thread.</summary>
    private void Raise(string? payload)
    {
        if (context is null)
        {
            Activated?.Invoke(payload);
            return;
        }

        context.Post(_ => Activated?.Invoke(payload), null);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        // Unblocks the waiting server so the thread can end rather than holding the pipe until the process does.
        if (listening)
            TrySend(null, TimeSpan.FromMilliseconds(200));

        try
        {
            if (IsOwner)
                mutex?.ReleaseMutex();
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }

        mutex?.Dispose();
    }
}
