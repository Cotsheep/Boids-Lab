using System;
using Godot;

namespace BoidsLab;

/// <summary>窗口内的操作说明弹层；与 HUD 并列，隐藏 HUD 后仍可独立打开。</summary>
public partial class HelpOverlay : Control
{
    public event Action<bool>? OpenChanged;

    private PanelContainer _card = null!;
    private Button _closeButton = null!;
    private bool _outsidePress;

    public void SetInstructions(string text)
    {
        var instructions = GetNode<RichTextLabel>("Card/Column/Instructions");
        instructions.Text = text;
        instructions.ScrollToLine(0);
    }

    public override void _Ready()
    {
        _card = GetNode<PanelContainer>("Card");
        _closeButton = GetNode<Button>("Card/Column/Header/Close");
        _closeButton.Pressed += Close;
        Resized += FitCard;
        GetWindow().FocusExited += CancelOutsidePress;
        FitCard();
    }

    public void Toggle()
    {
        if (Visible) Close();
        else Open();
    }

    public void Open()
    {
        if (Visible) return;
        _outsidePress = false;
        Show();
        OpenChanged?.Invoke(true);
        _closeButton.GrabFocus();
    }

    public void Close()
    {
        if (!Visible) return;
        _outsidePress = false;
        GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        Hide();
        OpenChanged?.Invoke(false);
    }

    public override void _GuiInput(InputEvent @event)
    {
        // 遮罩吃掉整个点击手势，松开后才关闭，避免这次点击落到背景上。
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse) return;
        bool outside = !_card.GetRect().HasPoint(mouse.Position);
        if (mouse.Pressed) _outsidePress = outside;
        else
        {
            bool shouldClose = _outsidePress && outside;
            _outsidePress = false;
            AcceptEvent();
            if (shouldClose) Close();
        }
    }

    private void CancelOutsidePress() => _outsidePress = false;

    private void FitCard()
    {
        _card.Size = new Vector2(Mathf.Min(760, Mathf.Max(1, Size.X - 48)),
            Mathf.Min(680, Mathf.Max(1, Size.Y - 48)));
        _card.Position = (Size - _card.Size) * 0.5f;
    }

    public override void _ExitTree()
    {
        if (_closeButton is not null) _closeButton.Pressed -= Close;
        Resized -= FitCard;
        GetWindow().FocusExited -= CancelOutsidePress;
    }
}
