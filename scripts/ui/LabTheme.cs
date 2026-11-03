using Godot;

namespace BoidsLab;

/// <summary>集中生成原生控件主题；颜色通过语义角色继承，动态控件无需逐个重建。</summary>
public static class LabTheme
{
    public static Theme Create(LabPalette p)
    {
        var theme = (Theme)GD.Load<Theme>("res://scenes/lab_theme.tres").Duplicate(true);
        StyleBoxFlat Box(Color fill, Color border, int horizontal = 12, int vertical = 8, int radius = 6) => new()
        {
            BgColor = fill, BorderColor = border,
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            ContentMarginLeft = horizontal, ContentMarginRight = horizontal,
            ContentMarginTop = vertical, ContentMarginBottom = vertical,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
        };
        var normal = Box(p.Raised, p.Border);
        var hover = Box(p.AccentWash, p.Accent);
        var disabled = Box(p.Panel, p.Border);
        var focus = Box(Colors.Transparent, p.Accent, 0, 0);
        focus.BorderWidthLeft = focus.BorderWidthTop = focus.BorderWidthRight = focus.BorderWidthBottom = 2;
        foreach (string type in new[] { "Button", "OptionButton", "CheckBox" })
        {
            theme.SetStylebox("normal", type, normal);
            theme.SetStylebox("hover", type, hover);
            theme.SetStylebox("pressed", type, hover);
            theme.SetStylebox("hover_pressed", type, hover);
            theme.SetStylebox("disabled", type, disabled);
            theme.SetStylebox("focus", type, focus);
            foreach (string state in new[] { "font_color", "font_hover_color", "font_focus_color", "font_pressed_color", "font_hover_pressed_color" })
                theme.SetColor(state, type, p.Text);
            theme.SetColor("font_disabled_color", type, p.Disabled);
            theme.SetColor("font_outline_color", type, Colors.Transparent);
        }
        theme.SetStylebox("normal", "PrimaryButton", Box(p.Accent, p.Accent));
        theme.SetStylebox("hover", "PrimaryButton", Box(p.Accent.Lerp(p.Text, 0.12f), p.Accent));
        theme.SetStylebox("pressed", "PrimaryButton", Box(p.Accent, p.OnAccent));
        foreach (string state in new[] { "font_color", "font_hover_color", "font_focus_color", "font_pressed_color", "font_hover_pressed_color" })
            theme.SetColor(state, "PrimaryButton", p.OnAccent);
        theme.SetStylebox("normal", "CategoryButton", Box(p.Panel, p.Panel, 8, 9));
        var active = Box(p.AccentWash, p.Accent, 8, 9);
        active.BorderWidthLeft = active.BorderWidthRight = active.BorderWidthTop = 0;
        active.BorderWidthBottom = 2;
        theme.SetStylebox("pressed", "CategoryButton", active);
        theme.SetStylebox("hover_pressed", "CategoryButton", active);
        theme.SetStylebox("hover", "CategoryButton", Box(p.Raised, p.Border, 8, 9));
        theme.SetColor("font_pressed_color", "CategoryButton", p.AccentText);
        theme.SetColor("font_hover_pressed_color", "CategoryButton", p.AccentText);
        theme.SetStylebox("panel", "PanelContainer", Box(p.Panel, p.Border, 18, 16, 10));
        theme.SetTypeVariation("HeaderPanel", "PanelContainer");
        var header = Box(p.Panel, p.Border, 18, 10, 10);
        header.BorderWidthLeft = 3;
        header.BorderColor = p.Accent;
        theme.SetStylebox("panel", "HeaderPanel", header);
        theme.SetStylebox("panel", "PopupMenu", Box(p.Panel, p.Border, 8, 8));
        theme.SetStylebox("hover", "PopupMenu", Box(p.AccentWash, p.AccentWash, 6, 6));
        theme.SetStylebox("panel", "TooltipPanel", Box(p.Text, p.Text, 10, 8));
        theme.SetColor("font_color", "TooltipLabel", p.Panel);
        theme.SetColor("font_color", "PopupMenu", p.Text);
        theme.SetColor("font_hover_color", "PopupMenu", p.AccentText);
        theme.SetColor("font_disabled_color", "PopupMenu", p.Disabled);
        theme.SetColor("font_accelerator_color", "PopupMenu", p.Muted);
        theme.SetStylebox("normal", "LineEdit", Box(p.Input, p.Border, 9, 7));
        theme.SetStylebox("read_only", "LineEdit", Box(p.Panel, p.Border, 9, 7));
        theme.SetStylebox("focus", "LineEdit", focus);
        theme.SetColor("font_color", "LineEdit", p.Text);
        theme.SetColor("font_uneditable_color", "LineEdit", p.Muted);
        theme.SetColor("font_placeholder_color", "LineEdit", p.Muted);
        theme.SetColor("caret_color", "LineEdit", p.Accent);
        theme.SetColor("selection_color", "LineEdit", p.AccentWash);
        theme.SetColor("font_selected_color", "LineEdit", p.Text);
        theme.SetColor("font_color", "Label", p.Text);
        theme.SetColor("default_color", "RichTextLabel", p.Text);
        theme.SetColor("selection_color", "RichTextLabel", p.AccentWash);
        theme.SetStylebox("focus", "RichTextLabel", focus);
        foreach (var (name, color) in new[] { ("MutedLabel", p.Muted), ("AccentLabel", p.AccentText),
            ("WarningLabel", p.Warning), ("SuccessLabel", p.Success), ("LeaderLabel", p.Leader) })
        {
            theme.SetTypeVariation(name, "Label");
            theme.SetColor("font_color", name, color);
        }
        var separator = new StyleBoxLine { Color = p.Border, Thickness = 1 };
        theme.SetStylebox("separator", "HSeparator", separator);
        theme.SetStylebox("separator", "PopupMenu", separator);
        foreach (string bar in new[] { "VScrollBar", "HScrollBar" })
        {
            theme.SetStylebox("scroll", bar, Box(p.Panel, p.Panel, 4, 4, 4));
            theme.SetStylebox("grabber", bar, Box(p.Border, p.Border, 4, 4, 4));
            theme.SetStylebox("grabber_highlight", bar, Box(p.Muted, p.Muted, 4, 4, 4));
            theme.SetStylebox("grabber_pressed", bar, Box(p.Accent, p.Accent, 4, 4, 4));
        }
        Texture2D Icon(string body) => SvgIcon(body);
        string stroke = p.Muted.ToHtml(false), accent = p.Accent.ToHtml(false), on = p.OnAccent.ToHtml(false);
        var uncheckedIcon = Icon($"<rect x='3' y='3' width='14' height='14' rx='3' fill='none' stroke='#{stroke}' stroke-width='1.5'/>");
        var checkedIcon = Icon($"<rect x='2' y='2' width='16' height='16' rx='3' fill='#{accent}'/><path d='M5 10l3 3 7-7' fill='none' stroke='#{on}' stroke-width='2'/>");
        theme.SetIcon("unchecked", "CheckBox", uncheckedIcon);
        theme.SetIcon("checked", "CheckBox", checkedIcon);
        theme.SetIcon("unchecked_disabled", "CheckBox", uncheckedIcon);
        theme.SetIcon("checked_disabled", "CheckBox", checkedIcon);
        theme.SetIcon("arrow", "OptionButton", Icon($"<path d='M5 8l5 5 5-5' fill='none' stroke='#{stroke}' stroke-width='1.8'/>"));
        theme.SetIcon("updown", "SpinBox", Icon($"<path d='M6 7l4-4 4 4M6 13l4 4 4-4' fill='none' stroke='#{stroke}' stroke-width='1.6'/>"));
        return theme;
    }

    private static Texture2D SvgIcon(string body)
    {
        var image = new Image();
        image.LoadSvgFromString($"<svg xmlns='http://www.w3.org/2000/svg' width='20' height='20' viewBox='0 0 20 20'>{body}</svg>");
        return ImageTexture.CreateFromImage(image);
    }
}
