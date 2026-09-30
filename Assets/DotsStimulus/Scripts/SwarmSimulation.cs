using System;

namespace DotsStimulus
{
    public enum NeighbourRule { Metric, Topological }

    [Serializable]
    public sealed class SwarmSettings
    {
        public int count = 1000;
        public bool dimension2D = true;
        public NeighbourRule rule = NeighbourRule.Metric;
        public double metricRadius = 2.0;
        // MATLAB's kth distance includes self and includes all ties at that distance.
        public int topologicalK = 2;
        public double topologicalRadiusCap = 1000;
        public double speed = 3;
        public double noise = 0;
        public double width = 20, height = 20, depth = 20, startDepth = 50;
        public bool wrapX = true, wrapY = true, wrapZ = true;

        public SwarmSettings Copy() => (SwarmSettings)MemberwiseClone();

        public void Validate()
        {
            if (count < 1 || count > 16000) throw new ArgumentOutOfRangeException(nameof(count), "Use 1..16000 dots; start with 1000.");
            if (topologicalK < 1 || topologicalK > count) throw new ArgumentOutOfRangeException(nameof(topologicalK), "k must be between 1 and count, including self.");
            if (rule != NeighbourRule.Metric && rule != NeighbourRule.Topological) throw new ArgumentOutOfRangeException(nameof(rule));
            Positive(width, nameof(width)); Positive(height, nameof(height)); Positive(depth, nameof(depth));
            Positive(startDepth, nameof(startDepth)); Positive(speed, nameof(speed));
            Nonnegative(metricRadius, nameof(metricRadius)); Nonnegative(topologicalRadiusCap, nameof(topologicalRadiusCap));
            Nonnegative(noise, nameof(noise));
        }

        private static void Positive(double v, string name)
        {
            if (double.IsNaN(v) || double.IsInfinity(v) || v <= 0) throw new ArgumentOutOfRangeException(name);
        }
        private static void Nonnegative(double v, string name)
        {
            if (double.IsNaN(v) || double.IsInfinity(v) || v < 0) throw new ArgumentOutOfRangeException(name);
        }
    }

    // Keep MATLAB's double precision in the simulation; convert to floats only for rendering.
    public struct DVector3
    {
        public double x, y, z;
        public DVector3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public double Length => Math.Sqrt(x * x + y * y + z * z);
        public static DVector3 operator +(DVector3 a, DVector3 b) => new DVector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static DVector3 operator -(DVector3 a, DVector3 b) => new DVector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static DVector3 operator *(DVector3 a, double b) => new DVector3(a.x * b, a.y * b, a.z * b);
        public static DVector3 operator /(DVector3 a, double b) => a * (1 / b);
        public static DVector3 Cross(DVector3 a, DVector3 b) => new DVector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
    }

    /// <summary>Pure C# port of info/PLS_Stimulus_Base.m. No Unity, physics, or rendering dependencies.</summary>
    public sealed class SwarmSimulation
    {
        // MATLAB eps is machine epsilon at 1, NOT C# double.Epsilon (smallest subnormal).
        public const double MatlabEpsilon = 2.2204460492503131e-16;
        public readonly SwarmSettings Settings;
        public readonly DVector3[] Positions;
        public readonly DVector3[] Velocities;
        public long StepCount { get; private set; }
        private readonly DVector3[] nextDirections;
        private readonly bool[] bad;
        private readonly double[] distances, sortedDistances;
        private readonly Random random;

        public SwarmSimulation(SwarmSettings settings, int seed)
        {
            settings.Validate();
            Settings = settings.Copy();
            random = new Random(seed);
            Positions = new DVector3[Settings.count];
            Velocities = new DVector3[Settings.count];
            nextDirections = new DVector3[Settings.count];
            bad = new bool[Settings.count];
            distances = new double[Settings.count];
            sortedDistances = new double[Settings.count];
            Reset();
        }

        // Reuses the random stream, like MATLAB R/D/arrow restarts after rng('shuffle').
        public void Reset()
        {
            for (int i = 0; i < Positions.Length; i++)
            {
                Positions[i] = new DVector3((random.NextDouble() - .5) * Settings.width,
                    (random.NextDouble() - .5) * Settings.height, Settings.startDepth + random.NextDouble() * Settings.depth);
                double azimuth = random.NextDouble() * 2 * Math.PI;
                double cosine = 2 * random.NextDouble() - 1;
                double sine = Math.Sqrt(1 - cosine * cosine);
                Velocities[i] = new DVector3(sine * Math.Sin(azimuth), cosine, sine * Math.Cos(azimuth)) * Settings.speed;
                if (Settings.dimension2D)
                {
                    Positions[i].z = Settings.startDepth;
                    Velocities[i] = PlanarDirection() * Settings.speed;
                }
            }
            StepCount = 0;
        }

