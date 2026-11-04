namespace BoidsLab;

/// <summary>方向跟随的低速滞回：停稳后需更明显的速度才恢复，避免微小噪声翻转外观。</summary>
internal static class VelocityFacing
{
    public const float StopSpeedSquared = 0.0001f; // 0.01 世界单位/秒。
    private const float ResumeSpeedSquared = 0.0004f; // 0.02 世界单位/秒。

    public static bool ShouldTrack(float speedSquared, bool tracking) =>
        speedSquared > (tracking ? StopSpeedSquared : ResumeSpeedSquared);
}
