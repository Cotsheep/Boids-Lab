namespace BoidsLab;

public enum ObstacleShape
{
    Rectangle,
    Circle,
}

/// <summary>面板需要的选中对象信息；圆形的宽高均为直径。</summary>
public readonly record struct ObstacleSelection(ObstacleShape Shape, float Width, float Height);
