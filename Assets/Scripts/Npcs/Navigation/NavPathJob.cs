using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Brickcraft.Npcs.Navigation
{
    /// <summary>
    /// A* over the spans (see NavGrid): from where an NPC stands to a goal, moving to the 8 columns around
    /// (diagonals only when both sides are free), stepping up and dropping down as far as it can, with room
    /// for its whole body (its footprint and headroom). Then the path is straightened: waypoints it can walk
    /// between in a straight line are skipped. Runs on a worker thread.
    /// </summary>
    [BurstCompile]
    internal struct NavPathJob : IJob
    {
        [ReadOnly] public NativeParallelMultiHashMap<int2, NavSpan> spans;
        public NavAgent agent;
        /// <summary>Cells: where its feet are and where it goes.</summary>
        public int3 start;
        public int3 goal;
        /// <summary>Close enough: columns from the goal (and within its height above or below).</summary>
        public int goalRange;
        public int maxNodes;

        /// <summary>The floor cells it goes through, straightened (its own first).</summary>
        public NativeList<int3> path;
        public NativeReference<NavResult> result;

        private struct Node
        {
            public int3 position;
            public int parent;
            public float cost;
            public bool closed;
        }

        private static readonly int2[] Directions = {
            new int2(1, 0), new int2(-1, 0), new int2(0, 1), new int2(0, -1),
            new int2(1, 1), new int2(1, -1), new int2(-1, 1), new int2(-1, -1),
        };

        public void Execute() {
            path.Clear();
            result.Value = NavResult.Failed;
            if (!snap(start, agent.step + 2, agent.drop + 2, out int startFloor)) {
                return;
            }
            int3 from = new int3(start.x, startFloor, start.z);
            int3 to = goal;
            if (snap(goal, agent.step, 64, out int goalFloor)) {
                to.y = goalFloor;
            }

            NativeList<Node> nodes = new NativeList<Node>(256, Allocator.Temp);
            NativeParallelHashMap<int3, int> indexes = new NativeParallelHashMap<int3, int>(256, Allocator.Temp);
            NativeList<float2> open = new NativeList<float2>(256, Allocator.Temp); // heap of (estimate, node)

            nodes.Add(new Node() { position = from, parent = -1, cost = 0 });
            indexes.Add(from, 0);
            push(ref open, new float2(estimate(from, to), 0));
            int best = 0;
            float bestEstimate = estimate(from, to);
            int found = -1;

            while (open.Length > 0 && nodes.Length < maxNodes) {
                int current = (int)pop(ref open).y;
                Node node = nodes[current];
                if (node.closed) {
                    continue;
                }
                node.closed = true;
                nodes[current] = node;

                if (isAtGoal(node.position, to)) {
                    found = current;
                    break;
                }
                float left = estimate(node.position, to);
                if (left < bestEstimate) {
                    bestEstimate = left;
                    best = current;
                }

                for (int d = 0; d < 8; d++) {
                    int2 direction = Directions[d];
                    if (!tryMove(node.position, direction, out int3 next)) {
                        continue;
                    }
                    // no cutting corners: both sides of a diagonal are free
                    if (d >= 4 && (!tryMove(node.position, new int2(direction.x, 0), out _) || !tryMove(node.position, new int2(0, direction.y), out _))) {
                        continue;
                    }
                    float cost = node.cost + (d >= 4 ? 1.4142f : 1f) + math.abs(next.y - node.position.y) * 0.1f;
                    if (indexes.TryGetValue(next, out int known)) {
                        Node other = nodes[known];
                        if (other.closed || other.cost <= cost) {
                            continue;
                        }
                        other.cost = cost;
                        other.parent = current;
                        nodes[known] = other;
                        push(ref open, new float2(cost + estimate(next, to), known));
                    } else {
                        int index = nodes.Length;
                        nodes.Add(new Node() { position = next, parent = current, cost = cost });
                        indexes.Add(next, index);
                        push(ref open, new float2(cost + estimate(next, to), index));
                    }
                }
            }

            int last = found >= 0 ? found : best;
            result.Value = found >= 0 ? NavResult.Found : NavResult.Partial;
            NativeList<int3> raw = new NativeList<int3>(64, Allocator.Temp);
            for (int i = last; i >= 0; i = nodes[i].parent) {
                raw.Add(nodes[i].position);
            }
            // from the start, keeping only the corners
            int count = raw.Length;
            int a = count - 1;
            path.Add(raw[a]);
            while (a > 0) {
                int b = a - 1;
                while (b > 0 && canWalkStraight(raw[a], raw[b - 1])) {
                    b--;
                }
                path.Add(raw[b]);
                a = b;
            }
        }

        // octile distance, sideways only (steps cost little)
        private static float estimate(int3 a, int3 b) {
            float dx = math.abs(a.x - b.x);
            float dz = math.abs(a.z - b.z);
            return math.max(dx, dz) + 0.4142f * math.min(dx, dz);
        }

        private bool isAtGoal(int3 position, int3 to) {
            return math.max(math.abs(position.x - to.x), math.abs(position.z - to.z)) <= goalRange
                && math.abs(position.y - to.y) <= agent.height;
        }

        // the floor of the column closest to a cell, from below up to up and down to down
        private bool snap(int3 cell, int up, int down, out int floor) {
            floor = 0;
            int bestDistance = int.MaxValue;
            if (spans.TryGetFirstValue(cell.xz, out NavSpan span, out NativeParallelMultiHashMapIterator<int2> it)) {
                do {
                    int distance = math.abs(span.floor - cell.y);
                    if (span.floor <= cell.y + up && span.floor >= cell.y - down && distance < bestDistance
                            && canStand(new int3(cell.x, span.floor, cell.z))) {
                        bestDistance = distance;
                        floor = span.floor;
                    }
                } while (spans.TryGetNextValue(out span, ref it));
            }
            return bestDistance != int.MaxValue;
        }

        // a step to the next column: the floor there closest to its own, within what it can climb or drop
        private bool tryMove(int3 from, int2 direction, out int3 next) {
            next = default;
            int2 column = from.xz + direction;
            int bestDistance = int.MaxValue;
            if (spans.TryGetFirstValue(column, out NavSpan span, out NativeParallelMultiHashMapIterator<int2> it)) {
                do {
                    int rise = span.floor - from.y;
                    if (rise > agent.step || -rise > agent.drop || math.abs(rise) >= bestDistance) {
                        continue;
                    }
                    int3 candidate = new int3(column.x, span.floor, column.y);
                    // room to go up there from where it is (its head clears the step)
                    if (rise > 0 && !hasHeadroom(from, from.y + rise)) {
                        continue;
                    }
                    if (canStand(candidate)) {
                        bestDistance = math.abs(rise);
                        next = candidate;
                    }
                } while (spans.TryGetNextValue(out span, ref it));
            }
            return bestDistance != int.MaxValue;
        }

        // its body fits standing there: the floor under its center, and over every column it covers no
        // ground higher than a step and room for its height
        private bool canStand(int3 position) {
            for (int dx = -agent.radius; dx <= agent.radius; dx++) {
                for (int dz = -agent.radius; dz <= agent.radius; dz++) {
                    int2 column = position.xz + new int2(dx, dz);
                    bool center = dx == 0 && dz == 0;
                    bool fits = false;
                    if (spans.TryGetFirstValue(column, out NavSpan span, out NativeParallelMultiHashMapIterator<int2> it)) {
                        do {
                            if (center) {
                                fits = span.floor == position.y && span.ceiling - position.y >= agent.height && !(agent.avoidFluids && span.IsFluid);
                            } else {
                                fits = span.floor <= position.y + agent.step && span.ceiling >= position.y + agent.height
                                    && span.ceiling > position.y;
                            }
                        } while (!fits && spans.TryGetNextValue(out span, ref it));
                    }
                    if (!fits) {
                        return false;
                    }
                }
            }
            return true;
        }

        // the span it stands in goes up to floor + its height
        private bool hasHeadroom(int3 position, int floor) {
            if (spans.TryGetFirstValue(position.xz, out NavSpan span, out NativeParallelMultiHashMapIterator<int2> it)) {
                do {
                    if (span.floor == position.y) {
                        return span.ceiling >= floor + agent.height;
                    }
                } while (spans.TryGetNextValue(out span, ref it));
            }
            return false;
        }

        // it can walk from one to the other in a straight line: every column it crosses is a step it can take
        private bool canWalkStraight(int3 a, int3 b) {
            float2 delta = new float2(b.x - a.x, b.z - a.z);
            int samples = (int)math.ceil(math.max(math.abs(delta.x), math.abs(delta.y)) * 2);
            int3 at = a;
            for (int s = 1; s <= samples; s++) {
                float2 point = new float2(a.x, a.z) + delta * ((float)s / samples);
                int2 column = (int2)math.round(point);
                int2 direction = column - at.xz;
                if (direction.x == 0 && direction.y == 0) {
                    continue;
                }
                if (!tryMove(at, direction, out int3 next)) {
                    return false;
                }
                if (direction.x != 0 && direction.y != 0
                        && (!tryMove(at, new int2(direction.x, 0), out _) || !tryMove(at, new int2(0, direction.y), out _))) {
                    return false;
                }
                at = next;
            }
            return at.y == b.y;
        }

        private static void push(ref NativeList<float2> heap, float2 item) {
            heap.Add(item);
            int i = heap.Length - 1;
            while (i > 0) {
                int parent = (i - 1) / 2;
                if (heap[parent].x <= heap[i].x) {
                    break;
                }
                float2 swap = heap[parent];
                heap[parent] = heap[i];
                heap[i] = swap;
                i = parent;
            }
        }

        private static float2 pop(ref NativeList<float2> heap) {
            float2 top = heap[0];
            int last = heap.Length - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);
            int i = 0;
            while (true) {
                int left = i * 2 + 1;
                int right = left + 1;
                int smallest = i;
                if (left < heap.Length && heap[left].x < heap[smallest].x) {
                    smallest = left;
                }
                if (right < heap.Length && heap[right].x < heap[smallest].x) {
                    smallest = right;
                }
                if (smallest == i) {
                    break;
                }
                float2 swap = heap[smallest];
                heap[smallest] = heap[i];
                heap[i] = swap;
                i = smallest;
            }
            return top;
        }
    }
}
