using Microsoft.Playwright;
using System.Runtime.CompilerServices;
using SMAd.HumanInput;

namespace SMAd.HumanPointerP2
{
    public readonly record struct PointerPosition(double X, double Y)
    {
        public static PointerPosition Lerp(PointerPosition a, PointerPosition b, double t) =>
            new(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));
    }

    public sealed class PointerTracePoint
    {
        public double X { get; init; }
        public double Y { get; init; }
        public int DelayMs { get; init; }
        public double TimeMs { get; init; }
    }

    public sealed class PointerTrace
    {
        public PointerPosition Start { get; init; }
        public PointerPosition End { get; init; }
        public double DurationMs { get; init; }
        public IReadOnlyList<PointerTracePoint> Points { get; init; } = Array.Empty<PointerTracePoint>();
    }

    public sealed class PointerUserProfile
    {
        public int Seed { get; init; }
        public double SpeedBias { get; init; } = 1.0;
        public double PrecisionBias { get; init; } = 1.0;
        public double CurveBias { get; init; } = 1.0;
        public double TremorBias { get; init; } = 1.0;
        public double ReactionBias { get; init; } = 1.0;
        public double WheelBias { get; init; } = 1.0;
        public double OvershootChance { get; init; } = 0.12;

        public static PointerUserProfile Create(int seed)
        {
            var random = new Random(seed);
            double speedBias = Around(random, 1.0, 0.085, 0.84, 1.18);
            return new PointerUserProfile
            {
                Seed = seed,
                SpeedBias = speedBias,
                PrecisionBias = Around(random, 1.0, 0.09, 0.82, 1.20),
                CurveBias = Around(random, 1.0, 0.14, 0.72, 1.28),
                TremorBias = Around(random, 1.0, 0.12, 0.72, 1.25),
                ReactionBias = Math.Clamp((2.0 - speedBias) + Around(random, 0, 0.055, -0.11, 0.11), 0.82, 1.22),
                WheelBias = Around(random, 1.0, 0.09, 0.84, 1.18),
                OvershootChance = Around(random, 0.10, 0.035, 0.045, 0.18)
            };
        }

        private static double Around(Random random, double mean, double deviation, double min, double max)
        {
            double u1 = Math.Max(double.Epsilon, random.NextDouble());
            double u2 = random.NextDouble();
            double normal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            return Math.Clamp(mean + (normal * deviation), min, max);
        }
    }

    public sealed class HumanPointerSession
    {
        private sealed class CursorState
        {
            public PointerPosition Position;
        }

        private readonly ConditionalWeakTable<IPage, CursorState> _cursorStates = new();

        public HumanPointerSession(PointerUserProfile profile)
        {
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            Random = new Random(profile.Seed);
        }

        public PointerUserProfile Profile { get; }
        public Random Random { get; }
        public HumanActionIntent LastIntent { get; set; } = HumanActionIntent.Preview;
        public int ConsecutiveForwardScrolls { get; set; }

        public PointerPosition GetCursor(IPage page)
        {
            return _cursorStates.GetValue(page, _ => new CursorState
            {
                // Playwright 新 Page 的逻辑鼠标位置从视口左上角开始。
                Position = new PointerPosition(0, 0)
            }).Position;
        }

        public void SetCursor(IPage page, PointerPosition position)
        {
            _cursorStates.GetValue(page, _ => new CursorState()).Position = position;
        }
    }

    public sealed class HumanPointerOperatorOptions
    {
        public HumanPointerSession? Session { get; set; }
        public Action<string>? Log { get; set; }
        public double DelayFactor { get; set; } = 1.0;
        public bool VerifyHitTarget { get; set; } = true;
    }
}
