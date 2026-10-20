using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using TestFramework.DebugUI.State.Board;

namespace TestFramework.DebugUI.Layout;

public static partial class RunBoardLayout
{
    // Turning the finished arrangement into the nodes, ports and polylines a board is drawn from.
    private sealed partial class Arrangement
    {
        private LayoutResult Materialise()
        {
            ImmutableList<LayoutNode>.Builder nodes = ImmutableList.CreateBuilder<LayoutNode>();
            ImmutableList<LayoutEdge>.Builder edges = ImmutableList.CreateBuilder<LayoutEdge>();

            nodes.AddRange(BuildStageBands());

            foreach (StepBox step in steps.Values)
            {
                LayoutNode node = step.ToNode(measurements);
                nodes.Add(node.Kind == LayoutNodeKind.Assertion ? AsCheck(node) : node);
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
        /// A pipe into a check is an assertion rather than a consumption: nothing downstream uses
        /// the value, it is being checked. The distinction is what lets the view colour those pipes by
        /// whether the check held.
        /// </remarks>
        private static LayoutEdgeKind KindOf(Wire wire)
            => wire.Consumer.Row.IsAssertions ? LayoutEdgeKind.Assertion : LayoutEdgeKind.Consumption;

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

        /// <summary>A check card, finished with what the check was and whether it held.</summary>
        private LayoutNode AsCheck(LayoutNode node)
        {
            RunCheck check = checks[node.StepId ?? 0];

            return node with { Title = check.Name, Detail = check.Subject, IsSatisfied = check.Held };
        }

        private IEnumerable<LayoutNode> BuildStageBands()
        {
            // The checks are banded like any stage, below the stages the run executed; what tells them apart is
            // how the view colours a band that is about the run's checks.
            foreach (IGrouping<string, Row> stage in rows
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

                bool isAssertions = string.Equals(stage.Key, RunChecks.StageName, StringComparison.Ordinal);

                yield return new LayoutNode
                {
                    Id = "stage:" + stage.Key,
                    Kind = LayoutNodeKind.Stage,
                    StageName = stage.Key,
                    IsSatisfied = !isAssertions || checks.All(check => check.Held),
                    Detail = isAssertions ? $"{checks.Count(check => check.Held)} of {checks.Count} held" : null,
                    X = left,
                    Y = top,
                    Width = right - left,
                    Height = bottom - top
                };
            }
        }
    }
}
