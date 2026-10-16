using System;
using System.Collections.Generic;
using System.Linq;

namespace TestFramework.DebugUI.Layout;

public static partial class RunBoardLayout
{
    /// <summary>
    /// Giving every horizontal run a track of its own, and then each row and channel its depth.
    /// </summary>
    private sealed partial class Arrangement
    {
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

                // The tallest card in the row decides how deep the row is. Cards in a row share a top
                // edge rather than a bottom one, so a short card beside a widget's leaves its space
                // below it — which is where its own pipes drop into the channel anyway.
                y = measurements.Snap(y + BoxesIn(row).Max(box => box.Height));

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
    }
}
