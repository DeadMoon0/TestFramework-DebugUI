using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace TestFramework.DebugUI.Launcher;

/// <summary>
/// Asks GitHub for the newest release of the application.
/// </summary>
/// <remarks>
/// <para>
/// Unauthenticated, because the repository is public and asking for a token would put a credential
/// prompt in front of a tool that is meant to start. That caps the caller at GitHub's anonymous rate
/// limit, which one check per application start is nowhere near.
/// </para>
/// <para>
/// Every failure returns nothing rather than throwing. The caller's contract is that an unreachable
/// feed is ordinary, and the launcher starts what is already installed.
/// </para>
/// </remarks>
public sealed class GitHubReleaseSource : IReleaseSource, IDisposable
{
    /// <summary>How long the check is given before the launcher stops waiting for it.</summary>
    /// <remarks>
    /// Short on purpose. This runs between someone opening the tool and the tool appearing, so the
    /// budget is what a person will sit through without wondering whether it has hung — not what a
    /// slow network might eventually manage.
    /// </remarks>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(4);

    private readonly HttpClient client;
    private readonly Uri endpoint;

    /// <summary>Creates a source reading the releases of one repository.</summary>
    public GitHubReleaseSource(string owner, string repository, HttpClient? client = null)
    {
        endpoint = new Uri($"https://api.github.com/repos/{owner}/{repository}/releases/latest");

        this.client = client ?? new HttpClient();

        // GitHub rejects a request with no user agent outright, which would look exactly like being
        // offline and would be diagnosed as such for some time.
        if (!this.client.DefaultRequestHeaders.UserAgent.Any())
            this.client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TestFramework-DebugUI-Launcher", "1.0"));
    }

    /// <inheritdoc />
    public async Task<ReleaseInfo?> LatestAsync(CancellationToken cancellationToken)
    {
        try
        {
            using CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(Budget);

            using HttpResponseMessage response = await client.GetAsync(endpoint, budget.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return null;

            string body = await response.Content.ReadAsStringAsync(budget.Token).ConfigureAwait(false);

            return Read(body);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or Newtonsoft.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>The asset the release workflow publishes the application as.</summary>
    /// <remarks>
    /// Named rather than guessed. A release may carry several archives — the launcher's own, symbols,
    /// a source snapshot — and picking whichever zip came back first would install one of those the
    /// day a second one is attached, with no error and no way to tell from the outside.
    /// </remarks>
    internal const string PackageAsset = "TestFramework.DebugUI.zip";

    /// <summary>
    /// Reads a release out of GitHub's answer.
    /// </summary>
    /// <remarks>
    /// A release with no downloadable package is treated as no release at all. Publishing the notes
    /// before attaching the binaries is an ordinary few minutes in anyone's release process, and
    /// updating to a version whose package does not exist yet would leave the tool unable to start.
    /// </remarks>
    internal static ReleaseInfo? Read(string body)
    {
        JObject release = JObject.Parse(body);

        Version? version = ReleaseTag.Read(release.Value<string>("tag_name"));

        if (version is null)
            return null;

        string? url = release["assets"] is JArray assets ? PackageIn(assets) : null;

        if (url is null)
            return null;

        return new ReleaseInfo { Version = version, DownloadUrl = new Uri(url) };
    }

    /// <summary>
    /// Finds the application package among a release's assets.
    /// </summary>
    /// <remarks>
    /// The canonical name first. The fallback to any single zip exists for a release cut before the
    /// name was settled, and deliberately declines to choose when there is more than one — guessing
    /// there is how the wrong application gets installed silently.
    /// </remarks>
    private static string? PackageIn(JArray assets)
    {
        List<(string Name, string Url)> archives =
        [
            .. assets
                .Select(asset => (Name: asset.Value<string>("name") ?? string.Empty, Url: asset.Value<string>("browser_download_url")))
                .Where(asset => asset.Url is not null && asset.Url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .Select(asset => (asset.Name, Url: asset.Url!))
        ];

        (string Name, string Url) named = archives.FirstOrDefault(asset => string.Equals(asset.Name, PackageAsset, StringComparison.OrdinalIgnoreCase));

        if (named.Url is not null)
            return named.Url;

        return archives.Count == 1 ? archives[0].Url : null;
    }

    /// <inheritdoc />
    public void Dispose() => client.Dispose();
}
