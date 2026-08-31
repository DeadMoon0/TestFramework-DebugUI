using TestFramework.Core.Debugger;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Timelines;
using Xunit.Abstractions;

namespace TestFramework.DebugUI.TimelineSamples.Tests;

/// <summary>
/// A run with something to look at, for watching the window draw it.
/// </summary>
/// <remarks>
/// <para>
/// Like everything else in this project, this is a sample rather than a regression test: it exists so
/// there is a real run in the journal whose steps produced pictures, documents and logs — the three
/// kinds of evidence the window has renderers for. Assertions here only keep it honest enough to
/// notice when it stops producing one.
/// </para>
/// <para>
/// The pictures are real pages, photographed from the family's own sample shop by the browser pack.
/// Drawn stand-ins were the first attempt and were the wrong thing to look at: what a reader judges
/// is whether a thumbnail of a page is recognisable at the size a card gives it, and a coloured
/// square answers a question nobody asked.
/// </para>
/// <para>
/// Run it, then open the tool: the board should show a page on the cards that photographed one, the
/// step panel a filmstrip under its outputs, and the inspector the page itself rather than a hex dump.
/// </para>
/// </remarks>
public class WidgetSampleTests(ITestOutputHelper outputHelper)
{
    [Fact]
    public async Task AStepThatWalksAJourney_ShowsWhatItSaw()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(new PageStep("catalogue", "The product list, nothing in the cart yet")).Name("browse the catalogue")
            .Trigger(new PageStep("checkout", "The order form, filled in")).Name("fill the basket")
            .Trigger(new PageStep("confirmation", "The order, placed")).Name("check out")
            .Build();

        TimelineRun run = await timeline.SetupRun(outputHelper).RunAsync();

