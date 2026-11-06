using HumanTouchP3.Input;

namespace HumanTouchP3;

/// <summary>Initial calibration from one 411×721 CSS viewport and one operator, not population defaults.</summary>
public static class PostureProfiles
{
    public static HumanUserProfile Create(HandPosture posture) => posture switch
    {
        HandPosture.RightHandOneHanded => new HumanUserProfile {
            Handedness = HumanHandedness.Right, VerticalCenterXRatio = .62, StartPositionStdRatio = .10,
            VerticalUpStartXRatio = .58, VerticalDownStartXRatio = .66, VerticalUpStartYRatio = .89, VerticalDownStartYRatio = .61,
            VerticalUpCrossRatio = .11, VerticalDownCrossRatio = -.12, FlingDistanceScale = .50,
            CurveBias = 1.1, PreferredTouchRadiusPx = 4.2 },
        HandPosture.LeftHandOneHanded => new HumanUserProfile {
            Handedness = HumanHandedness.Left, VerticalCenterXRatio = .25, StartPositionStdRatio = .075,
            VerticalUpStartXRatio = .18, VerticalDownStartXRatio = .31, VerticalUpStartYRatio = .77, VerticalDownStartYRatio = .55,
            VerticalUpCrossRatio = -.065, VerticalDownCrossRatio = .07, FlingDistanceScale = .50,
            CurveBias = 1.1, PreferredTouchRadiusPx = 4.2 },
        HandPosture.LeftHandHoldRightIndex => new HumanUserProfile {
            Handedness = HumanHandedness.Right, VerticalCenterXRatio = .50, StartPositionStdRatio = .10,
            VerticalUpStartXRatio = .52, VerticalDownStartXRatio = .48, VerticalUpStartYRatio = .78, VerticalDownStartYRatio = .45,
            VerticalUpCrossRatio = .03, VerticalDownCrossRatio = 0, FlingDistanceScale = .80,
            CurveBias = .65, PreferredTouchRadiusPx = 3.2 },
        _ => throw new ArgumentOutOfRangeException(nameof(posture))
    };
    /// <summary>Combine posture calibration with the caller's stable per-device behavior profile.</summary>
    public static HumanUserProfile Apply(HandPosture posture, HumanUserProfile original)
    {
        ArgumentNullException.ThrowIfNull(original);
        var calibration = Create(posture);
        return new HumanUserProfile
        {
            ProfileId = original.ProfileId, Seed = original.Seed, Handedness = calibration.Handedness,
            VerticalCenterXRatio = calibration.VerticalCenterXRatio, HorizontalCenterYRatio = original.HorizontalCenterYRatio,
            StartPositionStdRatio = calibration.StartPositionStdRatio,
            VerticalUpStartXRatio = calibration.VerticalUpStartXRatio, VerticalDownStartXRatio = calibration.VerticalDownStartXRatio,
            VerticalUpStartYRatio = calibration.VerticalUpStartYRatio, VerticalDownStartYRatio = calibration.VerticalDownStartYRatio,
            VerticalUpCrossRatio = calibration.VerticalUpCrossRatio, VerticalDownCrossRatio = calibration.VerticalDownCrossRatio,
            FlingDistanceScale = calibration.FlingDistanceScale,
            SpeedBias = original.SpeedBias, DistanceBias = original.DistanceBias,
            CurveBias = original.CurveBias * calibration.CurveBias, CurveAsymmetryBias = original.CurveAsymmetryBias,
            DriftBias = original.DriftBias, TremorBias = original.TremorBias, SubmovementBias = original.SubmovementBias,
            MotionPeakBias = original.MotionPeakBias, ForceBias = original.ForceBias, TouchAreaBias = original.TouchAreaBias,
            PauseBias = original.PauseBias, HesitationBias = original.HesitationBias, PullBackBias = original.PullBackBias,
            FatigueSensitivity = original.FatigueSensitivity, RecoveryBias = original.RecoveryBias,
            ReactionBias = original.ReactionBias, ReadingBias = original.ReadingBias, ScanBias = original.ScanBias,
            PreferredTouchRadiusPx = calibration.PreferredTouchRadiusPx,
            PreferredRotationDeg = posture == HandPosture.LeftHandOneHanded ? 135 : 42
        };
    }

}
