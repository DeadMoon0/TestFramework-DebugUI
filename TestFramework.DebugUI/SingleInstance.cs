using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using TestFramework.DebugUI.State.Diagnostics;

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
    /// The name the tool holds its own election under.
    /// </summary>
    /// <remarks>
    /// One name for the product, and everything derived from it. A caller may hold an election under a
    /// different name — the tool's own suite does, so that testing the election does not require the
    /// tool to be closed, and so two of these tests are not each other's second instance.
    /// </remarks>
    private const string DefaultName = "TestFramework.DebugUI";

    /// <summary>
    /// The name the election is held under.
    /// </summary>
    /// <remarks>
    /// Local rather than global, so the one-instance rule is per logged-in user. Two people on one machine are
    /// two people, and each of them has their own pipe, their own journal and their own window.
    /// </remarks>
    private static string MutexNameFor(string name) => @"Local\" + name + ".instance";

    /// <summary>The pipe a second launch hands its argument to.</summary>
    private static string PipeNameFor(string name) => name + ".activation";

    private readonly Mutex? mutex;
    private readonly SynchronizationContext? context;

    /// <summary>What this instance was elected under, so it serves and wakes the right pipe.</summary>
    private readonly string name;

    private bool listening;
    private bool disposed;

    private SingleInstance(Mutex? mutex, bool isOwner, string name)
    {
        this.mutex = mutex;
        this.name = name;
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
    /// <param name="name">
    /// What to hold the election under, or null for the tool's own name. Given only by something that
    /// wants an election of its own rather than the product's — which in practice is the suite that
    /// tests this, since holding the real election would make it the tool's second instance.
    /// </param>
    public static SingleInstance Acquire(string? name = null)
    {
        string elected = name is { Length: > 0 } given ? given : DefaultName;

        try
        {
            Mutex mutex = new(initiallyOwned: true, MutexNameFor(elected), out bool createdNew);

            return new SingleInstance(mutex, createdNew, elected);
        }
        catch (Exception e)
        {
            Log.Write(e);

            return new SingleInstance(null, isOwner: true, elected);
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
    /// <param name="payload">What to hand over, or null to ask only that the window be shown.</param>
    /// <param name="timeout">How long to wait for the owner to answer.</param>
    /// <param name="name">The election to hand to, or null for the tool's own.</param>
    public static bool TrySend(string? payload, TimeSpan timeout, string? name = null)
    {
        try
        {
            using NamedPipeClientStream client = new(
                ".",
                PipeNameFor(name is { Length: > 0 } given ? given : DefaultName),
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
            Log.Write(e);

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
                    PipeNameFor(name),
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
                Log.Write(e);

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
            TrySend(null, TimeSpan.FromMilliseconds(200), name);

        try
        {
            if (IsOwner)
                mutex?.ReleaseMutex();
        }
        catch (Exception e)
        {
            Log.Write(e);
        }

        mutex?.Dispose();
    }
}
