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
            return new PointerUserProfile
            {
                Seed = seed,
                SpeedBias = Next(random, 0.86, 1.18),
                PrecisionBias = Next(random, 0.82, 1.20),
                CurveBias = Next(random, 0.72, 1.30),
                TremorBias = Next(random, 0.70, 1.25),
                ReactionBias = Next(random, 0.82, 1.24),
                WheelBias = Next(random, 0.84, 1.18),
                OvershootChance = Next(random, 0.06, 0.19)
            };
        }

        private static double Next(Random random, double min, double max) =>
            min + (random.NextDouble() * (max - min));
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
