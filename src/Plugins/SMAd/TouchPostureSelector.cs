using HumanTouchP3;

namespace SMAd;

public static class TouchPostureSelector
{
    public static HandPosture Resolve(string? value, Random? random = null)
    {
        if (Enum.TryParse<HandPosture>(value, true, out var parsed) && Enum.IsDefined(parsed)) return parsed;
        return FromRoll((random ?? Random.Shared).Next(100));
    }

    public static HandPosture FromRoll(int roll) => roll switch
    {
        < 0 or >= 100 => throw new ArgumentOutOfRangeException(nameof(roll)),
        < 69 => HandPosture.RightHandOneHanded,
        < 82 => HandPosture.LeftHandOneHanded,
        _ => HandPosture.LeftHandHoldRightIndex
    };
}
