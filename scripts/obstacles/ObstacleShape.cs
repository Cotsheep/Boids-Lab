namespace BoidsLab;

public enum ObstacleShape
{
    Rectangle,
    Circle,
    Box,
    Sphere,
    Custom,
}

/// <summary>面板需要的尺寸；圆/球的各轴均为直径，二维不使用 Depth。</summary>
public readonly record struct ObstacleSelection(ObstacleShape Shape, float Width, float Height, float Depth = 0);

public static class ObstacleSize
{
    public const float Min = 16f;
    public const float Max = 2000f;
}
