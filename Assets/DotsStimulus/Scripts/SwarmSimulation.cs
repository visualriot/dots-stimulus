using System;
using System.Collections.Generic;

namespace DotsStimulus
{
    public enum NeighbourRule { Metric, Topological }
    public enum VolumeShape { Box, Sphere }
    public enum BoundaryMode { MagneticSteering, SmoothLinearBounce, FadeAndRespawn }
    public enum FadeRespawnMode { Centre, RandomWithinCore }

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
        public VolumeShape volumeShape = VolumeShape.Box;
        public double sphereRadius = 10;
        public double boundaryBuffer = 1;
        public BoundaryMode boundaryMode = BoundaryMode.MagneticSteering;
        public double fadeBuffer = 2;
        public double fadeLifetimeMin = 1;
        public double fadeLifetimeMax = 2;
        public FadeRespawnMode fadeRespawnMode = FadeRespawnMode.Centre;
        public bool wrapX = true, wrapY = true, wrapZ = true;

        public SwarmSettings Copy() => (SwarmSettings)MemberwiseClone();

        public void Validate()
        {
            if (count < 1 || count > 16000) throw new ArgumentOutOfRangeException(nameof(count), "Use 1..16000 dots; start with 1000.");
            if (topologicalK < 1 || topologicalK > count) throw new ArgumentOutOfRangeException(nameof(topologicalK), "k must be between 1 and count, including self.");
            if (rule != NeighbourRule.Metric && rule != NeighbourRule.Topological) throw new ArgumentOutOfRangeException(nameof(rule));
            if (volumeShape != VolumeShape.Box && volumeShape != VolumeShape.Sphere) throw new ArgumentOutOfRangeException(nameof(volumeShape));
            if (boundaryMode != BoundaryMode.MagneticSteering && boundaryMode != BoundaryMode.SmoothLinearBounce &&
                boundaryMode != BoundaryMode.FadeAndRespawn) throw new ArgumentOutOfRangeException(nameof(boundaryMode));
            if (fadeRespawnMode != FadeRespawnMode.Centre && fadeRespawnMode != FadeRespawnMode.RandomWithinCore)
                throw new ArgumentOutOfRangeException(nameof(fadeRespawnMode));
            Positive(width, nameof(width)); Positive(height, nameof(height)); Positive(depth, nameof(depth));
            Positive(sphereRadius, nameof(sphereRadius)); Positive(boundaryBuffer, nameof(boundaryBuffer)); Positive(fadeBuffer, nameof(fadeBuffer));
            Positive(fadeLifetimeMin, nameof(fadeLifetimeMin)); Positive(fadeLifetimeMax, nameof(fadeLifetimeMax));
            if (fadeLifetimeMax < fadeLifetimeMin) throw new ArgumentOutOfRangeException(nameof(fadeLifetimeMax), "Maximum fade lifetime must be at least the minimum fade lifetime.");
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
        public static DVector3 operator -(DVector3 a) => new DVector3(-a.x, -a.y, -a.z);
        public static DVector3 operator *(DVector3 a, double b) => new DVector3(a.x * b, a.y * b, a.z * b);
        public static DVector3 operator /(DVector3 a, double b) => a * (1 / b);
        public static DVector3 Cross(DVector3 a, DVector3 b) => new DVector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
    }

    /// <summary>Pure C# port of info/PLS_Stimulus_Base.m. No Unity, physics, or rendering dependencies.</summary>
    public sealed class SwarmSimulation
    {
        public sealed class FadingDot
        {
            public DVector3 Position { get; internal set; }
            public double Opacity { get; internal set; }
            public double Lifetime { get; internal set; }
            public double Elapsed { get; internal set; }
            internal DVector3 StartPosition;
            internal DVector3 Direction;
            internal double TravelDistance;
        }

        // MATLAB eps is machine epsilon at 1, NOT C# double.Epsilon (smallest subnormal).
        public const double MatlabEpsilon = 2.2204460492503131e-16;
        public readonly SwarmSettings Settings;
        public readonly DVector3[] Positions;
        public readonly DVector3[] Velocities;
        public IReadOnlyList<FadingDot> FadingDots => fadingDots;
        public long StepCount { get; private set; }
        private readonly DVector3[] nextDirections;
        private readonly DVector3[] bounceEntries, bounceDirections;
        private readonly bool[] bad;
        private readonly bool[] bounceActive;
        private readonly double[] bounceDistances, bounceElapsed, bounceDurations;
        private readonly double[] distances, sortedDistances;
        private readonly List<FadingDot> fadingDots = new List<FadingDot>();
        private readonly Random random;

