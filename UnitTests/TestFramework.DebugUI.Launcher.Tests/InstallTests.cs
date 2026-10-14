using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.DebugUI.Launcher;

namespace TestFramework.DebugUI.Launcher.Tests;

/// <summary>
/// An update either installs a working application or leaves everything as it was - promptly.
/// </summary>
public sealed class InstallTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "tf-launcher-install-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task APackageWithTheApplication_IsInstalled()
    {
        using HttpClient client = new(new ServingHandler(Zip(LauncherPaths.ApplicationExecutable)));

        await Store().InstallAsync(Release("0.5.0"), client, progress: null, CancellationToken.None);

        Assert.Equal([Version.Parse("0.5.0")], Store().Installed());
    }

    [Fact]
    public async Task APackageWithoutTheApplication_IsRefused_AndLeavesNoVersionFolder()
    {
        // What the old "any single zip" fallback installed when the application's upload had failed: the launcher's
        // own package. The folder it committed held no application, so Installed() ignored it and the next start
        // downloaded the same package again - every start, silently.
        using HttpClient client = new(new ServingHandler(Zip("TestFramework.DebugUI.Launcher.exe")));

        InvalidDataException refused = await Assert.ThrowsAsync<InvalidDataException>(
            () => Store().InstallAsync(Release("0.5.0"), client, progress: null, CancellationToken.None));

        Assert.Contains(LauncherPaths.ApplicationExecutable, refused.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(new LauncherPaths(root).FolderFor(Version.Parse("0.5.0"))), "A version folder was committed without the application.");
    }

    [Fact]
    public async Task ADownloadThatStalls_FailsInsteadOfHolding()
    {
        // A stalled CDN used to keep the launcher on its progress bar indefinitely, so the installed version - the
        // fallback - was never started.
        using HttpClient client = new(new StallingHandler());
        Stopwatch elapsed = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Store().InstallAsync(Release("0.5.0"), client, progress: null, TimeSpan.FromMilliseconds(500), CancellationToken.None));

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10), $"The stalled download held for {elapsed.Elapsed}.");
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private VersionStore Store() => new(new LauncherPaths(root));

    private static ReleaseInfo Release(string version) => new()
    {
        Version = Version.Parse(version),
        DownloadUrl = new Uri("https://example.invalid/TestFramework.DebugUI.zip"),
    };

    private static byte[] Zip(string entry)
    {
        using MemoryStream stream = new();
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using StreamWriter writer = new(archive.CreateEntry(entry).Open());
            writer.Write("x");
        }

        return stream.ToArray();
    }

    private sealed class ServingHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    /// <summary>Answers the request, sends a few bytes, then nothing more.</summary>
    private sealed class StallingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) });
    }

    private sealed class StallingStream : Stream
    {
        private bool sentFirst;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!sentFirst)
            {
                sentFirst = true;
                buffer.Span[0] = 0x50;
                return 1;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
