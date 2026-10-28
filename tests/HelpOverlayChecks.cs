using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace BoidsLab.Tests;

/// <summary>真实主场景中的弹层输入隔离；不保存配置，不创建图形窗口。</summary>
public partial class HelpOverlayChecks : Node
{
    private int _checks;

    public override async void _Ready()
    {
        if (DisplayServer.GetName() != "headless")
        {
            GD.PushError("Run help_overlay_checks.tscn with --headless.");
            GetTree().Quit(2);
            return;
        }
        try
        {
            MainController game = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<MainController>();
            AddChild(game);
            await Frame();
            GetViewport().NotifyMouseEntered();
            var world = game.GetNode<BoidManager>("World/BoidsWorld2D");
            var camera = world.GetNode<SimulationCamera2D>("Camera2D");
            var hud = game.GetNode<Control>("%UIRoot");
            var help = game.GetNode<HelpOverlay>("%HelpOverlay");
            var open = game.GetNode<Button>("%HelpButton");
            var close = help.GetNode<Button>("Card/Column/Header/Close");
            var card = help.GetNode<Control>("Card");
            var instructions = help.GetNode<RichTextLabel>("Card/Column/Instructions");
            var panel = game.GetNode<ControlPanel>("%ControlPanel");
            var reset = (Button)panel.FindChild("Reset", true, false);
            var input = ((SpinBox)panel.FindChild("BoidCount", true, false)).GetLineEdit();
            int configurationChanges = 0;
            int resetPresses = 0;
            panel.ConfigurationChanged += () => configurationChanges++;
            reset.Pressed += () => resetPresses++;
            Check(!help.Visible && !world.IsRunning, "Help starts hidden without changing initial pause");

            Click(open.GetGlobalRect().GetCenter());
            Check(help.Visible && !world.IsRunning, "Header button opens help without resuming");
            await Frame();
            Click(card.GetGlobalRect().Position + new Vector2(8, 8));
            Check(help.Visible, "Click inside card does not dismiss");
            Tap(Key.Escape);
            Check(!help.Visible && !world.IsRunning, "Escape closes without changing pause");
            input.GrabFocus();
            Tap(Key.F2);
            KeyEvent(Key.F2, true, true);
            Check(help.Visible && help.IsAncestorOf(GetViewport().GuiGetFocusOwner()), "F2 works from input focus, ignores repeat and moves focus into help");
            string inputText = input.Text;
            for (int i = 0; i < 12; i++)
            {
                Tap(Key.Tab);
                var focus = GetViewport().GuiGetFocusOwner();
                Check(focus is not null && help.IsAncestorOf(focus), "Tab stays in modal controls");
            }
            Tap(Key.Space);
            KeyEvent(Key.Space, true, true);
            Check(world.IsRunning && help.Visible, "Space resumes in help, echo does not pause or close");
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Check(world.NeighborSearch.HasResults && !GetTree().Paused, "Simulation continues computing while help is visible");
            Tap(Key.Space);
            Check(!world.IsRunning && help.Visible, "Space pauses in help");
            Tap(Key.F2);
            Check(!help.Visible && GetViewport().GuiGetFocusOwner() == input && input.Text == inputText,
                "F2 closes and restores original focus without editing input");

            Tap(Key.F1);
            Tap(Key.F2);
            Check(!hud.Visible && help.Visible, "F2 opens help independently of hidden HUD");
            Tap(Key.F1);
            Check(hud.Visible && help.Visible, "F1 does not dismiss help");
            Click(close.GetGlobalRect().GetCenter());
            Check(!help.Visible, "Close button dismisses help");

            // 用屏幕右侧的独立个体验证点击遮罩不会同时选择/取消选择。
            foreach (Boid2D boid in world.Boids.ToArray()) world.RemoveBoid(boid);
            Vector2 outside = new(GetViewport().GetVisibleRect().Size.X - 34, 320);
            Boid2D probe = world.SpawnBoid(world.GetGlobalTransformWithCanvas().AffineInverse() * outside, Vector2.Zero);
            await Frame();
            Click(outside);
            Check(world.SelectedBoid == probe, "Probe is selectable before opening help");
            Tap(Key.F2);
            Pointer(outside);
            Button(outside, true);
            Check(help.Visible && world.SelectedBoid == probe, "Outside press is intercepted until release");
            Button(outside, false);
            Check(!help.Visible && world.SelectedBoid == probe, "Outside click closes without deselecting underlying Boid");
            Click(outside);
            Check(world.SelectedBoid is null, "Next independent click reaches world normally");

            // 放大/移动后 Home 才有可观察的效果，避免错误地通过阻断检查。
            Wheel(outside, MouseButton.WheelUp);
            Vector2 cameraBefore = camera.Position;
            Vector2 zoomBefore = camera.Zoom;
            Tap(Key.F2);
            Tap(Key.Home);
            Wheel(outside, MouseButton.WheelUp);
            Wheel(instructions.GetGlobalRect().GetCenter(), MouseButton.WheelDown);
            Pointer(card.GetGlobalRect().GetCenter());
            Button(card.GetGlobalRect().GetCenter(), true);
            Pointer(outside, new Vector2(40, 20), true);
            Button(outside, false);
            Check(help.Visible && camera.Position == cameraBefore && camera.Zoom == zoomBefore,
                "Modal blocks Home, outside/inside wheel and dragging; dragging out of card does not dismiss");
            Check(instructions.GetVScrollBar().Value > 0, "Help content scrolls internally");
            for (int i = 0; i < 12; i++)
            {
                Tap(Key.Tab);
                Check(help.IsAncestorOf(GetViewport().GuiGetFocusOwner()), "Focus stays isolated after pointer use");
            }
            Tap(Key.F2);
            Tap(Key.Home);
            Check(camera.ZoomFactor == 1, "Camera controls resume after closing");

            // 打开说明会取消已按住的世界手势；关闭后继续按住也不能粘住相机。
            Pointer(outside);
            Button(outside, true);
            Pointer(outside + new Vector2(-20, 0), new Vector2(-20, 0), true);
            cameraBefore = camera.Position;
            Tap(Key.F2);
            Pointer(outside + new Vector2(-40, 0), new Vector2(-20, 0), true);
            Tap(Key.F2);
            Pointer(outside + new Vector2(-60, 0), new Vector2(-20, 0), true);
            Button(outside + new Vector2(-60, 0), false);
            Check(camera.Position == cameraBefore && world.SelectedBoid is null, "Opening cancels active drag and pending selection across close");

            Pointer(reset.GetGlobalRect().GetCenter());
            Button(reset.GetGlobalRect().GetCenter(), true);
            Tap(Key.F2);
            Button(reset.GetGlobalRect().GetCenter(), false);
            Check(resetPresses == 0 && help.Visible, "Opening while a HUD button is held cancels its pending activation");
            Tap(Key.Escape);
            Tap(Key.F2);
            Click(reset.GetGlobalRect().GetCenter());
            Check(resetPresses == 0 && world.Boids.Count == 1 && configurationChanges == 0,
                "Clicking covered HUD cannot reset or change settings");
            Tap(Key.Escape);
            world.SetRunning(true);
            Tap(Key.F2);
            Tap(Key.Escape);
            Check(world.IsRunning, "Opening and closing preserves an already-running simulation");
            world.SetRunning(false);

            // 只改变无窗口视口尺寸，检查小窗口内容可滚动，大窗口弹层仍居中。
            Tap(Key.F2);
            foreach (Vector2I size in new[] { new Vector2I(960, 640), new Vector2I(1920, 1080) })
            {
                GetWindow().Size = size;
                await Frame();
                Rect2 viewport = GetViewport().GetVisibleRect();
                Rect2 rect = card.GetGlobalRect();
                Check(viewport.Encloses(rect) && rect.GetCenter().DistanceTo(viewport.GetCenter()) < 1,
                    "Help fits and centers at desktop viewport sizes");
                Check(rect.Size.X <= 760 && rect.Size.Y <= 680, "Large viewport keeps readable help width/height");
            }
            GD.Print($"PASS: {_checks} help overlay interaction/layout assertions (headless).");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private async Task Frame()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Tap(Key key) { KeyEvent(key, true); KeyEvent(key, false); }
    private void KeyEvent(Key key, bool pressed, bool echo = false) =>
        GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed, Echo = echo }, true);
    private void Pointer(Vector2 position, Vector2 relative = default, bool held = false) =>
        GetViewport().PushInput(new InputEventMouseMotion { Position = position, GlobalPosition = position,
            Relative = relative, ButtonMask = held ? MouseButtonMask.Left : 0 }, true);
    private void Button(Vector2 position, bool pressed) =>
        GetViewport().PushInput(new InputEventMouseButton { Position = position, GlobalPosition = position,
            ButtonIndex = MouseButton.Left, Pressed = pressed, ButtonMask = pressed ? MouseButtonMask.Left : 0 }, true);
    private void Click(Vector2 position) { Pointer(position); Button(position, true); Button(position, false); }
    private void Wheel(Vector2 position, MouseButton direction)
    {
        Pointer(position);
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventMouseButton { Position = position, GlobalPosition = position,
                ButtonIndex = direction, Pressed = pressed, Factor = 1 }, true);
    }
    private void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
