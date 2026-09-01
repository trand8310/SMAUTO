

namespace PlaywrightHumanInput;

    public sealed class StableRandom
    {
        private ulong _state;

        public StableRandom(int seed)
            : this(unchecked((ulong)(uint)seed))
        {
        }

        public StableRandom(ulong seed)
        {
            _state = seed ^ 0x9E3779B97F4A7C15UL;
        }

        public ulong NextUInt64()
        {
            ulong value = _state += 0x9E3779B97F4A7C15UL;

            value = (value ^ (value >> 30)) *
                    0xBF58476D1CE4E5B9UL;

            value = (value ^ (value >> 27)) *
                    0x94D049BB133111EBUL;

            return value ^ (value >> 31);
        }

        public double NextDouble()
        {
            // 取 53 位有效随机数据。
            return (NextUInt64() >> 11) *
                   (1.0 / 9007199254740992.0);
        }

        public int NextInt(int minValue, int maxValueExclusive)
        {
            if (maxValueExclusive <= minValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxValueExclusive));
            }

            uint range = unchecked(
                (uint)(maxValueExclusive - minValue));

            return minValue +
                   (int)(NextUInt64() % range);
        }

        public bool NextBool(double probability)
        {
            probability = Math.Clamp(probability, 0, 1);
            return NextDouble() < probability;
        }
    }