        public SwarmSimulation(SwarmSettings settings, int seed)
        {
            settings.Validate();
            Settings = settings.Copy();
            random = new Random(seed);
            Positions = new DVector3[Settings.count];
            Velocities = new DVector3[Settings.count];
            nextDirections = new DVector3[Settings.count];
            bounceEntries = new DVector3[Settings.count];
            bounceDirections = new DVector3[Settings.count];
            bad = new bool[Settings.count];
            bounceActive = new bool[Settings.count];
            bounceDistances = new double[Settings.count];
            bounceElapsed = new double[Settings.count];
            bounceDurations = new double[Settings.count];
            distances = new double[Settings.count];
            sortedDistances = new double[Settings.count];
            Reset();
        }

        // Reuses the random stream, like MATLAB R/D/arrow restarts after rng('shuffle').
        public void Reset()
        {
            Array.Clear(bounceActive, 0, bounceActive.Length);
            fadingDots.Clear();
            for (int i = 0; i < Positions.Length; i++)
            {
                Positions[i] = RandomPointInCore();
                Velocities[i] = RandomSpatialDirection() * Settings.speed;
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
            AdvanceFadingDots(deltaSeconds);
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
                var position = Positions[i] + velocity * deltaSeconds;
                if (Settings.dimension2D)
                {
                    bounceActive[i] = false;
                    if (Settings.wrapX) position.x = Wrap(position.x, -Settings.width / 2, Settings.width);
                    if (Settings.wrapY) position.y = Wrap(position.y, -Settings.height / 2, Settings.height);
                    position.z = Settings.startDepth;
                }
                else if (Settings.boundaryMode == BoundaryMode.SmoothLinearBounce)
                {
                    if (bounceActive[i])
                    {
                        AdvanceLinearBounce(i, deltaSeconds, out position, out velocity);
                    }
                    else if (IsOutsideCore(position) && TryBeginLinearBounce(i, Positions[i], velocity, deltaSeconds, out position, out velocity))
                    {
                        // The bounce state supplies both position and velocity until the dot returns to the core.
                    }
                    else
                    {
                        ConstrainToOuterBoundary(ref position, ref velocity);
                    }
                }
                else if (Settings.boundaryMode == BoundaryMode.FadeAndRespawn)
                {
                    bounceActive[i] = false;
                    if (IsOutsideCore(position))
                    {
                        BeginFade(Positions[i], velocity, deltaSeconds);
                        RespawnAfterFade(i);
                        position = Positions[i];
                        velocity = Velocities[i];
                    }
                }
                else
                {
                    bounceActive[i] = false;
                    if (TryGetBoundarySteering(position, out DVector3 inward, out double penetration))
                    {
                        double weight = SmoothStep(penetration);
                        direction = SlerpUnit(direction, inward, weight);
                        velocity = direction * Settings.speed;
                        position = Positions[i] + velocity * deltaSeconds;
                    }
                    ConstrainToOuterBoundary(ref position, ref velocity);
                }
                Velocities[i] = velocity;
                Positions[i] = position;
            }
            StepCount++;
        }

        private DVector3 PlanarDirection()
        {
            double phi = random.NextDouble() * 2 * Math.PI;
            return new DVector3(Math.Cos(phi), Math.Sin(phi), 0);
        }

        private DVector3 RandomSpatialDirection()
        {
            double azimuth = random.NextDouble() * 2 * Math.PI;
            double cosine = 2 * random.NextDouble() - 1;
            double sine = Math.Sqrt(1 - cosine * cosine);
            return new DVector3(sine * Math.Sin(azimuth), cosine, sine * Math.Cos(azimuth));
        }

        private DVector3 RandomPointInCore()
        {
            if (!Settings.dimension2D && Settings.volumeShape == VolumeShape.Sphere) return RandomPointInSphere();
            return new DVector3((random.NextDouble() - .5) * Settings.width,
                (random.NextDouble() - .5) * Settings.height, Settings.startDepth + random.NextDouble() * Settings.depth);
        }

        private DVector3 RandomPointInSphere()
        {
            double radius = Settings.sphereRadius * Math.Pow(random.NextDouble(), 1.0 / 3.0);
            double azimuth = random.NextDouble() * 2 * Math.PI;
            double cosine = 2 * random.NextDouble() - 1;
            double sine = Math.Sqrt(1 - cosine * cosine);
            var centre = SphereCentre();
            return centre + new DVector3(sine * Math.Sin(azimuth), cosine, sine * Math.Cos(azimuth)) * radius;
        }

