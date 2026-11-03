using Godot;

namespace BoidsLab;

/// <summary>应用级外观偏好，与二维 / 三维模拟配置分开保存。</summary>
public sealed class AppearancePreferences
{
    public const string DefaultPath = "user://boids_lab_ui.cfg";
    public bool LightTheme { get; set; }

    public Error Load(string path = DefaultPath)
    {
        using var file = new ConfigFile();
        Error error = file.Load(path);
        LightTheme = false;
        if (error != Error.Ok) return error;
        Variant value = file.GetValue("appearance", "light_theme", false);
        LightTheme = value.VariantType == Variant.Type.Bool && value.AsBool();
        return Error.Ok;
    }

    public Error Save(string path = DefaultPath)
    {
        using var file = new ConfigFile();
        file.SetValue("appearance", "light_theme", LightTheme);
        return file.Save(path);
    }
}
