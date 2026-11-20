namespace ControlUI.Support.Services;

public sealed record MotionAxisSettings(
    double MoveUnit, int Pulses, double MaxVelocity, uint AccelUnitCode);

public sealed record MotionBoardReadback(MotionAxisSettings Units, uint ProfileMode, double MinVelocity,
    MotionSoftLimits Limits);

public sealed record MotionSoftLimits(bool Enabled, uint PositionSource, double Negative, double Positive);
