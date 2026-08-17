using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State;

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
public static class RunBoardLayout
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
    private sealed class Arrangement(RunGraph graph, LayoutOptions measurements)
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

            ImmutableList<StepIO> asserted =
            [
                .. graph.Assertions
                    .Select(assertion => assertion.Target)
                    .Where(produced.ContainsKey)
                    .Distinct(StringComparer.Ordinal)
                    .Select(target => produced[target])
            ];

            if (asserted.Count == 0)
                return;

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
                    StepBox box = new(row, step, x);
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

        /// <summary>
        /// Works out which pipe exists, and which channel each of its horizontal runs belongs in.
        /// </summary>
        private void PlanWires()
        {
            Dictionary<string, Origin> lastProducer = new(StringComparer.Ordinal);

            foreach (Row row in rows)
            {
                foreach (StepNode step in row.Steps)
                {
                    StepBox consumer = steps[new StepKey(row.StageName, step.StepId)];

                    foreach (StepIO input in Distinct(step.Inputs))
                    {
                        if (!lastProducer.TryGetValue(input.Key, out Origin origin))
                            continue;

                        // Steps sharing a layer run at the same time, so one cannot have consumed
                        // the other's output; an edge between them would assert an ordering the run
                        // never had.
                        if (origin.Row == row.Index)
                            continue;

                        wires.Add(new Wire(origin.Producer, consumer, input, origin.Row, row.Index));
                    }
                }

                foreach (StepNode step in row.Steps)
                {
                    StepBox producer = steps[new StepKey(row.StageName, step.StepId)];

                    foreach (StepIO output in Distinct(step.Outputs))
                        lastProducer[output.Key] = new Origin(producer, row.Index);
                }
            }

            AssignLanes();
            AssignTracks();
        }

        /// <summary>
        /// Sends each long pipe out the side it has less distance to travel, in the innermost lane
        /// that is free for the rows it has to pass.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A pipe crossing more than one row cannot go through the rows between, so it leaves the
        /// block entirely and comes back at the far end. Which side it leaves by was fixed to the
        /// right, which is wrong whenever both ends sit near the left edge: the pipe crossed the whole
        /// board twice to reach a lane, and every such pipe added another lane's width to a diagram
        /// that did not need to be that wide.
        /// </para>
        /// <para>
        /// The comparison is between the two detours a pipe would actually make — out to the lane and
        /// back, at both ends. It survives the shift below because moving everything by the same
        /// amount moves both edges and both endpoints together, which is what makes deciding the side
        /// before making room for it sound.
        /// </para>
        /// </remarks>
        private void AssignLanes()
        {
            // One lane per value leaving a connector, not one per pipe. Every pipe in a group carries
            // the same value out of the same connector to a different consumer, so they are one flow
            // that forks — drawing them as one trunk is not a simplification, it is what is actually
            // happening. Six pipes fanning a variable out to three steps used to take six lanes and
            // cross the board twelve times; as trunks they take one each.
            trunks.AddRange(wires
                .Where(wire => wire.IsLong)
                .GroupBy(wire => (Producer: wire.Producer.NodeId, wire.Declared.Key))
                .Select(group => new Trunk(group.Key.Key, [.. group]))

                // Shortest first, so the pipe with least to travel gets the lane nearest the block. By
                // arrival order instead, a pipe skipping a single row could be handed the outermost
                // lane while one skipping six sat inside it — the short hop then made the longer
                // detour, which is precisely backwards.
                .OrderBy(trunk => trunk.Span)
                .ThenBy(trunk => trunk.FirstChannel)
                .ThenBy(trunk => trunk.Key, StringComparer.Ordinal));

            foreach (Trunk trunk in trunks)
            {
                // Decided once for the whole trunk. Letting each pipe choose its own column would
                // split the very thing being joined.
                Column column = ChooseColumn(trunk);
                column.Take(trunk);

                trunk.Column = column;

                foreach (Wire wire in trunk.Wires)
                    wire.Column = column;
            }

            // Lanes on the left need room the board did not reserve, and a negative coordinate is not
            // room. Everything moves right instead, once, by exactly what the left lanes take. Columns
            // inside the block move with it, having been chosen against where the boxes were.
            if (leftLaneCount > 0)
            {
                double distance = measurements.LaneGap + ((leftLaneCount - 1) * measurements.LaneWidth);

                ShiftRight(distance);

                foreach (Column column in columns)
                    column.ShiftRight(distance);
            }

            leftLaneBase = measurements.Snap(ContentLeft() - measurements.LaneGap);
            rightLaneBase = measurements.Snap(ContentRight() + measurements.LaneGap);
        }

        /// <summary>
        /// Picks the column a trunk descends in: the cheapest of the ones already in use, a fresh one
        /// inside the block, and a fresh lane outside either edge.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every long pipe used to leave the block outright, on the grounds that a row might be in the
        /// way. Only the rows it actually passes can be in the way, and a board whose rows are one
        /// step wide leaves a corridor down each side of the stage that nothing occupies. Stepping
        /// over all of that to reach a lane outside is what made the pipes long and the board wide.
        /// </para>
        /// <para>
        /// Cheapest means least horizontal travel, counted over the whole trunk: out of the connector
        /// and back in at every consumer. That is the quantity a reader sees as a detour, and
        /// measuring it directly is what removes the need to guess a side up front.
        /// </para>
        /// </remarks>
        private Column ChooseColumn(Trunk trunk)
        {
            Column? shared = null;
            double sharedCost = double.PositiveInfinity;

            foreach (Column column in columns)
            {
                if (!column.Accepts(trunk) || !IsClearFor(trunk, ColumnCostX(column)))
                    continue;

                double cost = trunk.Travel(ColumnCostX(column));

                if (cost < sharedCost)
                {
                    sharedCost = cost;
                    shared = column;
                }
            }

            double inside = double.NaN;
            double insideCost = double.PositiveInfinity;

            foreach (double candidate in InsideCandidates(trunk))
            {
                double cost = trunk.Travel(candidate);

                if (cost < insideCost)
                {
                    insideCost = cost;
                    inside = candidate;
                }
            }

            double leftCost = trunk.Travel(PendingLaneX(onLeft: true, leftLaneCount + 1));
            double rightCost = trunk.Travel(PendingLaneX(onLeft: false, rightLaneCount + 1));

            // Reuse wins ties: a column already on the board is one fewer line to follow, and a tie
            // means the reader pays nothing for it.
            if (shared is not null && sharedCost <= insideCost && sharedCost <= leftCost && sharedCost <= rightCost)
                return shared;

            if (insideCost <= leftCost && insideCost <= rightCost)
                return Claim(Column.Inside(columns.Count + 1, inside));

            return leftCost <= rightCost
                ? Claim(Column.Outside(columns.Count + 1, onLeft: true, ++leftLaneCount))
                : Claim(Column.Outside(columns.Count + 1, onLeft: false, ++rightLaneCount));
        }

        private Column Claim(Column column)
        {
            columns.Add(column);
            return column;
        }

        /// <summary>
        /// Every position inside the block a trunk could descend in without hitting anything.
        /// </summary>
        /// <remarks>
        /// Kept a whole lane's width apart from the columns already in use, so two trunks descending
        /// near one another still read as two pipes rather than as a thick one.
        /// </remarks>
        private IEnumerable<double> InsideCandidates(Trunk trunk)
        {
            double left = ContentLeft();
            double right = ContentRight();

            for (double x = left; x <= right; x += measurements.Grid)
            {
                double candidate = measurements.Snap(x);

                if (!IsClearFor(trunk, candidate))
                    continue;

                if (columns.Any(column => Math.Abs(ColumnCostX(column) - candidate) < measurements.LaneWidth))
                    continue;

                yield return candidate;
            }
        }

        /// <summary>
        /// Whether a trunk could run down a given column without crossing a box or joining a pipe.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two separate checks, over two different sets of rows. The run passes through the rows
        /// strictly between its ends, so those boxes have to be clear of it by a margin. It passes
        /// through the channels at both ends too, where pipes drop out of the row above and rise into
        /// the row below at their connectors — so a column level with one of those connectors would
        /// end up drawn along that pipe, which is the one thing the routing may never do.
        /// </para>
        /// <para>
        /// The connector check reaches one row further at each end than the box check, and that is not
        /// an oversight: the run starts below the row it leaves and stops above the row it enters, so
        /// it may pass under a box it could not pass through.
        /// </para>
        /// </remarks>
        private bool IsClearFor(Trunk trunk, double x)
        {
            for (int index = trunk.FirstChannel + 1; index <= trunk.LastChannel; index++)
            {
                foreach (StepBox box in BoxesIn(rows[index]))
                {
                    if (x > box.X - measurements.LaneWidth && x < box.Right(measurements) + measurements.LaneWidth)
                        return false;
                }
            }

            int last = Math.Min(trunk.LastChannel + 1, rows.Count - 1);

            for (int index = trunk.FirstChannel; index <= last; index++)
            {
                foreach (StepBox box in BoxesIn(rows[index]))
                {
                    if (box.HasConnectorNear(x, measurements.ConnectorPitch / 2))
                        return false;
                }
            }

            return true;
        }

        private IEnumerable<StepBox> BoxesIn(Row row)
            => row.Steps.Select(step => steps[new StepKey(row.StageName, step.StepId)]);

        /// <summary>Where a column sits for the purpose of comparing detours, before any shift.</summary>
        private double ColumnCostX(Column column)
            => column.IsInside ? column.X : PendingLaneX(column.OnLeft, column.Lane);

        /// <summary>Where a lane outside the block would land if one more were opened on that side.</summary>
        private double PendingLaneX(bool onLeft, int lane) => onLeft
            ? ContentLeft() - measurements.LaneGap - ((lane - 1) * measurements.LaneWidth)
            : ContentRight() + measurements.LaneGap + ((lane - 1) * measurements.LaneWidth);

        private void ShiftRight(double distance)
        {
            foreach (StepBox step in steps.Values)
                step.ShiftRight(distance);
        }

        /// <summary>
        /// Gives every horizontal run a track no other run in that channel uses.
        /// </summary>
        /// <remarks>
        /// Greedy interval colouring over the horizontal spans, which is optimal for intervals: two
        /// pipes share a track only when their runs cannot touch. This is the step that makes
        /// overlapping impossible rather than merely unlikely.
        /// </remarks>
        private void AssignTracks()
        {
            Dictionary<int, List<Run>> byChannel = [];

            // Straight drops first: they claim no track, so settling them up front keeps them out of
            // the way of everything else.
            foreach (Wire wire in wires.Where(wire => !wire.IsLong))
            {
                if (Math.Abs(wire.SourceX - wire.Consumer.InputX[wire.Declared.Key]) < double.Epsilon)
                    wire.IsStraight = true;
            }

            // Reserved in three passes, from the top of the channel downwards, because a track's
            // position is its index and a pipe cannot be squeezed in above one already placed.
            //
            // A long pipe leaves sideways as soon as it clears its connector, so its drop is short
            // and belongs at the top; a long pipe arriving does the reverse and belongs at the
            // bottom, beside the connector it enters. Everything else sits between. Ordering this
            // way is what lets a dropper find a track above the risers sharing its column, rather
            // than discovering too late that there is no room above.
            // One track for the whole trunk, not one per pipe. All of them leave the same connector
            // for the same lane, so separate tracks bought nothing and cost a rung: three pipes drew
            // three horizontals stacked at the head of a trunk that is otherwise a single line.
            foreach (Trunk trunk in trunks.OrderBy(trunk => trunk.SourceX).ThenBy(trunk => trunk.Key, StringComparer.Ordinal))
            {
                trunk.SourceTrack = Reserve(byChannel, trunk.FirstChannel, trunk.SourceX, ColumnX(trunk.Column),
                    dropX: trunk.SourceX, riseX: double.NaN);

                foreach (Wire wire in trunk.Wires)
                    wire.SourceTrack = trunk.SourceTrack;
            }

            foreach (Wire wire in wires.Where(wire => !wire.IsLong && !wire.IsStraight))
            {
                wire.SourceTrack = Reserve(byChannel, wire.FromRow, wire.SourceX,
                    wire.Consumer.InputX[wire.Declared.Key],
                    dropX: wire.SourceX,
                    riseX: wire.Consumer.InputX[wire.Declared.Key]);
            }

            foreach (Wire wire in wires.Where(wire => wire.IsLong))
            {
                wire.TargetTrack = Reserve(byChannel, wire.ToRow - 1, ColumnX(wire.Column!), wire.TargetX,
                    dropX: double.NaN,
                    riseX: wire.TargetX);
            }

            foreach ((int channel, List<Run> runs) in byChannel)
                channelTracks[channel] = runs.Count;
        }

        /// <summary>
        /// Finds a track whose horizontal run and vertical legs collide with nothing already there.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The horizontal test is ordinary interval colouring. The vertical test is the one that is
        /// easy to miss and produces the worst-looking result: a pipe drops from its connector down
        /// to its track, and rises from its track up into the target connector, so two pipes whose
        /// connectors happen to share an x can end up running along each other even though their
        /// horizontals are nowhere near. Connectors sharing an x is not a rare accident — boxes are
        /// the same width and connectors are evenly spread, so it is the normal case.
        /// </para>
        /// <para>
        /// A pipe dropping at some x occupies that column from the top of the channel down to its
        /// track; one rising at the same x occupies it from its track to the bottom. They miss each
        /// other only when the riser's track is below the dropper's, which is what the search here
        /// enforces.
        /// </para>
        /// </remarks>
        private int Reserve(Dictionary<int, List<Run>> byChannel, int channel, double from, double to, double dropX, double riseX)
        {
            double left = Math.Min(from, to);
            double right = Math.Max(from, to);

            if (!byChannel.TryGetValue(channel, out List<Run>? runs))
                byChannel[channel] = runs = [];

            for (int track = 0; track <= runs.Count; track++)
            {
                if (track < runs.Count && Touches(runs[track], left, right))
                    continue;

                if (VerticalsClash(runs, track, dropX, riseX))
                    continue;

                if (track == runs.Count)
                    runs.Add(new Run(left, right, Columns(dropX), Columns(riseX)));
                else
                    runs[track] = runs[track].Extend(left, right, dropX, riseX);

                return track;
            }

            // Nothing satisfied both tests, so the pipe gets a track below everything. Its rise is
            // then clear by construction; only a drop can still share a column, which is reported by
            // the routing tests rather than hidden.
            runs.Add(new Run(left, right, Columns(dropX), Columns(riseX)));
            return runs.Count - 1;
        }

        /// <summary>Whether two horizontal runs come close enough to read as one line.</summary>
        private bool Touches(Run run, double left, double right)
            => !(run.Right + measurements.Grid < left || right + measurements.Grid < run.Left);

        /// <summary>
        /// Whether putting a pipe on a track would leave one of its vertical legs running along
        /// another pipe's.
        /// </summary>
        /// <remarks>
        /// A pipe drops from its connector to its track, so it owns that column from the top of the
        /// channel down; and it rises from its track to the target connector, owning that column from
        /// the track down. Two pipes sharing a column therefore miss each other only when the one
        /// rising sits below the one dropping.
        /// </remarks>
        private static bool VerticalsClash(List<Run> runs, int track, double dropX, double riseX)
        {
            if (!double.IsNaN(riseX))
            {
                for (int other = track; other < runs.Count; other++)
                {
                    if (runs[other].Drops.Contains(riseX))
                        return true;
                }
            }

            if (!double.IsNaN(dropX))
            {
                for (int other = 0; other < Math.Min(track + 1, runs.Count); other++)
                {
                    if (runs[other].Rises.Contains(dropX))
                        return true;
                }
            }

            return false;
        }

        private static HashSet<double> Columns(double x) => double.IsNaN(x) ? [] : [x];

        /// <summary>
        /// Fixes every y coordinate, giving each channel exactly the depth its tracks need.
        /// </summary>
        private void PlaceVertically()
        {
            double y = measurements.Snap(measurements.TopMargin + measurements.StageHeaderHeight);
            string? previousStage = null;

            foreach (Row row in rows)
            {
                if (previousStage is not null && !string.Equals(previousStage, row.StageName, StringComparison.Ordinal))
                    y = measurements.Snap(y + measurements.StageGap + measurements.StageHeaderHeight);

                previousStage = row.StageName;

                row.StepTop = y;
                y = measurements.Snap(y + measurements.StepHeight);

                row.ChannelTop = y;
                y = measurements.Snap(y + ChannelHeight(row.Index));
            }
        }

        /// <summary>
        /// How deep a channel has to be to hold its tracks.
        /// </summary>
        /// <remarks>
        /// Grows with the traffic through it rather than being fixed, so a busy junction opens up
        /// instead of forcing its pipes together — which is where a board stops being readable.
        /// </remarks>
        private double ChannelHeight(int channel)
        {
            int tracks = channelTracks.TryGetValue(channel, out int count) ? count : 0;
            return Math.Max(measurements.LayerGap, measurements.Snap(((tracks + 1) * measurements.TrackSpacing) + measurements.PipeLead));
        }

        private double TrackY(int channel, int track)
        {
            Row row = rows[channel];
            return measurements.Snap(row.ChannelTop + measurements.PipeLead + (track * measurements.TrackSpacing));
        }

        /// <summary>Where a column finally sits, once the board has been made room in.</summary>
        private double ColumnX(Column column)
            => column.IsInside ? column.X : LaneX(column.OnLeft, column.Lane);

        /// <summary>Where a lane sits: lane one nearest the block, the rest stacked outwards.</summary>
        private double LaneX(bool onLeft, int lane) => measurements.Snap(onLeft
            ? leftLaneBase - ((lane - 1) * measurements.LaneWidth)
            : rightLaneBase + ((lane - 1) * measurements.LaneWidth));

        private LayoutResult Materialise()
        {
            ImmutableList<LayoutNode>.Builder nodes = ImmutableList.CreateBuilder<LayoutNode>();
            ImmutableList<LayoutEdge>.Builder edges = ImmutableList.CreateBuilder<LayoutEdge>();

            nodes.AddRange(BuildStageBands());

            // The verdict is the one box whose appearance depends on the run as a whole rather than
            // on the thing it draws, so it is the one box the arrangement has to finish itself.
            bool satisfied = graph.Assertions.All(assertion => assertion.Succeeded);

            foreach (StepBox step in steps.Values)
            {
                LayoutNode node = step.ToNode(measurements);
                nodes.Add(node.Kind == LayoutNodeKind.Verdict ? node with { IsSatisfied = satisfied } : node);
            }

            foreach (Wire wire in wires)
                edges.Add(BuildConsumption(wire));

            AddUnconnectedPorts();

            double right = rightLaneCount == 0 ? ContentRight() : LaneX(onLeft: false, rightLaneCount);

            return new LayoutResult
            {
                Nodes = nodes.ToImmutable(),
                Edges = edges.ToImmutable(),
                Ports = [.. ports.Values],
                Width = measurements.Snap(right + measurements.RightMargin),
                // Measured from what is actually drawn, not from where the last channel began: the
                // stage band wraps its rows and extends past them, so reporting the channel left the
                // last stage hanging off the bottom of anything that fitted the board to a window.
                Height = measurements.Snap(nodes.Max(node => node.Bottom) + measurements.BottomMargin)
            };
        }

        /// <summary>
        /// Draws the connectors for declarations no pipe reaches.
        /// </summary>
        /// <remarks>
        /// An unconnected connector is information: the step asked for something no earlier step
        /// supplies, or produced something nothing went on to read. Leaving either out would hide
        /// exactly the case a reader is hunting — and with no box drawn on the pipe any more, the
        /// connectors are the only place a declaration appears at all.
        /// </remarks>
        private void AddUnconnectedPorts()
        {
            foreach (StepBox step in steps.Values)
            {
                foreach (StepIO input in Distinct(step.Step.Inputs))
                {
                    Port(step.NodeId, input, isInput: true, connected: false,
                        step.InputX[input.Key], step.Top);
                }

                foreach (StepIO output in Distinct(step.Step.Outputs))
                {
                    Port(step.NodeId, output, isInput: false, connected: false,
                        step.OutputX[output.Key], step.Bottom(measurements));
                }
            }
        }

        private LayoutEdge BuildConsumption(Wire wire)
        {
            LayoutPort from = Port(wire.Producer.NodeId, wire.Declared, isInput: false, connected: true,
                wire.SourceX, wire.Producer.Bottom(measurements));

            LayoutPort to = Port(wire.Consumer.NodeId, wire.Declared, isInput: true, connected: true,
                wire.Consumer.InputX[wire.Declared.Key], wire.Consumer.Top);

            if (wire.IsStraight)
            {
                return new LayoutEdge
                {
                    Id = $"consumes:{wire.Producer.NodeId}/{wire.Declared.Key}->{wire.Consumer.NodeId}",
                    Kind = KindOf(wire),
                    ValueKind = wire.Declared.Kind,
                    FromNodeId = wire.Producer.NodeId,
                    ToNodeId = wire.Consumer.NodeId,
                    FromPortId = from.Id,
                    ToPortId = to.Id,
                    Key = wire.Declared.Key,
                    Points = Simplify([new LayoutPoint(from.X, from.Y), new LayoutPoint(to.X, to.Y)])
                };
            }

            double sourceTrack = TrackY(wire.FromRow, wire.SourceTrack);

            List<LayoutPoint> points =
            [
                new LayoutPoint(from.X, from.Y),
                new LayoutPoint(from.X, sourceTrack)
            ];

            if (wire.IsLong)
            {
                double column = ColumnX(wire.Column!);
                double targetTrack = TrackY(wire.ToRow - 1, wire.TargetTrack);

                points.Add(new LayoutPoint(column, sourceTrack));
                points.Add(new LayoutPoint(column, targetTrack));
                points.Add(new LayoutPoint(to.X, targetTrack));
            }
            else
            {
                points.Add(new LayoutPoint(to.X, sourceTrack));
            }

            points.Add(new LayoutPoint(to.X, to.Y));

            return new LayoutEdge
            {
                Id = $"consumes:{wire.Producer.NodeId}/{wire.Declared.Key}->{wire.Consumer.NodeId}",
                Kind = KindOf(wire),
                ValueKind = wire.Declared.Kind,
                FromNodeId = wire.Producer.NodeId,
                ToNodeId = wire.Consumer.NodeId,
                FromPortId = from.Id,
                ToPortId = to.Id,
                Key = wire.Declared.Key,
                Lane = wire.Column?.Index ?? 0,
                Points = Simplify(points)
            };
        }

        /// <summary>
        /// What a pipe into a given consumer carries.
        /// </summary>
        /// <remarks>
        /// A pipe into the verdict is an assertion rather than a consumption: nothing downstream uses
        /// the value, it is being checked. The distinction is what lets the view colour those pipes by
        /// whether the check held.
        /// </remarks>
        private static LayoutEdgeKind KindOf(Wire wire)
            => wire.Consumer.Row.IsVerdict ? LayoutEdgeKind.Assertion : LayoutEdgeKind.Consumption;

        private LayoutPort Port(string nodeId, StepIO declared, bool isInput, bool connected, double x, double y)
        {
            string id = $"port:{nodeId}:{(isInput ? "in" : "out")}:{declared.Key}";

            if (ports.TryGetValue(id, out LayoutPort? existing))
                return existing;

            LayoutPort port = new()
            {
                Id = id,
                NodeId = nodeId,
                Key = declared.Key,
                Kind = declared.Kind,
                IsInput = isInput,
                IsConnected = connected,
                X = x,
                Y = y
            };

            ports[id] = port;
            return port;
        }

        private IEnumerable<LayoutNode> BuildStageBands()
        {
            // The verdict belongs to no stage: it is a statement about the run, and banding it would
            // read as a stage the run executed.
            foreach (IGrouping<string, Row> stage in rows
                         .Where(row => !row.IsVerdict)
                         .GroupBy(row => row.StageName, StringComparer.Ordinal))
            {
                List<LayoutNode> members = [];

                foreach (Row row in stage)
                {
                    foreach (StepNode step in row.Steps)
                        members.Add(steps[new StepKey(row.StageName, step.StepId)].ToNode(measurements));
                }

                if (members.Count == 0)
                    continue;

                double left = measurements.Snap(members.Min(node => node.X) - measurements.StagePadding);
                double right = measurements.Snap(members.Max(node => node.Right) + measurements.StagePadding);
                double top = measurements.Snap(members.Min(node => node.Y) - measurements.StageHeaderHeight);
                double bottom = measurements.Snap(members.Max(node => node.Bottom) + measurements.StagePadding);

                yield return new LayoutNode
                {
                    Id = "stage:" + stage.Key,
                    Kind = LayoutNodeKind.Stage,
                    StageName = stage.Key,
                    X = left,
                    Y = top,
                    Width = right - left,
                    Height = bottom - top
                };
            }
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

        private readonly record struct StepKey(string StageName, int StepId);

        /// <summary>What one track in a channel already carries.</summary>
        private sealed record Run(double Left, double Right, HashSet<double> Drops, HashSet<double> Rises)
        {
            internal Run Extend(double left, double right, double dropX, double riseX)
            {
                if (!double.IsNaN(dropX))
                    Drops.Add(dropX);

                if (!double.IsNaN(riseX))
                    Rises.Add(riseX);

                return this with { Left = Math.Min(Left, left), Right = Math.Max(Right, right) };
            }
        }

        private readonly record struct Origin(StepBox Producer, int Row);

        /// <summary>
        /// A vertical line down the board that trunks descend in.
        /// </summary>
        /// <remarks>
        /// Either a position inside the block, in the space a narrow row leaves beside it, or a lane
        /// outside one of its edges. The two are the same thing to everything downstream — a column is
        /// only ever asked where it is and whether it is free — which is what lets the choice between
        /// them be made by cost rather than by rule.
        /// </remarks>
        private sealed class Column
        {
            private readonly List<Trunk> occupants = [];

            private Column(int index) => Index = index;

            /// <summary>An identity for the view, so pipes sharing a column can be told they do.</summary>
            internal int Index { get; }

            internal bool IsInside { get; private init; }

            /// <summary>Where the column sits, for an inside one.</summary>
            internal double X { get; private set; }

            internal bool OnLeft { get; private init; }

            /// <summary>Which lane out from the edge, for an outside one.</summary>
            internal int Lane { get; private init; }

            internal static Column Inside(int index, double x) => new(index) { IsInside = true, X = x };

            internal static Column Outside(int index, bool onLeft, int lane) => new(index) { OnLeft = onLeft, Lane = lane };

            /// <summary>Whether a trunk could descend here without meeting one already doing so.</summary>
            internal bool Accepts(Trunk trunk) => occupants.All(trunk.Clears);

            internal void Take(Trunk trunk) => occupants.Add(trunk);

            internal void ShiftRight(double distance) => X += distance;
        }

        /// <summary>
        /// Every long pipe carrying one value out of one connector, routed as a single flow.
        /// </summary>
        /// <remarks>
        /// These are not separate pipes that happen to look alike: it is one value leaving one port
        /// and forking to reach several steps. Routing them together is what stops a fan-out taking a
        /// lane per consumer, and it is the only reason the pipes may share a line — the routing tests
        /// excuse an overlap between two runs out of the same port for exactly this case.
        /// </remarks>
        private sealed class Trunk(string key, List<Wire> members)
        {
            internal string Key => key;

            internal List<Wire> Wires => members;

            /// <summary>Where the flow leaves the producing step.</summary>
            internal double SourceX => members[0].SourceX;

            /// <summary>The channel the flow drops into, being the one below its producer.</summary>
            internal int FirstChannel { get; } = members.Min(wire => wire.FromRow);

            /// <summary>The channel the last consumer is reached from.</summary>
            internal int LastChannel { get; } = members.Max(wire => wire.ToRow) - 1;

            /// <summary>How many channels the flow occupies, being how far out of its way it goes.</summary>
            internal int Span => LastChannel - FirstChannel;

            /// <summary>The vertical line this flow descends in.</summary>
            internal Column Column { get; set; } = null!;

            internal int SourceTrack { get; set; }

            /// <summary>
            /// How far sideways the whole flow would travel to descend at a given position.
            /// </summary>
            /// <remarks>
            /// Counted out of the connector and back in at every consumer, because that is the detour
            /// a reader actually sees. A trunk feeding three steps pays the return trip three times,
            /// so it is worth more to place well than one feeding a single step — which falls out of
            /// measuring instead of ranking.
            /// </remarks>
            internal double Travel(double x)
            {
                double travel = Math.Abs(SourceX - x);

                foreach (Wire wire in members)
                    travel += Math.Abs(wire.TargetX - x);

                return travel;
            }

            /// <summary>
            /// Whether this flow and another never occupy the lane at the same height.
            /// </summary>
            /// <remarks>
            /// Measured in channels rather than in pixels because a lane has to be picked before the
            /// channels have been given their depths, and the vertical run is bounded by a track in
            /// its first channel and one in its last. Two flows with no channel in common therefore
            /// cannot touch, whatever those depths turn out to be.
            /// </remarks>
            internal bool Clears(Trunk other)
                => LastChannel < other.FirstChannel || FirstChannel > other.LastChannel;
        }

        private sealed class Row(int index, string stageName, ImmutableList<StepNode> steps, bool isVerdict = false)
        {
            internal int Index => index;

            internal string StageName => stageName;

            internal ImmutableList<StepNode> Steps => steps;

            /// <summary>Whether this row holds the verdict rather than steps the run executed.</summary>
            internal bool IsVerdict => isVerdict;

            internal double StepTop { get; set; }


            internal double ChannelTop { get; set; }
        }

        private sealed class StepBox(Row row, StepNode step, double x)
        {
            internal Row Row => row;

            internal StepNode Step => step;

            internal double X => x;

            internal double Top => row.StepTop;

            internal Dictionary<string, double> InputX { get; } = new(StringComparer.Ordinal);

            internal Dictionary<string, double> OutputX { get; } = new(StringComparer.Ordinal);

            internal string NodeId => row.IsVerdict ? "verdict" : $"step:{row.StageName}/{step.StepId}";

            internal double Right(LayoutOptions measurements) => x + measurements.StepWidth;

            /// <summary>Whether any connector on this box sits close enough to an x to be run along.</summary>
            internal bool HasConnectorNear(double x, double clearance)
                => InputX.Values.Concat(OutputX.Values).Any(connector => Math.Abs(connector - x) < clearance);

            /// <summary>
            /// Moves the step and everything placed against it.
            /// </summary>
            /// <remarks>
            /// The connectors move with the card because their positions were derived from it and are
            /// then held as absolute numbers. Moving the card alone would leave every pipe attached to
            /// where the card used to be.
            /// </remarks>
            internal void ShiftRight(double distance)
            {
                x += distance;

                foreach (string key in InputX.Keys.ToArray())
                    InputX[key] += distance;

                foreach (string key in OutputX.Keys.ToArray())
                    OutputX[key] += distance;
            }

            internal double Bottom(LayoutOptions measurements) => row.StepTop + measurements.StepHeight;

            internal LayoutNode ToNode(LayoutOptions measurements) => new()
            {
                Id = NodeId,
                Kind = row.IsVerdict ? LayoutNodeKind.Verdict : LayoutNodeKind.Step,
                StageName = row.StageName,
                StepId = row.IsVerdict ? null : step.StepId,
                X = x,
                Y = row.StepTop,
                Width = measurements.StepWidth,
                Height = measurements.StepHeight
            };
        }

        private sealed class Wire(StepBox producer, StepBox consumer, StepIO declared, int fromRow, int toRow)
        {
            internal StepBox Producer => producer;

            internal StepBox Consumer => consumer;

            /// <summary>Where the pipe leaves the producing step.</summary>
            internal double SourceX => producer.OutputX[declared.Key];

            /// <summary>Where the pipe arrives at the consuming step.</summary>
            internal double TargetX => consumer.InputX[declared.Key];

            internal StepIO Declared => declared;

            internal int FromRow => fromRow;

            internal int ToRow => toRow;

            /// <summary>Whether the pipe has to pass a row, and so cannot simply drop into place.</summary>
            internal bool IsLong => toRow > fromRow + 1;

            /// <summary>Whether the two connectors line up, making the pipe a single vertical drop.</summary>
            internal bool IsStraight { get; set; }

            /// <summary>The vertical line the pipe descends in, shared with the rest of its trunk.</summary>
            internal Column? Column { get; set; }

            internal int SourceTrack { get; set; }

            internal int TargetTrack { get; set; }
        }
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
