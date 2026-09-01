using System;
using System.Collections.Generic;
using System.Linq;

namespace PlaywrightHumanInput
{
    public sealed class HumanBehaviorModel
    {
        public SwipeIntent DecideNextIntent(HumanTouchSession session, bool allowBackReview = true)
        {
            session.RecoverToNow();
            var r = session.Random;
            double fatigue = session.Fatigue;
            double attention = session.Attention;

            var weights = new Dictionary<SwipeIntent, double>
            {
                [SwipeIntent.Reading] = 0.18,
                [SwipeIntent.Preview] = 0.34,
                [SwipeIntent.Fling] = 0.23,
                [SwipeIntent.MicroAdjust] = 0.07,
                [SwipeIntent.FastScan] = 0.13,
                [SwipeIntent.BackReview] = allowBackReview ? 0.05 : 0
            };

            switch (session.BehaviorState)
            {
                case BrowseBehaviorState.Observe:
                    weights[SwipeIntent.Reading] *= 1.28;
                    weights[SwipeIntent.Preview] *= 1.12;
                    break;
                case BrowseBehaviorState.Read:
                    weights[SwipeIntent.Reading] *= 1.24;
                    weights[SwipeIntent.MicroAdjust] *= 1.35;
                    break;
                case BrowseBehaviorState.FastScan:
                    weights[SwipeIntent.FastScan] *= 1.20;
                    weights[SwipeIntent.Fling] *= 1.16;
                    break;
                case BrowseBehaviorState.BackReview:
                    weights[SwipeIntent.Reading] *= 1.34;
                    weights[SwipeIntent.BackReview] *= 0.42;
                    break;
            }

            switch (session.LastIntent)
            {
                case SwipeIntent.Fling:
                    weights[SwipeIntent.Reading] *= 1.55;
                    weights[SwipeIntent.Preview] *= 1.25;
                    weights[SwipeIntent.Fling] *= 0.62;
                    weights[SwipeIntent.MicroAdjust] *= 1.25;
                    break;
                case SwipeIntent.Reading:
                    weights[SwipeIntent.Reading] *= 1.65;
                    weights[SwipeIntent.MicroAdjust] *= 1.55;
                    weights[SwipeIntent.Fling] *= 0.58;
                    break;
                case SwipeIntent.MicroAdjust:
                    weights[SwipeIntent.Reading] *= 1.75;
                    weights[SwipeIntent.Preview] *= 1.25;
                    weights[SwipeIntent.MicroAdjust] *= 0.42;
                    break;
                case SwipeIntent.FastScan:
                    weights[SwipeIntent.Fling] *= 1.25;
                    weights[SwipeIntent.FastScan] *= 1.18;
                    weights[SwipeIntent.Reading] *= 0.82;
                    break;
                case SwipeIntent.BackReview:
                    weights[SwipeIntent.Reading] *= 1.6;
                    weights[SwipeIntent.Preview] *= 1.35;
                    weights[SwipeIntent.BackReview] *= 0.18;
                    break;
            }

            if (session.ConsecutiveUpCount >= 4 && allowBackReview)
                weights[SwipeIntent.BackReview] *= 1.0 + Math.Min(2.4, (session.ConsecutiveUpCount - 3) * 0.45);

            weights[SwipeIntent.Fling] *= 1.0 - fatigue * 0.52;
            weights[SwipeIntent.FastScan] *= 1.0 - fatigue * 0.48;
            weights[SwipeIntent.Reading] *= 1.0 + fatigue * 0.72 + attention * 0.14;
            weights[SwipeIntent.MicroAdjust] *= 1.0 + attention * 0.48;

            double total = weights.Values.Sum(x => Math.Max(0, x));
            double roll = r.NextDouble() * total;
            double acc = 0;
            foreach (var pair in weights)
            {
                acc += Math.Max(0, pair.Value);
                if (roll <= acc)
                    return pair.Key;
            }
            return SwipeIntent.Preview;
        }

        public TimeSpan DecideObserveDelay(HumanTouchSession session, SwipeIntent completedIntent, double delayFactor = 1.0)
        {
            session.RecoverToNow();
            var r = session.Random;
            double median = completedIntent switch
            {
                SwipeIntent.Reading => 1150,
                SwipeIntent.Fling => 820,
                SwipeIntent.MicroAdjust => 390,
                SwipeIntent.FastScan => 520,
                SwipeIntent.BackReview => 920,
                _ => 680
            };
            median *= session.UserProfile.PauseBias;
            median *= 1.0 + session.Fatigue * 0.55;

            double ms = RandomMath.LogNormal(r, median, 0.42, 180, 5200);
            if (RandomMath.Chance(r, 0.07 + 0.09 * session.Attention))
                ms += RandomMath.LogNormal(r, 1100, 0.55, 500, 5500);

            ms *= Math.Clamp(delayFactor, 0.25, 4.0);
            return TimeSpan.FromMilliseconds(ms);
        }
    }
}