        private void RespawnAfterFade(int index)
        {
            Positions[index] = Settings.fadeRespawnMode == FadeRespawnMode.Centre ? CoreCentre() : RandomPointInCore();
            Velocities[index] = RandomSpatialDirection() * Settings.speed;
        }

        private void BeginFade(DVector3 origin, DVector3 velocity, double deltaSeconds)
        {
            double speed = velocity.Length;
            if (speed < MatlabEpsilon) return;
            DVector3 direction = velocity / speed;
            if (!TryGetFadeExitDistances(origin, direction, out double coreExit, out double outerExit)) return;

            double lifetime = Settings.fadeLifetimeMin + random.NextDouble() * (Settings.fadeLifetimeMax - Settings.fadeLifetimeMin);
            double secondsToCore = coreExit / speed;
            double elapsed = Math.Max(0, deltaSeconds - secondsToCore);
            if (elapsed >= lifetime) return;
            double availableDistance = Math.Max(0, outerExit - coreExit);
            var dot = new FadingDot
            {
                StartPosition = origin + direction * coreExit,
                Direction = direction,
                TravelDistance = Math.Min(availableDistance, .5 * speed * lifetime),
                Lifetime = lifetime,
                Elapsed = elapsed
            };
            UpdateFadingDot(dot);
            fadingDots.Add(dot);
        }

        private void AdvanceFadingDots(double deltaSeconds)
        {
            for (int i = fadingDots.Count - 1; i >= 0; i--)
            {
                FadingDot dot = fadingDots[i];
                dot.Elapsed += deltaSeconds;
                if (dot.Elapsed >= dot.Lifetime)
                {
                    fadingDots.RemoveAt(i);
                    continue;
                }
                UpdateFadingDot(dot);
            }
        }

        private static void UpdateFadingDot(FadingDot dot)
        {
            double progress = Clamp01(dot.Elapsed / dot.Lifetime);
            double easedDistance = dot.TravelDistance * (2 * progress - progress * progress);
            dot.Position = dot.StartPosition + dot.Direction * easedDistance;
            dot.Opacity = 1 - progress;
        }

        private bool TryGetFadeExitDistances(DVector3 origin, DVector3 direction, out double coreExit, out double outerExit)
        {
            if (Settings.volumeShape == VolumeShape.Sphere)
            {
                DVector3 relative = origin - SphereCentre();
                double projection = relative.x * direction.x + relative.y * direction.y + relative.z * direction.z;
                double centreDistanceSquared = relative.x * relative.x + relative.y * relative.y + relative.z * relative.z;
                double coreDiscriminant = projection * projection - centreDistanceSquared + Settings.sphereRadius * Settings.sphereRadius;
                double outerRadius = Settings.sphereRadius + Settings.fadeBuffer;
                double outerDiscriminant = projection * projection - centreDistanceSquared + outerRadius * outerRadius;
                if (coreDiscriminant < 0 || outerDiscriminant < 0)
                {
                    coreExit = outerExit = 0;
                    return false;
                }
                coreExit = Math.Max(0, -projection + Math.Sqrt(coreDiscriminant));
                outerExit = Math.Max(0, -projection + Math.Sqrt(outerDiscriminant));
                return outerExit > coreExit;
            }

            coreExit = RayBoxExit(origin, direction, Settings.width * .5, Settings.height * .5,
                Settings.startDepth, Settings.startDepth + Settings.depth);
            double buffer = Settings.fadeBuffer;
            outerExit = RayBoxExit(origin, direction, Settings.width * .5 + buffer, Settings.height * .5 + buffer,
                Settings.startDepth - buffer, Settings.startDepth + Settings.depth + buffer);
            return !double.IsInfinity(coreExit) && !double.IsInfinity(outerExit) && outerExit > coreExit;
        }

        private bool TryBeginLinearBounce(int index, DVector3 origin, DVector3 velocity, double deltaSeconds,
            out DVector3 position, out DVector3 resultVelocity)
        {
            double velocityLength = velocity.Length;
            if (velocityLength < MatlabEpsilon || !TryGetRayExitDistances(origin, velocity / velocityLength, out double coreExit, out double outerExit))
            {
                position = origin + velocity * deltaSeconds;
                resultVelocity = velocity;
                return false;
            }

            DVector3 direction = velocity / velocityLength;
            double travelDistance = outerExit - coreExit;
            if (travelDistance <= MatlabEpsilon)
            {
                position = origin + velocity * deltaSeconds;
                resultVelocity = velocity;
                return false;
            }

            bounceActive[index] = true;
            bounceEntries[index] = origin + direction * coreExit;
            bounceDirections[index] = direction;
            bounceDistances[index] = travelDistance;
            bounceElapsed[index] = 0;
            bounceDurations[index] = Math.PI * travelDistance / Settings.speed;
            double secondsToCore = coreExit / velocityLength;
            AdvanceLinearBounce(index, Math.Max(0, deltaSeconds - secondsToCore), out position, out resultVelocity);
            return true;
        }

