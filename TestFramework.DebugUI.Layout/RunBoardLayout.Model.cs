using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using TestFramework.DebugUI.State.Board;

namespace TestFramework.DebugUI.Layout;

public static partial class RunBoardLayout
{
    /// <summary>
    /// The pieces an arrangement is made of: rows, the boxes in them, the pipes between them, and the
    /// columns and tracks those pipes are given.
    /// </summary>
    private sealed partial class Arrangement
    {
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

        private sealed class StepBox(Row row, StepNode step, double x, double height)
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

            /// <summary>How tall this card is, which its own content decides.</summary>
            internal double Height => height;

            internal double Bottom(LayoutOptions measurements) => row.StepTop + height;

            internal LayoutNode ToNode(LayoutOptions measurements) => new()
            {
                Id = NodeId,
                Kind = row.IsVerdict ? LayoutNodeKind.Verdict : LayoutNodeKind.Step,
                StageName = row.StageName,
                StepId = row.IsVerdict ? null : step.StepId,
                X = x,
                Y = row.StepTop,
                Width = measurements.StepWidth,
                Height = height
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
}
