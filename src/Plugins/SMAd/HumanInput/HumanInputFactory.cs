using PlaywrightHumanInput;
using SMAd.HumanPointerP2;

namespace SMAd.HumanInput
{
    public static class HumanInputFactory
    {
        public static IHumanInputOperator Create(
            int os,
            int seed,
            string? brand,
            string? model,
            Action<string>? log = null)
        {
            if (os is 1 or 2)
            {
                var user = HumanUserProfile.CreateRandom(seed, HumanHandedness.Right);
                var device = TouchDeviceProfiles.ResolveForDesktopCdp(brand, model);
                var session = new HumanTouchSession(user, device);

                return new TouchInputAdapter(new HumanTouchOperator(
                    new HumanTouchOperatorOptions
                    {
                        Session = session,
                        DelayFactor = 1.0,
                        AllowBackReview = true,
                        Log = log
                    }));
            }

            return new HumanPointerOperator(new HumanPointerOperatorOptions
            {
                Session = new HumanPointerSession(PointerUserProfile.Create(seed)),
                Log = log
            });
        }
    }
}