        private void AdvanceLinearBounce(int index, double deltaSeconds, out DVector3 position, out DVector3 velocity)
        {
            bounceElapsed[index] += deltaSeconds;
            if (bounceElapsed[index] >= bounceDurations[index])
            {
                bounceActive[index] = false;
                position = bounceEntries[index];
                velocity = -bounceDirections[index] * Settings.speed;
                return;
            }

            double phase = Math.PI * bounceElapsed[index] / bounceDurations[index];
            position = bounceEntries[index] + bounceDirections[index] * (bounceDistances[index] * Math.Sin(phase));
            double speedScale = Math.Cos(phase);
            // Keep a numerically non-zero direction at the exact turnaround so the
            // MATLAB alignment calculation never receives a zero self velocity.
            if (Math.Abs(speedScale) < 1e-9) speedScale = phase < Math.PI * .5 ? 1e-9 : -1e-9;
            velocity = bounceDirections[index] * (Settings.speed * speedScale);
        }

        private bool TryGetRayExitDistances(DVector3 origin, DVector3 direction, out double coreExit, out double outerExit)
        {
            if (Settings.volumeShape == VolumeShape.Sphere)
            {
                DVector3 relative = origin - SphereCentre();
                double projection = relative.x * direction.x + relative.y * direction.y + relative.z * direction.z;
                double centreDistanceSquared = relative.x * relative.x + relative.y * relative.y + relative.z * relative.z;
                double coreDiscriminant = projection * projection - centreDistanceSquared + Settings.sphereRadius * Settings.sphereRadius;
                double outerRadius = Settings.sphereRadius + Settings.boundaryBuffer;
                double outerDiscriminant = projection * projection - centreDistanceSquared + outerRadius * outerRadius;
                if (coreDiscriminant < 0 || outerDiscriminant < 0)
                {
                    coreExit = outerExit = 0;
                    return false;
                }
                coreExit = Math.Max(0, -projection + Math.Sqrt(coreDiscriminant));
                outerExit = Math.Max(0, -projection + Math.Sqrt(outerDiscriminant));
                return outerExit > coreExit;
            }

            coreExit = RayBoxExit(origin, direction, Settings.width * .5, Settings.height * .5,
                Settings.startDepth, Settings.startDepth + Settings.depth);
            double buffer = Settings.boundaryBuffer;
            outerExit = RayBoxExit(origin, direction, Settings.width * .5 + buffer, Settings.height * .5 + buffer,
                Settings.startDepth - buffer, Settings.startDepth + Settings.depth + buffer);
            return !double.IsInfinity(coreExit) && !double.IsInfinity(outerExit) && outerExit > coreExit;
        }

        private static double RayBoxExit(DVector3 origin, DVector3 direction, double halfWidth, double halfHeight, double near, double far)
        {
            double exit = double.PositiveInfinity;
            if (direction.x > MatlabEpsilon) exit = Math.Min(exit, (halfWidth - origin.x) / direction.x);
            else if (direction.x < -MatlabEpsilon) exit = Math.Min(exit, (-halfWidth - origin.x) / direction.x);
            if (direction.y > MatlabEpsilon) exit = Math.Min(exit, (halfHeight - origin.y) / direction.y);
            else if (direction.y < -MatlabEpsilon) exit = Math.Min(exit, (-halfHeight - origin.y) / direction.y);
            if (direction.z > MatlabEpsilon) exit = Math.Min(exit, (far - origin.z) / direction.z);
            else if (direction.z < -MatlabEpsilon) exit = Math.Min(exit, (near - origin.z) / direction.z);
            return Math.Max(0, exit);
        }

        private bool IsOutsideCore(DVector3 position)
        {
            if (Settings.volumeShape == VolumeShape.Sphere)
                return (position - SphereCentre()).Length > Settings.sphereRadius;
            return Math.Abs(position.x) > Settings.width * .5 || Math.Abs(position.y) > Settings.height * .5 ||
                position.z < Settings.startDepth || position.z > Settings.startDepth + Settings.depth;
        }

        public bool IsInsideCore(DVector3 position) => !IsOutsideCore(position);

