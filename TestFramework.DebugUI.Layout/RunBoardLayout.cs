using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.State.Runs;

namespace TestFramework.DebugUI.Layout;

/// <summary>
/// Turns a run into board geometry.
/// </summary>
/// <remarks>
/// <para>
/// A pure function of the run and the measurements: the same graph always lays out the same way, and
/// nothing here knows what a board is drawn with. That is what lets the whole layout be tested
/// against its polylines rather than against a screenshot, and it is why no geometry is kept in
/// application state — it is derived, so storing it would mean maintaining it.
/// </para>
/// <para>
/// The shape is a vertical flow. Stages are bands down the board; within a stage each execution
/// layer is a row of steps, the values those steps produce hang directly beneath them, and pipes run
/// connector to connector.
/// </para>
/// <para>
/// <b>Pipes never cross a box, and never run along one another.</b> That is the property the whole
/// arrangement exists to guarantee, and it is bought in three parts: values sit directly under the
/// connector that produced them, so a production pipe is a straight vertical drop; every horizontal
/// run happens inside a channel between two rows, where no box ever sits; and within a channel each
/// pipe gets a track of its own, so two horizontals can never share a line. A pipe travelling more
/// than one row leaves for a lane to the right of everything, which is the only way to pass a row
/// without going through it.
/// </para>
/// <para>
/// Two pipes can still <em>cross</em> — one going down where another goes across. That is
/// unavoidable for an arbitrary graph, and a crossing reads cleanly where an overlap does not: a
/// crossing is visibly two pipes, while two pipes sharing a line look like one.
/// </para>
/// </remarks>
public static partial class RunBoardLayout
{
    /// <summary>
    /// Lays out a run.
    /// </summary>
    public static LayoutResult Compute(RunGraph graph, LayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(graph);

        return new Arrangement(graph, options ?? LayoutOptions.Default).Build();
    }

    /// <summary>
    /// One run being laid out.
    /// </summary>
    /// <remarks>
    /// Built in three passes, because the passes genuinely depend on each other in that order:
    /// horizontal positions are needed to work out which pipes can share a track, the number of
    /// tracks decides how tall each channel has to be, and only then are the vertical positions
    /// known. Trying to do it in one pass is what produces channels too thin for the pipes in them.
    /// </remarks>
    private sealed partial class Arrangement(RunGraph graph, LayoutOptions measurements)
    {
        private readonly List<Row> rows = [];
        private readonly Dictionary<StepKey, StepBox> steps = [];
        private readonly List<Wire> wires = [];
        private readonly List<Trunk> trunks = [];
        private readonly List<Column> columns = [];
        private readonly Dictionary<string, LayoutPort> ports = new(StringComparer.Ordinal);

        /// <summary>How many horizontal tracks each channel has to hold.</summary>
        private readonly Dictionary<int, int> channelTracks = [];

        private double leftLaneBase;
        private double rightLaneBase;
        private int leftLaneCount;
        private int rightLaneCount;

        internal LayoutResult Build()
        {
            BuildRows();
            if (rows.Count == 0)
                return LayoutResult.Empty;

            PlaceHorizontally();
            PlanWires();
            PlaceVertically();

            return Materialise();
        }

        private void BuildRows()
        {
            int index = 0;

            foreach (StageNode stage in graph.Stages)
            {
                if (stage.Steps.Count == 0)
                    continue;

                foreach (IGrouping<int, StepNode> layer in stage.Steps
                             .GroupBy(step => step.LayerIndex)
                             .OrderBy(group => group.Key))
                {
                    rows.Add(new Row(index++, stage.Name, [.. layer.OrderBy(step => step.StepId)]));
                }
            }

            AddVerdictRow(index);
        }

        /// <summary>
        /// Adds the verdict as a last row that every asserted value flows into.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Modelled as a row holding one box rather than as a case of its own, so it inherits the
        /// routing wholesale: its pipes get tracks, lanes and corners from the same passes that draw
        /// every other pipe. A verdict wired up by a parallel code path would be the one thing on the
        /// board free to cross something.
        /// </para>
        /// <para>
        /// Only values a step actually produced are wired in. An assertion against something the
        /// board never drew has nothing to run a pipe from, and inventing a source for it would draw
        /// a flow that did not happen.
        /// </para>
        /// </remarks>
        private void AddVerdictRow(int index)
        {
            if (graph.Assertions.Count == 0)
                return;

            Dictionary<string, StepIO> produced = new(StringComparer.Ordinal);

            foreach (StepIO output in graph.Stages.SelectMany(stage => stage.Steps).SelectMany(step => step.Outputs))
                produced.TryAdd(output.Key, output);

            IReadOnlySet<string> producedKeys = produced.Keys.ToHashSet(StringComparer.Ordinal);

            // Drawn whenever the run checked anything. The checks no pipe can show are listed inside it, and a run
            // proved entirely by those - steps that completed, an expression - used to get no verdict at all.
            ImmutableList<StepIO> asserted =
            [
                .. graph.Assertions
                    .Where(assertion => VerdictChecks.IsWired(assertion, producedKeys))
                    .Select(assertion => assertion.Target)
                    .Distinct(StringComparer.Ordinal)
                    .Select(target => produced[target])
            ];

            StepNode verdict = new()
            {
                StepId = 0,
                Name = "Verdict",
                Inputs = asserted
            };

            rows.Add(new Row(index, VerdictStage, [verdict], isVerdict: true));
        }

