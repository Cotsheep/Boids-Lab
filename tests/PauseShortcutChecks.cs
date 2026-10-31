using System;
using System.Linq;
using Godot;

namespace BoidsLab.Tests;

/// <summary>在真实主场景中检查空格的 GUI 优先级；不修改参数或保存用户配置。</summary>
public partial class PauseShortcutChecks : Node
{
    private int _checks;

    public override async void _Ready()
    {
        if (DisplayServer.GetName() != "headless")
        {
            GD.PushError("Run pause_shortcut_checks.tscn with --headless.");
            GetTree().Quit(2);
            return;
        }
        try
        {
            MainController game = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<MainController>();
            AddChild(game);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var world = game.GetNode<BoidManager>("World/BoidsWorld2D");
            var hud = game.GetNode<Control>("UI/UIRoot");
            var panel = game.GetNode<ControlPanel>("%ControlPanel");
            var start = (Button)panel.FindChild("StartPause", true, false);
            var reset = (Button)panel.FindChild("Reset", true, false);
            var checkbox = (CheckBox)panel.FindChild("ShowGrid", true, false);
            var input = ((SpinBox)panel.FindChild("BoidCount", true, false)).GetLineEdit();
            var mode = (OptionButton)panel.FindChild("NeighborSearchMode", true, false);
            int pressed = 0;
            int configurationChanges = 0;
            int popupRequests = 0;
            start.Pressed += () => pressed++;
            reset.Pressed += () => pressed++;
            checkbox.Pressed += () => pressed++;
            panel.ConfigurationChanged += () => configurationChanges++;
            mode.GetPopup().AboutToPopup += () => popupRequests++;
            Check(InputMap.HasAction("toggle_pause"), "Project defines pause action");

            // 同一批事件内同步检查，避免真实物理帧推进影响这组纯输入断言。
            foreach (Control? focus in new Control?[] { null, start, reset, checkbox, input, mode })
            {
                GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
                if (focus is not null) panel.RevealControl(focus);
                focus?.GrabFocus();
                world.SetRunning(false);
                bool previousCheck = checkbox.ButtonPressed;
                string previousText = input.Text;
                int previousMode = mode.Selected;
                Boid2D[] instances = world.Boids.ToArray();
                KeyEvent(Key.Space, true);
                Check(world.IsRunning && start.Text == "暂停", "Space starts/resumes with every tested GUI focus");
                KeyEvent(Key.Space, true, true);
                KeyEvent(Key.Space, true, true);
                Check(world.IsRunning, "Holding space does not toggle repeatedly");
                KeyEvent(Key.Space, false);
                Check(world.IsRunning, "Release does not toggle or activate GUI");
                KeyEvent(Key.Space, true);
                Check(!world.IsRunning && start.Text == "开始", "Next space press pauses");
                KeyEvent(Key.Space, false);
                Check(checkbox.ButtonPressed == previousCheck && input.Text == previousText && mode.Selected == previousMode,
                    "Space never changes checkbox, input text or selected search mode");
                Check(world.Boids.SequenceEqual(instances), "Space with Reset focus does not recreate Boids");
                Check(pressed == 0 && configurationChanges == 0 && popupRequests == 0,
                    "Space is consumed before GUI activation and does not save settings");
            }

            panel.SelectCategory("Basics");
            input.GrabFocus();
            KeyEvent(Key.F1, true);
            KeyEvent(Key.F1, false);
            Check(!hud.Visible, "F1 still hides HUD");
            KeyEvent(Key.Space, true);
            KeyEvent(Key.Space, false);
            Check(world.IsRunning && !hud.Visible, "Space works with hidden HUD");
            KeyEvent(Key.Space, true);
            KeyEvent(Key.Space, false);
            Check(!world.IsRunning && !GetTree().Paused, "Only simulation is paused, leaving UI/camera input active");
            KeyEvent(Key.F1, true);
            KeyEvent(Key.F1, false);
            Check(hud.Visible, "F1 still restores HUD");
            // Enter 保留原生按钮操作，确认没有整体禁用 GUI 键盘输入。
            start.GrabFocus();
            KeyEvent(Key.Enter, true);
            KeyEvent(Key.Enter, false);
            Check(world.IsRunning && pressed == 1, "Enter still activates focused button normally");
            world.SetRunning(false);
            GD.Print($"PASS: {_checks} global pause shortcut/focus assertions (headless).");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private void KeyEvent(Key key, bool pressed, bool echo = false) =>
        GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key,
            Pressed = pressed, Echo = echo, Unicode = key == Key.Space ? 32u : 0u }, true);

    private void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