        private bool TryGetBoundarySteering(DVector3 position, out DVector3 inward, out double normalizedPenetration)
        {
            if (Settings.volumeShape == VolumeShape.Sphere)
            {
                DVector3 radial = position - SphereCentre();
                double distance = radial.Length;
                double penetration = distance - Settings.sphereRadius;
                if (penetration <= 0)
                {
                    inward = new DVector3();
                    normalizedPenetration = 0;
                    return false;
                }

                inward = -radial / distance;
                normalizedPenetration = Clamp01(penetration / Settings.boundaryBuffer);
                return true;
            }

            double halfWidth = Settings.width * .5;
            double halfHeight = Settings.height * .5;
            double near = Settings.startDepth;
            double far = Settings.startDepth + Settings.depth;
            var correction = new DVector3(
                position.x < -halfWidth ? -halfWidth - position.x : position.x > halfWidth ? halfWidth - position.x : 0,
                position.y < -halfHeight ? -halfHeight - position.y : position.y > halfHeight ? halfHeight - position.y : 0,
                position.z < near ? near - position.z : position.z > far ? far - position.z : 0);
            double penetrationBox = Math.Max(Math.Abs(correction.x), Math.Max(Math.Abs(correction.y), Math.Abs(correction.z)));
            if (penetrationBox <= 0)
            {
                inward = new DVector3();
                normalizedPenetration = 0;
                return false;
            }

            inward = correction / correction.Length;
            normalizedPenetration = Clamp01(penetrationBox / Settings.boundaryBuffer);
            return true;
        }

        private void ConstrainToOuterBoundary(ref DVector3 position, ref DVector3 velocity)
        {
            if (Settings.volumeShape == VolumeShape.Sphere)
            {
                DVector3 centre = SphereCentre();
                DVector3 radial = position - centre;
                double outerRadius = Settings.sphereRadius + Settings.boundaryBuffer;
                double distance = radial.Length;
                if (distance <= outerRadius) return;
                DVector3 outward = radial / distance;
                position = centre + outward * outerRadius;
                velocity = -outward * Settings.speed;
                return;
            }

            double buffer = Settings.boundaryBuffer;
            double halfWidth = Settings.width * .5 + buffer;
            double halfHeight = Settings.height * .5 + buffer;
            double near = Settings.startDepth - buffer;
            double far = Settings.startDepth + Settings.depth + buffer;
            var constrained = new DVector3(
                Clamp(position.x, -halfWidth, halfWidth),
                Clamp(position.y, -halfHeight, halfHeight),
                Clamp(position.z, near, far));
            if (constrained.x == position.x && constrained.y == position.y && constrained.z == position.z) return;
            position = constrained;
            if (TryGetBoundarySteering(position, out DVector3 inward, out _)) velocity = inward * Settings.speed;
        }

        private DVector3 CoreCentre() => Settings.volumeShape == VolumeShape.Sphere
            ? SphereCentre()
            : new DVector3(0, 0, Settings.startDepth + Settings.depth * .5);

        private DVector3 SphereCentre() => new DVector3(0, 0, Settings.startDepth + Settings.sphereRadius);

        private static DVector3 SlerpUnit(DVector3 from, DVector3 to, double amount)
        {
            amount = Clamp01(amount);
            double fromLength = from.Length;
            double toLength = to.Length;
            if (fromLength < MatlabEpsilon) return toLength < MatlabEpsilon ? new DVector3(0, 0, 1) : to / toLength;
            if (toLength < MatlabEpsilon) return from / fromLength;
            from /= fromLength;
            to /= toLength;
            double dot = Clamp(from.x * to.x + from.y * to.y + from.z * to.z, -1, 1);
            if (dot > .999999)
            {
                DVector3 blended = from * (1 - amount) + to * amount;
                return blended / blended.Length;
            }
            if (dot < -.999999)
            {
                DVector3 helper = Math.Abs(from.x) < .9 ? new DVector3(1, 0, 0) : new DVector3(0, 1, 0);
                DVector3 perpendicular = DVector3.Cross(from, helper);
                perpendicular /= perpendicular.Length;
                double angle = Math.PI * amount;
                return from * Math.Cos(angle) + perpendicular * Math.Sin(angle);
            }

            double theta = Math.Acos(dot);
            double sinTheta = Math.Sin(theta);
            return from * (Math.Sin((1 - amount) * theta) / sinTheta) + to * (Math.Sin(amount * theta) / sinTheta);
        }

        private static double SmoothStep(double value)
        {
            value = Clamp01(value);
            return value * value * (3 - 2 * value);
        }

        private static double Clamp01(double value) => Clamp(value, 0, 1);
        private static double Clamp(double value, double minimum, double maximum) => Math.Max(minimum, Math.Min(maximum, value));

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
