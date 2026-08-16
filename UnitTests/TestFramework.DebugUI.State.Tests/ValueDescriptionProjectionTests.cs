using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers the description surviving the trip from Core into the state tree.
/// </summary>
/// <remarks>
/// The projection used to keep only the one line Core had rendered and drop everything behind it, so
/// no amount of detail on the producing side could reach the screen.
/// </remarks>
public class ValueDescriptionProjectionTests
{
    [Fact]
    public void AVariableKeepsTheFactsItWasDescribedWith()
    {
        RunGraph graph = RunProjection.ApplyValueUpdate(new RunGraph(), Variable("orders", Described()));

        ValueDescription described = graph.Variables["orders"].Description;

        Assert.Equal("[412 items]", described.Summary);
        Assert.Equal(DebugValueShape.Collection, described.Shape);
        Assert.Equal("412", described.Facts.Single(fact => fact.Name == "items").Value);
    }

    [Fact]
    public void AValueWrittenToAFileKeepsThePathToIt()
    {
        // What lets the UI offer the rest of a cut value rather than showing a dead end.
        RunGraph graph = RunProjection.ApplyValueUpdate(new RunGraph(), Variable("orders", Described()));

        ValueDescription described = graph.Variables["orders"].Description;

        Assert.True(described.HasMore);
        Assert.Equal("values/orders.json", described.Body!.RelativePath);
    }

    [Fact]
    public void TwoDescriptionsStatingTheSameFactsAreEqual()
    {
        // Core's own record compares its arrays by reference, so without this the state tree treats
        // every write as a change and every binding watching a value re-emits on all of them.
        Assert.Equal(ValueDescription.From(Description()), ValueDescription.From(Description()));
    }

    [Fact]
    public void RepublishingAnUnchangedValueLeavesTheGraphAlone()
    {
        // The consequence that matters. SetItem returns the dictionary untouched only while equal
        // values compare equal.
        RunGraph first = RunProjection.ApplyValueUpdate(new RunGraph(), Variable("orders", Described()));
        RunGraph second = RunProjection.ApplyValueUpdate(first, Variable("orders", Described()));

        Assert.Same(first.Variables, second.Variables);
    }

    [Fact]
    public void ADescriptionThatDiffersInOneFactIsNotEqual()
    {
        DebugValueDescription changed = Description() with
        {
            Fields = [new DebugValueField { Name = "items", Value = "413" }]
        };

        Assert.NotEqual(ValueDescription.From(Description()), ValueDescription.From(changed));
    }

    [Fact]
    public void AValueFromAnOlderJournalProjectsToAnEmptyDescriptionRatherThanFailing()
    {
        // Journals recorded before descriptions existed replay with the defaulted one.
        RunGraph graph = RunProjection.ApplyValueUpdate(new RunGraph(), Variable("orderId", DebugValueDescription.Empty));

        Assert.Same(ValueDescription.Empty, graph.Variables["orderId"].Description);
    }

    [Fact]
    public void AnArtifactReadsItsLifecycleFromTheFieldsRatherThanTheJsonPayload()
    {
        RunGraph graph = RunProjection.ApplyValueUpdate(new RunGraph(), Artifact(
            lifecycle: new DebugValueLifecycle { State = "Cleaned", Versions = ["v1", "v2"], CurrentVersion = "v2" },
            core: null));

        ArtifactNode artifact = graph.Artifacts["receipt"];

        Assert.Equal("Cleaned", artifact.State);
        Assert.Equal(["v1", "v2"], artifact.Versions);
    }

    [Fact]
    public void AnArtifactFromAnOlderJournalStillReadsItsLifecycleFromTheJsonPayload()
    {
        // Recordings made before Core stated these as fields have to keep replaying, and the fallback
        // is the only thing standing between them and an artifact with no state and no history.
        RunGraph graph = RunProjection.ApplyValueUpdate(new RunGraph(), Artifact(
            lifecycle: null,
            core: new JObject { ["state"] = "Setup", ["versions"] = new JArray("v1") }));

        ArtifactNode artifact = graph.Artifacts["receipt"];

        Assert.Equal("Setup", artifact.State);
        Assert.Equal(["v1"], artifact.Versions);
    }

    [Fact]
    public void RepublishingAnArtifactWithTheSameHistoryLeavesTheListAlone()
    {
        // Core resends the whole history on every artifact update, so rebuilding the list each time
        // would make every update look like a change to everything watching it.
        DebugValueLifecycle lifecycle = new() { State = "Setup", Versions = ["v1", "v2"], CurrentVersion = "v2" };

        RunGraph first = RunProjection.ApplyValueUpdate(new RunGraph(), Artifact(lifecycle, core: null));
        RunGraph second = RunProjection.ApplyValueUpdate(first, Artifact(lifecycle, core: null));

        Assert.Same(first.Artifacts["receipt"].Versions, second.Artifacts["receipt"].Versions);
    }

    private static PipeValueUpdateSignal Artifact(DebugValueLifecycle? lifecycle, JObject? core) => new()
    {
        SessionId = "session-1",
        Name = "receipt",
        ValueKind = DebugValueKind.Artifact,
        Envelope = new DebugValueEnvelope
        {
            Kind = DebugValueKind.Artifact,
            TypeName = "FileArtifactDescriber",
            DisplayText = "receipt",
            SchemaKey = DebugValueSchemaKeys.File,
            Lifecycle = lifecycle,
            Core = core
        }
    };

    private static DebugValueDescription Described() => Description();

    private static DebugValueDescription Description() => new()
    {
        Summary = "[412 items]",
        Shape = DebugValueShape.Collection,
        Fields = [new DebugValueField { Name = "items", Value = "412" }],
        Badges = ["large"],
        Preview = new DebugValuePreview { Form = DebugPreviewForm.Json, Text = "[1,2,3", IsTruncated = true, SizeInBytes = 90_000 },
        Body = new DebugValueBody
        {
            Path = @"C:\runs\Sample-1234abcd\values\orders.json",
            RelativePath = "values/orders.json",
            SizeInBytes = 90_000,
            ContentHash = "ABCD"
        }
    };

    private static PipeValueUpdateSignal Variable(string name, DebugValueDescription description) => new()
    {
        SessionId = "session-1",
        Name = name,
        ValueKind = DebugValueKind.Variable,
        Envelope = new DebugValueEnvelope
        {
            Kind = DebugValueKind.Variable,
            TypeName = "System.Collections.Generic.List`1[System.Int32]",
            DisplayText = description.Summary,
            Description = description,
            SchemaKey = DebugValueSchemaKeys.Of(description.Shape)
        }
    };
}
