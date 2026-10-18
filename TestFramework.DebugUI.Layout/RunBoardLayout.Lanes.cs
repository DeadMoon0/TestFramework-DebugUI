using System;
using System.Collections.Generic;
using System.Linq;
using TestFramework.DebugUI.State.Board;

namespace TestFramework.DebugUI.Layout;

public static partial class RunBoardLayout
{
    // Deciding which pipes exist, and where each long one descends: a column inside the block or a lane
    // outside either edge.
    private sealed partial class Arrangement
    {
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

        /// <summary>Where a column sits for the purpose of comparing detours, before any shift.</summary>
        private double ColumnCostX(Column column)
            => column.IsInside ? column.X : PendingLaneX(column.OnLeft, column.Lane);

        /// <summary>Where a lane outside the block would land if one more were opened on that side.</summary>
        private double PendingLaneX(bool onLeft, int lane) => onLeft
            ? ContentLeft() - measurements.LaneGap - ((lane - 1) * measurements.LaneWidth)
            : ContentRight() + measurements.LaneGap + ((lane - 1) * measurements.LaneWidth);

        /// <summary>Where a column finally sits, once the board has been made room in.</summary>
        private double ColumnX(Column column)
            => column.IsInside ? column.X : LaneX(column.OnLeft, column.Lane);

        /// <summary>Where a lane sits: lane one nearest the block, the rest stacked outwards.</summary>
        private double LaneX(bool onLeft, int lane) => measurements.Snap(onLeft
            ? leftLaneBase - ((lane - 1) * measurements.LaneWidth)
            : rightLaneBase + ((lane - 1) * measurements.LaneWidth));
    }
}
