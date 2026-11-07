using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace BoidsLab.Tests;

/// <summary>两种主题的对比度、即时切换、偏好及模拟状态隔离。</summary>
public partial class ThemeChecks : Node
{
    private int _checks;
    private MainController _app = null!;
    private ControlPanel Panel => _app.GetNode<ControlPanel>("%ControlPanel");
    private ISimulationWorld World => (ISimulationWorld)_app.GetNode<Node>("%World").GetChild(0);
    private BoidConfig Config => World is BoidManager two ? two.Config : ((BoidManager3D)World).Config;
    private const string UiPath = "res://.godot/validation/theme-preferences.cfg";
    private T Find<T>(string name) where T : Node => (T)Panel.FindChild(name, true, false);

    public override async void _Ready()
    {
        if (DisplayServer.GetName() != "headless") { GetTree().Quit(2); return; }
        try
        {
            CheckPreferences();
            CheckResourceSerialization();
            foreach (var palette in new[] { LabPalette.Dark, LabPalette.Light }) CheckContrast(palette);
            _app = MakeApp();
            AddChild(_app);
            await Frame();
            GetViewport().NotifyMouseEntered();
            for (int mode = 0; mode < 2; mode++)
            {
                if (mode != _app.CurrentWorldIndex) { _app.RequestWorldSwitch(mode); await Frame(); }
                await CheckSwitching();
                CheckObstaclePreview();
                CheckLivePaletteEditing();
                CheckPopulationAndIdentity();
            }
            _app.SetLightTheme(true);
            _app.Free();
            _app = MakeApp();
            AddChild(_app);
            await Frame();
            Check(_app.IsLightTheme && _app.GetNode<Control>("%UIRoot").Theme.GetColor("font_color", "Label") == LabPalette.Light.Text,
                "Restart restores application theme independently of simulation files");
            _app.RequestWorldSwitch(1);
            await Frame();
            Check(_app.IsLightTheme && ((BoidManager3D)World).GetNode<SimulationView3D>("SimulationView").Palette == LabPalette.Light,
                "Newly instantiated worlds inherit the selected theme");
            _app.Free();
            GD.Print($"PASS: {_checks} theme/contrast/persistence/rendering/state isolation assertions (headless).");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private MainController MakeApp()
    {
        var app = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<MainController>();
        app.AppearanceConfigurationPath = UiPath;
        app.World2D = (SimulationWorldDefinition)app.World2D.Duplicate();
        app.World3D = (SimulationWorldDefinition)app.World3D.Duplicate();
        app.World2D.ConfigurationPath = "res://.godot/validation/theme-world-2d.cfg";
        app.World3D.ConfigurationPath = "res://.godot/validation/theme-world-3d.cfg";
        foreach (var definition in new[] { app.World2D, app.World3D })
            new BoidConfig { BoidCount = 4 }.Save(definition.ConfigurationPath);
        return app;
    }

    private void CheckPreferences()
    {
        using var old = new ConfigFile();
        old.Save(UiPath);
        var preferences = new AppearancePreferences { LightTheme = true };
        Check(preferences.Load(UiPath) == Error.Ok && !preferences.LightTheme, "Older preferences default to dark");
        old.SetValue("appearance", "light_theme", "invalid");
        old.Save(UiPath);
        Check(preferences.Load(UiPath) == Error.Ok && !preferences.LightTheme, "Invalid preference types default to dark");
        foreach (bool light in new[] { true, false })
        {
            preferences.LightTheme = light;
            Check(preferences.Save(UiPath) == Error.Ok, "Theme preference saves");
            var loaded = new AppearancePreferences();
            Check(loaded.Load(UiPath) == Error.Ok && loaded.LightTheme == light, "Theme preference round-trips");
        }
    }

    private void CheckContrast(LabPalette p)
    {
        foreach (Color text in new[] { p.Text, p.Muted, p.AccentText, p.Warning, p.Success, p.LeaderText })
            Check(Contrast(text, p.Panel) >= 4.5, "Semantic text contrasts with its panel");
        Check(Contrast(p.OnAccent, p.Accent) >= 4.5, "Primary action text has sufficient contrast");
        Check(Contrast(p.AccentText, p.AccentWash) >= 4.5, "Active category remains readable");
        Check(Contrast(p.Text, p.Input) >= 4.5 && Contrast(p.Muted, p.Canvas) >= 4.5, "Editors and canvas labels remain readable");
        foreach (Color graphic in new[] { p.Boid, p.Leader, p.Selection, p.SelectedLeader, p.Perception, p.Separation, p.Velocity })
            Check(Contrast(graphic, p.Canvas) >= 3, "World identities and debug lines contrast with the canvas");
    }

    private void CheckResourceSerialization()
    {
        var copy = (LabPalette)LabPalette.Light.Duplicate();
        copy.Set("Boid", new Color("fa8020"));
        copy.Set("ObstaclePreview", new Color("f78e24"));
        const string path = "res://.godot/validation/editable-palette.tres";
        Check(ResourceSaver.Save(copy, path) == Error.Ok, "Palette saves as a native Godot resource");
        var loaded = ResourceLoader.Load<LabPalette>(path, cacheMode: ResourceLoader.CacheMode.Ignore);
        Check(loaded is not null && loaded.IsLight && loaded.Boid == copy.Boid && loaded.ObstaclePreview == copy.ObstaclePreview
            && loaded.LeaderText == copy.LeaderText,
            "Inspector color properties round-trip through a .tres resource");
        Check(LabPalette.Light.Boid != copy.Boid, "Duplicated palettes can be customized without changing the default resource");
    }

    private void CheckLivePaletteEditing()
    {
        foreach (bool light in new[] { false, true })
        {
            _app.SetLightTheme(light);
            LabPalette palette = light ? _app.LightPalette : _app.DarkPalette;
            var saved = (palette.Canvas, palette.Panel, palette.Boid, palette.ObstaclePreview);
            if (World is BoidManager3D three) three.SetLeader(three.Boids[0]);
            World.BeginObstaclePlacement(World is BoidManager ? ObstacleShape.Rectangle : ObstacleShape.Box);
            World.SetRunning(true);
            object[] before = Snapshot();
            object camera = CameraState();
            try
            {
                // Use the same exported-property setters as the Remote Inspector.
                palette.Set("Canvas", new Color("fffcf5"));
                palette.Set("Panel", new Color("fff0d0"));
                palette.Set("Boid", new Color("fa8020"));
                palette.Set("ObstaclePreview", new Color("f78e24"));
                Check(((StyleBoxFlat)_app.GetNode<Control>("%UIRoot").Theme.GetStylebox("panel", "PanelContainer")).BgColor == palette.Panel,
                    "Live resource edits refresh native control colors");
                if (World is BoidManager two)
                {
                    Check(two.Boids.All(b => b.GetNode<Polygon2D>("Body").Color == palette.Boid)
                        && two.GetNode<SimulationView>("SimulationView").Palette.Canvas == palette.Canvas,
                        "Live resource edits update dark and light 2D bodies and canvas");
                    Check(two.GetNode<ObstacleEditor2D>("ObstacleEditor").PlacementShape == ObstacleShape.Rectangle,
                        "Live palette edits preserve 2D placement");
                }
                else
                {
                    var world = (BoidManager3D)World;
                    var ordinary = world.Boids[1].GetNode<MeshInstance3D>("Body");
                    var faded = (BaseMaterial3D)ordinary.MaterialOverride;
                    Check(faded.AlbedoColor == palette.Boid && faded.DistanceFadeMode == BaseMaterial3D.DistanceFadeModeEnum.PixelAlpha
                        && world.GetNode<WorldEnvironment>("SimulationView/WorldEnvironment").Environment.BackgroundColor == palette.Canvas,
                        "Live resource edits refresh dark and light 3D fading and canvas");
                    Check(((BaseMaterial3D)world.GetNode<MeshInstance3D>("Obstacles/PlacementPreview/Body").MaterialOverride).AlbedoColor == palette.ObstaclePreview,
                        "Live resource edits recolor the active placement preview");
                }
                Check(World.IsRunning && before.SequenceEqual(Snapshot()) && Equals(camera, CameraState()),
                    "Inspector recoloring does not advance simulation or change the camera");
            }
            finally
            {
                (palette.Canvas, palette.Panel, palette.Boid, palette.ObstaclePreview) = saved;
                World.SetRunning(false);
                World.CancelObstaclePlacement();
                if (World is BoidManager3D threeD) threeD.SetLeader(null);
            }
        }
    }

    private async Task CheckSwitching()
    {
        Panel.SelectCategory("Flocking");
        Find<SpinBox>("SeparationRadius").Value = 90;
        Find<SpinBox>("PerceptionRadius").Value = 80;
        var page = Find<ScrollContainer>("FlockingPage");
        await Frame();
        page.EnsureControlVisible(Find<Control>("CohesionWeight"));
        await Frame();
        int scroll = page.ScrollVertical;
        var help = _app.GetNode<HelpOverlay>("%HelpOverlay");
        foreach (bool running in new[] { false, true })
        foreach (bool light in new[] { true, false, true })
        {
            World.SetRunning(running);
            object[] before = Snapshot();
            SimulationStats stats = World.Stats;
            object camera = CameraState();
            long configBefore = Config.RandomSeed;
            _app.SetLightTheme(light);
            var p = LabPalette.For(light);
            Check(Snapshot().SequenceEqual(before) && Equals(camera, CameraState()) && World.Stats.Equals(stats)
                && World.IsRunning == running && Config.RandomSeed == configBefore, "Theme switch preserves motion, camera, statistics and running state");
            Check(Panel.CurrentCategory == "Flocking" && page.ScrollVertical == scroll, "Theme switch preserves category and scroll");
            Check(Find<Label>("SeparationRadiusWarning").GetThemeColor("font_color") == p.Warning
                && Find<Label>("LeaderStatus").GetThemeColor("font_color") == p.LeaderText,
                "Warning and leader labels keep readable text colors independently of world highlights");
            Check(_app.GetNode<Control>("%UIRoot").Theme.GetStylebox("normal", "LineEdit") is StyleBoxFlat input && input.BgColor == p.Input,
                "Numeric inputs use the selected palette");
            help.Open();
            _app.SetLightTheme(!light);
            _app.SetLightTheme(light);
            Check(help.Visible && help.Theme == _app.GetNode<Control>("%UIRoot").Theme
                && help.GetNode<RichTextLabel>("Card/Column/Instructions").Text.Contains(p.AccentText.ToHtml(false)),
                "Open help recolors its native controls and inline headings");
            help.Close();
            World.SetRunning(false);
            await Frame();
        }
        foreach (var size in new[] { new Vector2I(960, 640), new Vector2I(1360, 860), new Vector2I(1920, 1080) })
        {
            GetWindow().Size = size;
            await Frame();
            var toggle = _app.GetNode<Button>("%ThemeToggle");
            bool previous = _app.IsLightTheme;
            Click(toggle.GetGlobalRect().GetCenter());
            Check(_app.IsLightTheme != previous, "Native theme button switches the palette");
            Check(GetViewport().GetVisibleRect().Encloses(_app.GetNode<Control>("%HideUiButton").GetGlobalRect()), "Header fits with the theme switch at every supported size");
        }
    }

    private void CheckObstaclePreview()
    {
        if (World is not BoidManager3D world) return;
        var editor = world.GetNode<ObstacleEditor3D>("ObstacleEditor");
        foreach (var shape in new[] { ObstacleShape.Box, ObstacleShape.Sphere })
        {
            foreach (bool initialLight in new[] { false, true })
            {
                _app.SetLightTheme(initialLight);
                world.BeginObstaclePlacement(shape);
                var preview = world.GetNode<Obstacle3D>("Obstacles/PlacementPreview");
                foreach (bool light in new[] { initialLight, !initialLight })
                {
                    _app.SetLightTheme(light);
                    var palette = LabPalette.For(light);
                    var fill = (BaseMaterial3D)preview.GetNode<MeshInstance3D>("Body").MaterialOverride;
                    var outline = preview.GetNode<MeshInstance3D>("SelectionOutline");
                    Check(fill.AlbedoColor == palette.ObstaclePreview
                        && ((BaseMaterial3D)outline.Mesh.SurfaceGetMaterial(0)).AlbedoColor == palette.ObstaclePreviewOutline,
                        "Placement preview uses orange from the first frame and recolors during placement");
                    Check(editor.PlacementShape == shape && editor.Obstacles.Count == 0 && outline.Visible,
                        "Recoloring keeps the preview gesture and never creates a collidable obstacle");
                }
                world.CancelObstaclePlacement();
            }
        }
        var placed = editor.AddObstacle(ObstacleShape.Box, new Vector3(10000, 0, 0));
        editor.ClearSelection();
        foreach (bool light in new[] { true, false })
        {
            _app.SetLightTheme(light);
            var fill = (BaseMaterial3D)placed.GetNode<MeshInstance3D>("Body").MaterialOverride;
            Check(fill.AlbedoColor == LabPalette.For(light).Obstacle
                && (!light || fill.ShadingMode == BaseMaterial3D.ShadingModeEnum.Unshaded),
                "Placed light-theme obstacles retain clean apricot colors without lighting tint");
            Check(placed.GetNode<MeshInstance3D>("SelectionOutline").Visible == light && placed.Position == new Vector3(10000, 0, 0),
                "Light-theme obstacle outlines define volume without changing geometry");
        }
    }

    private void CheckPopulationAndIdentity()
    {
        _app.SetLightTheme(true);
        if (World is BoidManager two)
        {
            var source = two.Boids[0];
            two.SetLeader(source);
            var ordinary = two.SpawnBoid(two.ViewBounds.GetCenter(), Vector2.Zero);
            var obstacle = two.GetNode<ObstacleEditor2D>("ObstacleEditor").AddObstacle(ObstacleShape.Circle, two.ViewBounds.GetCenter());
            object[] before = Snapshot();
            _app.SetLightTheme(false);
            _app.SetLightTheme(true);
            Check(two.Leader == source && Snapshot().SequenceEqual(before) && GodotObject.IsInstanceValid(obstacle), "2D identities, obstacles and positions survive theme switches");
            Check(source.GetNode<Polygon2D>("Body").Color == (source.IsSelected ? LabPalette.Light.SelectedLeader : LabPalette.Light.Leader)
                && ordinary.GetNode<Polygon2D>("Body").Color == LabPalette.Light.Boid, "New and existing 2D boids use readable light colors");
            two.Reset();
            Check(two.Boids.All(b => b.GetNode<Polygon2D>("Body").Color == LabPalette.Light.Boid), "Reset retains the chosen 2D palette");
        }
        else
        {
            var three = (BoidManager3D)World;
            var leader = three.Boids[0];
            three.SetLeader(leader);
            var ordinary = three.Boids[1];
            var source = (BaseMaterial3D)ordinary.GetNode<MeshInstance3D>("Body").Mesh.SurfaceGetMaterial(0);
            Color sceneColor = source.AlbedoColor;
            object[] before = Snapshot();
            object camera = CameraState();
            foreach (bool light in new[] { false, true, false, true })
            {
                _app.SetLightTheme(light);
                Check(three.Leader == leader && Snapshot().SequenceEqual(before) && Equals(camera, CameraState()), "3D leader and follow camera survive theme changes");
                var body = ordinary.GetNode<MeshInstance3D>("Body");
                var faded = (BaseMaterial3D)body.MaterialOverride;
                Check(faded.DistanceFadeMode == BaseMaterial3D.DistanceFadeModeEnum.PixelAlpha
                    && faded.AlbedoColor == LabPalette.For(light).Boid, "Distance fade matches the current palette");
                ordinary.SetSelected(true);
                Check(body.MaterialOverride != faded && ((BaseMaterial3D)body.MaterialOverride).DistanceFadeMode == BaseMaterial3D.DistanceFadeModeEnum.Disabled,
                    "Selection remains opaque above the fade material");
                ordinary.SetSelected(false);
            }
            var spawned = three.SpawnBoid(Vector3.Back * 100, Vector3.Zero);
            Check(spawned.GetNode<MeshInstance3D>("Body").MaterialOverride == ordinary.GetNode<MeshInstance3D>("Body").MaterialOverride,
                "New followers share the themed fade material");
            Check(source.AlbedoColor == sceneColor && source.DistanceFadeMode == BaseMaterial3D.DistanceFadeModeEnum.Disabled,
                "Theme changes never mutate the source scene material");
            three.Reset();
            Check(three.Boids.All(b => ((BaseMaterial3D)b.GetNode<MeshInstance3D>("Body").MaterialOverride).AlbedoColor == LabPalette.Light.Boid),
                "Reset retains the chosen 3D palette");
        }
    }

    private object[] Snapshot() => World is BoidManager two
        ? two.Boids.Select(b => (object)(b.GetInstanceId(), b.Position, b.Velocity, b.Acceleration, b.Rotation, b.AngularVelocity, b.IsLeader, b.IsSelected)).ToArray()
        : ((BoidManager3D)World).Boids.Select(b => (object)(b.GetInstanceId(), b.Position, b.Velocity, b.Acceleration, b.Quaternion, b.AngularVelocity, b.IsLeader, b.IsSelected)).ToArray();
    private object CameraState() => World is BoidManager two
        ? (object)(two.GetNode<SimulationCamera2D>("Camera2D").Transform, two.GetNode<SimulationCamera2D>("Camera2D").Zoom)
        : (((BoidManager3D)World).GetNode<SimulationCamera3D>("SimulationView/Camera3D").Transform,
            ((BoidManager3D)World).GetNode<SimulationCamera3D>("SimulationView/Camera3D").Size);
    private static double Contrast(Color a, Color b)
    {
        static double L(Color c)
        {
            static double Linear(double x) => x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
            return Linear(c.R) * 0.2126 + Linear(c.G) * 0.7152 + Linear(c.B) * 0.0722;
        }
        double x = L(a), y = L(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }
    private void Click(Vector2 point)
    {
        GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
        foreach (bool down in new[] { true, false })
            GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = down }, true);
    }
    private async Task Frame() { for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private void Check(bool value, string message) { _checks++; if (!value) throw new InvalidOperationException(message); }
}