        run.EnsureRanToCompletion();
    }

    [Fact]
    public async Task AStepThatRetries_ShowsWhatEachAttemptSaw()
    {
        // The case a filmstrip earns its place on: three pictures of the same step, and the one that
        // explains the failure is the first.
        Timeline timeline = Timeline.Create()
            .Trigger(new FlakyPageStep()).WithRetry(2, CalcDelays.None).Name("place the order")
            .Build();

        TimelineRun run = await timeline.SetupRun(outputHelper).RunAsync();

        run.EnsureRanToCompletion();
    }

    [Fact]
    public async Task AStepWithMoreThanPicturesToShow_KeepsAllOfIt()
    {
        // A page is not only what it looked like. The markup behind it and what the console said are
        // the other two things a reader asks for, and they travel the same way.
        Timeline timeline = Timeline.Create()
            .Trigger(new EvidenceStep()).Name("inspect the page")
            .Build();

        TimelineRun run = await timeline.SetupRun(outputHelper).RunAsync();

        run.EnsureRanToCompletion();
    }

    /// <summary>Photographs the page it claims to be on.</summary>
    private sealed class PageStep(string page, string summary) : Step<EmptyStepResultContext>
    {
        public override string Name => "page";
        public override string Description => $"Visits {page} and photographs it.";
        public override bool DoesReturn => false;

        public override Task<EmptyStepResultContext?> Execute(RunContext context)
        {
            context.Widgets.Publish(new Widget
            {
                Kind = WidgetKinds.Screenshot,
                Name = page,
                Form = DebugPreviewForm.Image,
                Bytes = SamplePages.Read(page),
                Summary = summary,
                Badges = ["1920x1080"]
            });

            return Task.FromResult<EmptyStepResultContext?>(EmptyStepResultContext.Instance);
        }

        public override Step<EmptyStepResultContext> Clone() => new PageStep(page, summary).WithClonedOptions(this);
        public override void DeclareIO(StepIOContract contract) { }
        public override StepInstance<Step<EmptyStepResultContext>, EmptyStepResultContext> GetInstance() => new(this);
    }

    /// <summary>Fails twice, photographing what it saw each time.</summary>
    private sealed class FlakyPageStep : Step<EmptyStepResultContext>
    {
        private int attempts;

        public override string Name => "order";
        public override string Description => "Places an order, eventually.";
        public override bool DoesReturn => false;

        public override Task<EmptyStepResultContext?> Execute(RunContext context)
        {
            attempts++;

            context.Widgets.Publish(new Widget
            {
                Kind = WidgetKinds.Screenshot,
                Name = "order-form",
                Form = DebugPreviewForm.Image,

                // Still on the form for the attempts that failed, on the confirmation for the one that
                // worked — which is the whole reason a reader opens the earlier ones.
                Bytes = SamplePages.Read(attempts < 3 ? "checkout" : "confirmation"),
                Summary = attempts < 3 ? "Still on the order form" : "The order, placed"
            });

            if (attempts < 3)
                throw new InvalidOperationException("The payment provider was not ready.");

            return Task.FromResult<EmptyStepResultContext?>(EmptyStepResultContext.Instance);
        }

        public override Step<EmptyStepResultContext> Clone() => new FlakyPageStep().WithClonedOptions(this);
        public override void DeclareIO(StepIOContract contract) { }
        public override StepInstance<Step<EmptyStepResultContext>, EmptyStepResultContext> GetInstance() => new(this);
    }

    /// <summary>Keeps a picture, the markup behind it and what the console said.</summary>
    private sealed class EvidenceStep : Step<EmptyStepResultContext>
    {
        public override string Name => "evidence";
        public override string Description => "Keeps everything the page had to say.";
        public override bool DoesReturn => false;

        public override Task<EmptyStepResultContext?> Execute(RunContext context)
        {
            context.Widgets.Publish(new Widget
            {
                Kind = WidgetKinds.Screenshot,
                Name = "orders",
                Form = DebugPreviewForm.Image,
                Bytes = SamplePages.Read("orders"),
                Summary = "The orders page, as it was"
            });

            context.Widgets.Publish(new Widget
            {
                Kind = WidgetKinds.Document,
                Name = "orders-source",
                Form = DebugPreviewForm.Markup,
                Text = Markup,
                Summary = "The markup the table was built from"
            });

            context.Widgets.Publish(new Widget
            {
                Kind = WidgetKinds.LogStream,
                Name = "console",
                Form = DebugPreviewForm.Text,
                Text = string.Join(Environment.NewLine,
                [
                    "[warn] total rendered before the currency pipe resolved",
                    "[error] GET /api/orders/A-1039 404 (Not Found)",
                    "[info] fell back to 'state unknown'"
                ]),
                Summary = "One error and a warning",
                Badges = ["3 lines"]
            });

            return Task.FromResult<EmptyStepResultContext?>(EmptyStepResultContext.Instance);
        }

        private const string Markup = """
            <main class="orders">
              <h1>Orders</h1>
              <table>
                <tr><th>Order</th><th>Placed</th><th>Total</th><th>State</th></tr>
                <tr><td>A-1041</td><td>31.08.2026</td><td>162.50</td><td>Packed</td></tr>
                <tr><td>A-1040</td><td>30.08.2026</td><td>9.00</td><td>Shipped</td></tr>
              </table>
            </main>
            """;

        public override Step<EmptyStepResultContext> Clone() => new EvidenceStep().WithClonedOptions(this);
        public override void DeclareIO(StepIOContract contract) { }
        public override StepInstance<Step<EmptyStepResultContext>, EmptyStepResultContext> GetInstance() => new(this);
    }

    /// <summary>
    /// The pages these samples photograph.
    /// </summary>
    /// <remarks>
    /// Real captures of the family's own sample shop, taken by the browser pack and kept beside this
    /// project rather than drawn in code: what makes a thumbnail worth putting on a card is that it
    /// looks like the page it came from, and nothing drawn here does.
    /// </remarks>
    private static class SamplePages
    {
        internal static byte[] Read(string page)
            => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Pages", page + ".png"));
    }
}
