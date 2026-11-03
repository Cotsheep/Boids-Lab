using Godot;

namespace BoidsLab;

/// <summary>避障内部预算及量纲的唯一来源；用户参数仍在 BoidConfig。</summary>
public static class ObstacleTuning
{
    public const uint CollisionLayer = 1u << 7; // 专用第 8 层，查询空间只放确认后的障碍。
    public const float Skin = 0.01f; // 接触后离开表面的数值余量，世界单位。
    public const float MinimumRadius = 0.001f; // 零间距的点近似，避免引擎退化球体。
    public const float Epsilon = 0.000001f;
    public const float ResponseSeconds = 0.25f; // 目标速度差转加速度的响应时间。
    public const float NearMinimum = 2; // 近表面检测的最小距离。
    public const float NearClearanceRatio = 0.5f; // 近表面检测相对间距的比例。
    public const float DepartingDot = 0.1f; // 朝外运动的方向阈值。
    public const float TangentDotTolerance = 0.01f; // 切向候选的方向数值余量。
    public const float StationarySpeed = 1; // 静止接触选向的速度阈值。
    public const int Directions2D = 32; // 从 +X 起，按角度左右交替，最多 32 个候选。
    public const int Directions3D = 64; // 从 -Z 起，按偏角排列的球面方向。
    public const int MotionSegments = 64; // 连续运动/环绕/滑动的总段数上限。
    public const int RecoveryPasses = 8; // 初始重叠的接触投影轮数。
    public const int ContactLimit = 32; // 每次最多接触对，饱和时报告诊断。
    public const int SweepRefinements = 8; // 引擎安全/不安全区间的再扫描预算。
    public const float SweepTolerance = 0.002f; // 扫掠位置误差目标，受浮点精度限制。

    public static readonly Vector2[] Samples2D = Build2D();
    public static readonly Vector3[] Samples3D = Build3D();

    private static Vector2[] Build2D()
    {
        var result = new Vector2[Directions2D];
        for (int i = 0; i < result.Length; i++)
            result[i] = Vector2.FromAngle(((i + 1) / 2) * Mathf.Tau / Directions2D * (i % 2 == 0 ? -1 : 1));
        return result;
    }

    private static Vector3[] Build3D()
    {
        var result = new Vector3[Directions3D];
        for (int i = 0; i < result.Length; i++)
        {
            float z = 1 - 2f * i / (result.Length - 1);
            float r = Mathf.Sqrt(Mathf.Max(0, 1 - z * z));
            float azimuth = i * Mathf.Pi * (3 - Mathf.Sqrt(5));
            result[i] = new Vector3(r * Mathf.Cos(azimuth), r * Mathf.Sin(azimuth), -z);
        }
        return result;
    }
}