        /// <summary>
        /// Fixes every x coordinate: the steps, their connectors, and the values beneath them.
        /// </summary>
        /// <remarks>
        /// A value is placed directly under the connector that produces it, and the connectors are
        /// spread evenly along the step's bottom edge. That is what keeps a production pipe a
        /// straight drop instead of a dogleg — and a straight drop cannot cross anything.
        /// </remarks>
        private void PlaceHorizontally()
        {
            double widest = rows.Max(row => RowWidth(row.Steps.Count, measurements.StepWidth, measurements.StepGap));
            double centreX = measurements.Snap(measurements.LeftMargin + (widest / 2));

            foreach (Row row in rows)
            {
                double rowWidth = RowWidth(row.Steps.Count, measurements.StepWidth, measurements.StepGap);
                double x = measurements.Snap(centreX - (rowWidth / 2));

                foreach (StepNode step in row.Steps)
                {
                    StepBox box = new(row, step, x, HeightOf(row, step));
                    steps[new StepKey(row.StageName, step.StepId)] = box;

                    PlaceConnectors(box);

                    x = measurements.Snap(x + measurements.StepWidth + measurements.StepGap);
                }
            }
        }

        /// <summary>
        /// Places a step's connectors, left to right from a fixed inset.
        /// </summary>
        /// <remarks>
        /// Inputs and outputs share one column pitch, so a connector on the top of a card lines up
        /// with the one below it and a pipe between two cards runs straight down instead of jogging
        /// for no reason. The pitch is the width of a value box, because each output has one hanging
        /// directly beneath it.
        /// </remarks>
        private void PlaceConnectors(StepBox box)
        {
            ImmutableList<StepIO> inputs = Distinct(box.Step.Inputs);
            ImmutableList<StepIO> outputs = Distinct(box.Step.Outputs);

            // Inputs and outputs share one pitch from a fixed inset, so an output connector lines up
            // with the input connector of the step below it and the pipe between them is a straight
            // drop rather than a jog.
            double first = measurements.Snap(box.X + measurements.ConnectorInset + (measurements.ConnectorPitch / 2));

            for (int index = 0; index < inputs.Count; index++)
                box.InputX[inputs[index].Key] = measurements.Snap(first + (index * measurements.ConnectorPitch));

            for (int index = 0; index < outputs.Count; index++)
                box.OutputX[outputs[index].Key] = measurements.Snap(first + (index * measurements.ConnectorPitch));
        }

        private IEnumerable<StepBox> BoxesIn(Row row)
            => row.Steps.Select(step => steps[new StepKey(row.StageName, step.StepId)]);

        /// <summary>
        /// How tall a step's card is.
        /// </summary>
        /// <remarks>
        /// Per step rather than per board: a step that drew something needs the room to show it, and
        /// one that drew nothing would only be paying for empty space — a board of mostly-blank tall
        /// cards is a board that fits smaller and reads worse. The verdict grows by a line for each
        /// check it lists - the ones no pipe can show - up to a few, and then one line for the rest.
        /// </remarks>
        private double HeightOf(Row row, StepNode step)
        {
            if (row.IsVerdict)
                return measurements.StepHeight + (VerdictChecks.Lines(VerdictChecks.Unwired(graph).Count) * measurements.VerdictLineHeight);

            return graph.Widgets.Any(widget => widget.BelongsTo(row.StageName, step.StepId))
                ? measurements.StepHeightWithWidget
                : measurements.StepHeight;
        }

        private void ShiftRight(double distance)
        {
            foreach (StepBox step in steps.Values)
                step.ShiftRight(distance);
        }

        private double RowWidth(int count, double itemWidth, double gap)
            => count <= 0 ? 0 : (count * itemWidth) + ((count - 1) * gap);

        /// <summary>The right edge of the content the pipes have to route around.</summary>
        private double ContentRight() => steps.Values.Max(step => step.Right(measurements));

        /// <summary>The left edge of the content the pipes have to route around.</summary>
        private double ContentLeft() => steps.Values.Min(step => step.X);

        private const double MinimumValueWidth = 60;

        /// <summary>
        /// The stage the verdict row claims to belong to.
        /// </summary>
        /// <remarks>
        /// A name no stage can have, because the verdict is not a stage: it keeps the row out of the
        /// step lookup's way without the rest of the arrangement needing to know it is special.
        /// </remarks>
        private const string VerdictStage = "$verdict";
    }

    /// <summary>Keeps declaration order while dropping a key declared more than once.</summary>
    private static ImmutableList<StepIO> Distinct(ImmutableList<StepIO> declared)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        ImmutableList<StepIO>.Builder distinct = ImmutableList.CreateBuilder<StepIO>();

        foreach (StepIO entry in declared)
        {
            if (!string.IsNullOrWhiteSpace(entry.Key) && seen.Add(entry.Key))
                distinct.Add(entry);
        }

        return distinct.ToImmutable();
    }

    /// <summary>Drops points that repeat the one before them, which would be a zero-length segment.</summary>
    private static ImmutableList<LayoutPoint> Simplify(IReadOnlyList<LayoutPoint> points)
    {
        ImmutableList<LayoutPoint>.Builder simplified = ImmutableList.CreateBuilder<LayoutPoint>();

        foreach (LayoutPoint point in points)
        {
            if (simplified.Count == 0 || simplified[^1] != point)
                simplified.Add(point);
        }

        return simplified.ToImmutable();
    }
}