        public void Step(double deltaSeconds)
        {
            if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            int firstBad = -1;
            for (int i = 0; i < Positions.Length; i++)
            {
                // Deliberately ordinary Euclidean distances, even when positions wrap.
                for (int j = 0; j < Positions.Length; j++) distances[j] = (Positions[j] - Positions[i]).Length;
                double threshold = Settings.metricRadius;
                if (Settings.rule == NeighbourRule.Topological)
                {
                    Array.Copy(distances, sortedDistances, distances.Length);
                    // Only the kth value matters. Partition instead of sorting every row.
                    threshold = Math.Min(SelectKth(sortedDistances, Settings.topologicalK - 1), Settings.topologicalRadiusCap);
                }
                var sum = new DVector3();
                for (int j = 0; j < Positions.Length; j++)
                    if (distances[j] <= threshold) sum += Velocities[j];
                double length = sum.Length;
                bad[i] = length < MatlabEpsilon;
                if (bad[i] && firstBad < 0) firstBad = i;
                nextDirections[i] = sum / (bad[i] ? 1 : length);
            }

            if (firstBad >= 0)
            {
                // Literal source behavior: ax is velocity, not a unit vector. Thus p2
                // has length speed. Preserve this rare 3D speed defect; see the guide.
                var axis = Velocities[firstBad];
                var helper = Math.Abs(axis.x) > .9 ? new DVector3(0, 1, 0) : new DVector3(1, 0, 0);
                var p1 = DVector3.Cross(axis, helper);
                p1 /= p1.Length;
                var p2 = DVector3.Cross(axis, p1);
                double angle = random.NextDouble() * 2 * Math.PI;
                var shared = p1 * Math.Cos(angle) + p2 * Math.Sin(angle);
                for (int i = 0; i < bad.Length; i++) if (bad[i]) nextDirections[i] = shared;
            }

            // Commit only after every dot's direction has been calculated from old velocities.
            for (int i = 0; i < Positions.Length; i++)
            {
                var direction = nextDirections[i];
                if (Settings.noise > 0)
                {
                    direction.x += Settings.noise * (random.NextDouble() - .5);
                    direction.y += Settings.noise * (random.NextDouble() - .5);
                    if (!Settings.dimension2D) direction.z += Settings.noise * (random.NextDouble() - .5);
                    double length = direction.Length;
                    direction /= length < MatlabEpsilon ? 1 : length;
                }
                var velocity = direction * Settings.speed;
                if (Settings.dimension2D)
                {
                    velocity.z = 0;
                    double planarSpeed = velocity.Length;
                    if (planarSpeed < MatlabEpsilon) { velocity = PlanarDirection() * Settings.speed; planarSpeed = Settings.speed; }
                    velocity = velocity * (Settings.speed / planarSpeed);
                }
                Velocities[i] = velocity;
                var position = Positions[i] + velocity * deltaSeconds;
                if (Settings.wrapX) position.x = Wrap(position.x, -Settings.width / 2, Settings.width);
                if (Settings.wrapY) position.y = Wrap(position.y, -Settings.height / 2, Settings.height);
                if (Settings.dimension2D) position.z = Settings.startDepth;
                else if (Settings.wrapZ) position.z = Wrap(position.z, Settings.startDepth, Settings.depth);
                Positions[i] = position;
            }
            StepCount++;
        }

        private DVector3 PlanarDirection()
        {
            double phi = random.NextDouble() * 2 * Math.PI;
            return new DVector3(Math.Cos(phi), Math.Sin(phi), 0);
        }

        private static double SelectKth(double[] values, int k)
        {
            int left = 0, right = values.Length - 1;
            while (left < right)
            {
                double pivot = values[left + (right - left) / 2];
                int i = left, j = right;
                while (i <= j)
                {
                    while (values[i] < pivot) i++;
                    while (values[j] > pivot) j--;
                    if (i <= j)
                    {
                        double swap = values[i]; values[i] = values[j]; values[j] = swap;
                        i++; j--;
                    }
                }
                if (k <= j) right = j;
                else if (k >= i) left = i;
                else return values[k];
            }
            return values[k];
        }

        // C# % alone is not MATLAB mod for negative positions.
        public static double Wrap(double value, double minimum, double size) => minimum + ((value - minimum) % size + size) % size;
    }
}
