using System;
using System.Collections.Generic;
using System.Linq;

namespace PlaywrightHumanInput
{
    public sealed class HumanBehaviorModel
    {
        public SwipeIntent DecideNextIntent(
            HumanTouchSession session,
            bool allowBackReview = true,
            PageContextSnapshot? context = null)
        {
            session.RecoverToNow();

            var r = session.Random;
            double fatigue = session.Fatigue;
            double attention = session.Attention;
            var user = session.UserProfile;

            var weights = new Dictionary<SwipeIntent, double>
            {
                [SwipeIntent.Reading] = 0.18 * user.ReadingBias,
                [SwipeIntent.Preview] = 0.34,
                [SwipeIntent.Fling] = 0.23 * user.ScanBias,
                [SwipeIntent.MicroAdjust] = 0.07 * user.ReadingBias,
                [SwipeIntent.FastScan] = 0.13 * user.ScanBias,
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
                    weights[SwipeIntent.Reading] *= 1.60;
                    weights[SwipeIntent.Preview] *= 1.35;
                    weights[SwipeIntent.BackReview] *= 0.18;
                    break;
            }

            if (session.ConsecutiveUpCount >= 4 && allowBackReview)
            {
                weights[SwipeIntent.BackReview] *=
                    1.0 + Math.Min(2.4, (session.ConsecutiveUpCount - 3) * 0.45);
            }

            weights[SwipeIntent.Fling] *= 1.0 - fatigue * 0.52;
            weights[SwipeIntent.FastScan] *= 1.0 - fatigue * 0.48;
            weights[SwipeIntent.Reading] *= 1.0 + fatigue * 0.72 + attention * 0.14;
            weights[SwipeIntent.MicroAdjust] *= 1.0 + attention * 0.48;

            ApplyPageContext(weights, context, allowBackReview);

            double total = weights.Values.Sum(x => Math.Max(0, x));
            if (total <= 1e-9)
                return SwipeIntent.Preview;

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

        public TimeSpan DecideObserveDelay(
            HumanTouchSession session,
            SwipeIntent completedIntent,
            double delayFactor = 1.0,
            PageContextSnapshot? context = null)
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

            if (context != null)
            {
                // 文本型页面一般会增加停留；媒体/交互型页面只做轻微调整，避免过度模型化。
                median *= 1.0 + context.TextDensityScore * 0.22;
                median *= 1.0 + context.MediaDensityScore * 0.06;

                if (context.IsNearBottom)
                    median *= 1.08;
            }

            double ms = RandomMath.LogNormal(r, median, 0.42, 180, 5200);

            if (RandomMath.Chance(r, 0.07 + 0.09 * session.Attention))
            {
                ms += RandomMath.LogNormal(r, 1100, 0.55, 500, 5500);
            }

            ms *= Math.Clamp(delayFactor, 0.25, 4.0);
            return TimeSpan.FromMilliseconds(ms);
        }

        private static void ApplyPageContext(
            IDictionary<SwipeIntent, double> weights,
            PageContextSnapshot? context,
            bool allowBackReview)
        {
            if (context == null)
                return;

            double text = context.TextDensityScore;
            double media = context.MediaDensityScore;
            double interactive = context.InteractiveDensityScore;

            weights[SwipeIntent.Reading] *= 1.0 + text * 0.72;
            weights[SwipeIntent.MicroAdjust] *= 1.0 + text * 0.45;
            weights[SwipeIntent.Fling] *= 1.0 - text * 0.30;
            weights[SwipeIntent.FastScan] *= 1.0 - text * 0.24;

            weights[SwipeIntent.Preview] *= 1.0 + media * 0.18 + interactive * 0.10;
            weights[SwipeIntent.Fling] *= 1.0 + media * 0.17;
            weights[SwipeIntent.FastScan] *= 1.0 + media * 0.20;
            weights[SwipeIntent.Reading] *= 1.0 - media * 0.18;

            if (context.IsNearTop && allowBackReview)
            {
                weights[SwipeIntent.BackReview] *= 0.25;
            }

            if (context.IsNearBottom)
            {
                weights[SwipeIntent.Fling] *= 0.48;
                weights[SwipeIntent.FastScan] *= 0.58;
                weights[SwipeIntent.Reading] *= 1.18;
                if (allowBackReview)
                    weights[SwipeIntent.BackReview] *= 1.55;
            }
        }
    }
}
