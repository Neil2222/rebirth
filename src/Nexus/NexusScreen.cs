using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Rebirth.Building;
using Rebirth.Items;
using Rebirth.Persistence;
using Rebirth.UI;
using Rebirth.World;

namespace Rebirth.Nexus;

/// <summary>
/// The strategic overview: a camera high above your little star system with every planet, site and
/// bot labelled, and menus to run it all — print bots at home, pick a planet and a design and send the
/// bots to build it, switch haul routes on and off. The world keeps running underneath.
/// </summary>
public partial class NexusScreen : CanvasLayer
{
	public event Action? Closed;
	public bool IsOpen => Visible;

	public Colony Colony { get; set; } = null!;

	private Camera3D _camera = null!;
	private Camera3D? _previousCamera;
	private Vector3 _focus;
	private Vector3 _focusTarget;
	/// <summary>Orbit frame: world axes for bodies, the station's own axes for stations on a planet's side.</summary>
	private Quaternion _frame = Quaternion.Identity, _frameTarget = Quaternion.Identity;
	private float _yaw = 0.6f, _pitch = -0.5f, _distance = 120f, _distanceTarget = 120f;
	private bool _dragging;

	// Selection: a body (with a build spot), a named grid, or nothing.
	private VoxelBody? _body;
	private BlockGrid? _grid;
	private Vector3? _spotNear;
	private Transform3D? _spot;
	private Node3D? _preview;

	private readonly List<(Node3D Target, Label3D Label, Func<string> Text)> _labels = new();
	private List<Blueprint> _stationDesigns = new();
	private List<Blueprint> _botDesigns = new();

	private ItemList _places = null!;
	private List<Node3D> _placeTargets = new();
	private Label _title = null!;
	private Label _details = null!;
	private VBoxContainer _buildBox = null!;
	private OptionButton _designPicker = null!;
	private Label _cost = null!;
	private Button _buildButton = null!;
	private VBoxContainer _botBox = null!;
	private OptionButton _botPicker = null!;
	private Label _botCost = null!;
	private CheckButton _routeToggle = null!;
	private Label _bots = null!;
	private VBoxContainer _jobs = null!;
	private Label _top = null!;
	private Label _message = null!;
	private int _jobsShown = -1;

	public override void _Ready()
	{
		Layer = 8;
		Visible = false;

		var root = new Control { Theme = UiTheme.Create(), MouseFilter = Control.MouseFilterEnum.Ignore };
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(root);

		// Top bar: title, home summary, close.
		var topPanel = new PanelContainer();
		topPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
		topPanel.OffsetLeft = 16; topPanel.OffsetRight = -16; topPanel.OffsetTop = 12;
		root.AddChild(topPanel);
		var top = new HBoxContainer();
		top.AddThemeConstantOverride("separation", 18);
		topPanel.AddChild(top);
		var title = new Label { Text = "NEXUS" };
		title.AddThemeColorOverride("font_color", UiTheme.Accent);
		title.AddThemeFontSizeOverride("font_size", 22);
		top.AddChild(title);
		_top = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center };
		top.AddChild(_top);
		AddButton(top, "Close  [N]", Close);

		// Left: places.
		var left = new PanelContainer { CustomMinimumSize = new Vector2(290, 0) };
		left.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.LeftWide);
		left.OffsetLeft = 16; left.OffsetTop = 80; left.OffsetBottom = -16; left.OffsetRight = 306;
		root.AddChild(left);
		var leftBox = new VBoxContainer();
		leftBox.AddThemeConstantOverride("separation", 8);
		left.AddChild(leftBox);
		leftBox.AddChild(UiTheme.Heading("PLACES"));
		_places = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		_places.ItemSelected += index => SelectPlace((int)index);
		leftBox.AddChild(_places);
		leftBox.AddChild(UiTheme.Heading("BOTS"));
		_bots = new Label { CustomMinimumSize = new Vector2(0, 150), AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_bots.AddThemeFontSizeOverride("font_size", 13);
		leftBox.AddChild(_bots);

		// Right: details and actions for the selection.
		var right = new PanelContainer { CustomMinimumSize = new Vector2(400, 0) };
		right.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.RightWide);
		right.OffsetRight = -16; right.OffsetTop = 80; right.OffsetBottom = -16; right.OffsetLeft = -416;
		root.AddChild(right);
		var rightBox = new VBoxContainer();
		rightBox.AddThemeConstantOverride("separation", 8);
		right.AddChild(rightBox);
		_title = new Label();
		_title.AddThemeColorOverride("font_color", UiTheme.Accent);
		_title.AddThemeFontSizeOverride("font_size", 20);
		rightBox.AddChild(_title);
		_details = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_details.AddThemeFontSizeOverride("font_size", 14);
		rightBox.AddChild(_details);

		_routeToggle = new CheckButton { Text = "Bots haul this site's ingots home" };
		_routeToggle.Toggled += OnRouteToggled;
		rightBox.AddChild(_routeToggle);

		_buildBox = new VBoxContainer();
		_buildBox.AddChild(UiTheme.Heading($"BUILD HERE WITH BOTS  (costs {Colony.AutoBuildCostFactor}× hand-built)"));
		_designPicker = new OptionButton();
		_designPicker.ItemSelected += _ => RefreshSpot();
		_buildBox.AddChild(_designPicker);
		_cost = new Label();
		_cost.AddThemeFontSizeOverride("font_size", 14);
		_buildBox.AddChild(_cost);
		_buildButton = new Button { Text = "Send bots to build" };
		_buildButton.Pressed += OrderBuild;
		_buildBox.AddChild(_buildButton);
		rightBox.AddChild(_buildBox);

		_botBox = new VBoxContainer();
		_botBox.AddChild(UiTheme.Heading("PRINT BOTS AT THE HOME FABRICATOR"));
		_botPicker = new OptionButton();
		_botPicker.ItemSelected += _ => UpdateBotCost();
		_botBox.AddChild(_botPicker);
		_botCost = new Label();
		_botCost.AddThemeFontSizeOverride("font_size", 14);
		_botBox.AddChild(_botCost);
		var print = new Button { Text = "Print bot" };
		print.Pressed += PrintBot;
		_botBox.AddChild(print);
		rightBox.AddChild(_botBox);

		rightBox.AddChild(UiTheme.Heading("CONSTRUCTION"));
		_jobs = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		rightBox.AddChild(_jobs);
		_message = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_message.AddThemeColorOverride("font_color", UiTheme.Accent);
		rightBox.AddChild(_message);

		var help = new Label { Text = "Drag: turn   Wheel: zoom   Click a planet: choose a spot   Click a station: select" };
		help.AddThemeColorOverride("font_color", UiTheme.HudText);
		help.AddThemeConstantOverride("outline_size", 6);
		help.AddThemeColorOverride("font_outline_color", UiTheme.HudOutline);
		help.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
		help.GrowHorizontal = Control.GrowDirection.Both;
		help.OffsetTop = -44;
		root.AddChild(help);

		_camera = new Camera3D { Far = 4000f, Fov = 55f, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
	}

	public override void _ExitTree()
	{
		if (_camera.GetParent() is null)
			_camera.Free();
	}

	private static void AddButton(Container parent, string text, Action action)
	{
		var button = new Button { Text = text };
		button.Pressed += action;
		parent.AddChild(button);
	}

	// ------------------------------------------------------------ open / close

	public void Open()
	{
		_previousCamera = GetViewport().GetCamera3D();
		Colony.World.AddChild(_camera);
		_camera.Current = true;

		_stationDesigns = Designs(DesignKind.Station);
		_botDesigns = Designs(DesignKind.Bot).Where(d => d.Contains(BlockKind.BotCore)).ToList();
		_designPicker.Clear();
		foreach (var design in _stationDesigns)
			_designPicker.AddItem(design.Name);
		int drillSite = _stationDesigns.FindIndex(d => d.Name == "Drill Site");
		_designPicker.Select(Mathf.Max(drillSite, 0));
		_botPicker.Clear();
		foreach (var design in _botDesigns)
			_botPicker.AddItem(design.Name);
		if (_botDesigns.Count > 0)
			_botPicker.Select(0);

		BuildPlaces();
		CreateLabels();
		_message.Text = "";
		_jobsShown = -1;
		Visible = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;
		SelectPlace(0);
		_focus = _focusTarget;
		_frame = _frameTarget;
		_distance = _distanceTarget;
	}

	public void Close()
	{
		if (!Visible)
			return;
		Visible = false;
		ClearPreview();
		foreach (var (_, label, _) in _labels)
			label.QueueFree();
		_labels.Clear();
		_camera.Current = false;
		_camera.GetParent()?.RemoveChild(_camera);
		if (_previousCamera is not null && IsInstanceValid(_previousCamera))
			_previousCamera.Current = true;
		Closed?.Invoke();
	}

	private static List<Blueprint> Designs(DesignKind kind) =>
		BlueprintLibrary.List().Select(BlueprintLibrary.Load).Where(d => d is not null && d.Kind == kind).Select(d => d!).ToList();

	// ------------------------------------------------------------ places

	private void BuildPlaces()
	{
		_places.Clear();
		_placeTargets = new List<Node3D>();
		if (Colony.Home is { } home)
		{
			_places.AddItem("⌂  Home");
			_placeTargets.Add(home);
		}
		var from = Colony.Home?.GlobalPosition ?? Vector3.Zero;
		foreach (var body in Colony.Bodies.OrderBy(b => b.GlobalPosition.DistanceTo(from)))
		{
			string kind = body is MiniPlanet ? "planet" : "asteroid";
			int sites = Colony.Sites.Count(s => NearBody(s) == body);
			_places.AddItem($"{(body is MiniPlanet ? "◉" : "•")}  {body.Name}   {kind}, {body.GlobalPosition.DistanceTo(from):0} m{(sites > 0 ? $", {sites} site{(sites > 1 ? "s" : "")}" : "")}");
			_placeTargets.Add(body);
		}
		foreach (var site in Colony.Sites)
		{
			_places.AddItem($"▣  {site.Label}");
			_placeTargets.Add(site);
		}
	}

	private VoxelBody? NearBody(Node3D node) =>
		Colony.Bodies.OrderBy(b => b.GlobalPosition.DistanceTo(node.GlobalPosition) - b.OuterRadius).FirstOrDefault();

	private void SelectPlace(int index)
	{
		if (index < 0 || index >= _placeTargets.Count)
			return;
		if (_places.GetSelectedItems() is not { Length: > 0 } sel || sel[0] != index)
			_places.Select(index);
		switch (_placeTargets[index])
		{
			case VoxelBody body:
				Select(body, null);
				break;
			case BlockGrid grid:
				Select(null, grid);
				break;
		}
	}

	private void Select(VoxelBody? body, BlockGrid? grid, Vector3? near = null)
	{
		_body = body;
		_grid = grid;
		_spotNear = near;
		if (body is not null)
		{
			_focusTarget = body.GlobalPosition;
			_frameTarget = Quaternion.Identity;
			_distanceTarget = body.Radius * 4f + 30f;
		}
		else if (grid is not null)
		{
			_focusTarget = grid.GlobalPosition;
			_frameTarget = grid.GlobalBasis.GetRotationQuaternion();
			_distanceTarget = 45f;
		}
		_buildBox.Visible = body is not null;
		_botBox.Visible = grid is not null && grid.Label == Colony.HomeLabel;
		_routeToggle.Visible = grid is not null && grid.Label != Colony.HomeLabel && grid.Inventory.Capacity > 0f;
		if (_routeToggle.Visible)
			_routeToggle.SetPressedNoSignal(Colony.Routes.Any(r => r.From == grid!.Label && r.Enabled));
		RefreshSpot();
		UpdateBotCost();
	}

	private Blueprint? SelectedDesign =>
		_designPicker.Selected >= 0 && _designPicker.Selected < _stationDesigns.Count ? _stationDesigns[_designPicker.Selected] : null;

	/// <summary>Works out the build spot for the chosen design and shows it as a hologram.</summary>
	private void RefreshSpot()
	{
		ClearPreview();
		_spot = null;
		if (_body is null || SelectedDesign is not { } design)
			return;
		_spot = Colony.SuggestSite(_body, design, _spotNear);
		var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/hologram.gdshader") };
		material.SetShaderParameter("tint", Palette.Mint);
		material.SetShaderParameter("progress", 2f);   // fully "printed": a solid-looking preview
		var model = BlueprintModel.Create(design, targetHeight: null, material);
		var site = _spot.Value;
		model.Transform = new Transform3D(site.Basis, site * design.Bounds().GetCenter());
		Colony.World.AddChild(model);
		_preview = model;
		var bounds = design.Bounds();
		material.SetShaderParameter("base_point", site * new Vector3(0, bounds.Position.Y, 0));
		material.SetShaderParameter("up_dir", site.Basis.Y);
		material.SetShaderParameter("height", bounds.Size.Y);
	}

	private void ClearPreview()
	{
		_preview?.QueueFree();
		_preview = null;
	}

	// ------------------------------------------------------------ actions

	private void OrderBuild()
	{
		if (_body is null || SelectedDesign is not { } design || _spot is not { } spot)
			return;
		if (Colony.Home is null)
		{
			Say("You need a home base with storage first.");
			return;
		}
		var job = Colony.OrderBuild(design, _body, spot);
		Say(Colony.Bots.Count == 0
			? $"{job.Name} is planned - print a bot at home to start building."
			: $"{job.Name} is planned. Bots will bring materials from home.");
		_spotNear = null;
		RefreshSpot();   // next suggestion, clear of the new site
	}

	private void PrintBot()
	{
		if (Colony.Home is not { } home || _botPicker.Selected < 0 || _botPicker.Selected >= _botDesigns.Count)
			return;
		var fabricator = home.Blocks.Where(b => b.Value.Definition.Kind == BlockKind.Fabricator).Select(b => (Vector3I?)b.Key).FirstOrDefault();
		if (fabricator is not { } cell)
		{
			Say("Home has no fabricator.");
			return;
		}
		home.EnqueuePrint(cell, _botDesigns[_botPicker.Selected]);
		Say($"Queued {_botDesigns[_botPicker.Selected].Name} at the home fabricator ({home.FabricatorQueue(cell).Count} in queue).");
	}

	private void OnRouteToggled(bool on)
	{
		if (_grid?.Label is not { } label)
			return;
		var route = Colony.Routes.FirstOrDefault(r => r.From == label);
		if (route is null && on)
			Colony.Routes.Add(new HaulRoute { From = label, To = Colony.HomeLabel });
		else if (route is not null)
			route.Enabled = on;
	}

	private void Say(string text) => _message.Text = text;

	// ------------------------------------------------------------ per frame

	public override void _Process(double delta)
	{
		if (!Visible)
			return;
		float dt = (float)delta;
		_focus = _focus.Lerp(_focusTarget, Mathf.Min(1f, dt * 4f));
		_distance = Mathf.Lerp(_distance, _distanceTarget, Mathf.Min(1f, dt * 4f));
		_frame = _frame.Slerp(_frameTarget, Mathf.Min(1f, dt * 4f));
		var orbit = new Basis(_frame) * Basis.FromEuler(new Vector3(_pitch, _yaw, 0f));
		_camera.GlobalTransform = new Transform3D(orbit, _focus + orbit * new Vector3(0, 0, _distance));

		foreach (var (target, label, text) in _labels)
		{
			if (!IsInstanceValid(target) || target.IsQueuedForDeletion())
			{
				label.Visible = false;
				continue;
			}
			label.GlobalPosition = target.GlobalPosition + LabelLift(target);
			label.Text = text();
		}
		// Bots and sites come and go while the screen is open.
		if (Colony.Bots.Any(b => _labels.All(l => l.Target != b.Grid)) || Colony.Jobs.Any(j => j.Hologram is not null && _labels.All(l => l.Target != j.Hologram)))
			CreateLabels();

		_top.Text = TopText();
		_bots.Text = Colony.Bots.Count == 0
			? "No bots yet. Select Home to print one."
			: string.Join("\n", Colony.Bots.Select(b => $"{b.Grid.Label}: {b.Status}"));
		UpdateDetails();
		UpdateJobs();
		if (_buildBox.Visible)
			UpdateCost();
	}

	private static Vector3 LabelLift(Node3D target) => target switch
	{
		// Names sit on the body itself, so they never cover a station's label above its surface.
		VoxelBody => Vector3.Zero,
		BlockGrid => Vector3.Up * 6f,
		_ => Vector3.Up * 8f,
	};

	private string TopText()
	{
		if (Colony.Home is not { } home)
			return "No home base";
		string stock = string.Join("   ", home.Inventory.Items.Where(kv => ItemCatalog.Get(kv.Key).Category == ItemCategory.Ingot && kv.Value >= 1f)
			.OrderBy(kv => kv.Key).Select(kv => $"{ItemCatalog.DisplayName(kv.Key)} {kv.Value:0}"));
		int idle = Colony.Bots.Count(b => b.Status is "Idle");
		return $"Home: {(stock.Length > 0 ? stock : "no ingots")}      Bots: {Colony.Bots.Count} ({idle} idle)      Sites: {Colony.Sites.Count()}";
	}

	private void UpdateDetails()
	{
		var sb = new StringBuilder();
		if (_body is not null)
		{
			_title.Text = _body.Name;
			sb.Append(_body is MiniPlanet planet ? $"Small planet, {planet.Radius:0} m across the middle, own gravity.\n" : $"Asteroid, about {_body.Radius:0} m.\n");
			if (Colony.Home is { } home)
				sb.Append($"{_body.GlobalPosition.DistanceTo(home.GlobalPosition):0} m from home.\n");
			var sites = Colony.Sites.Where(s => NearBody(s) == _body).ToList();
			sb.Append(sites.Count == 0 ? "No sites yet.\n" : $"Sites: {string.Join(", ", sites.Select(s => s.Label))}\n");
			sb.Append(_spotNear is null ? "Spot: sunny side, facing home. Click the surface to choose another.\n" : "Spot: where you clicked.\n");
		}
		else if (_grid is not null && IsInstanceValid(_grid) && !_grid.IsQueuedForDeletion())
		{
			_title.Text = _grid.Label ?? "Station";
			sb.Append($"Power: {_grid.PowerDelivered:0.00} / {_grid.PowerDemand:0.00} MW  (solar {_grid.SolarProduction:0.00})\n");
			foreach (var (cell, block) in _grid.Blocks.Where(b => b.Value.Definition.Kind is BlockKind.AutoDrill or BlockKind.Refinery))
				sb.Append($"{block.Definition.DisplayName}: {_grid.MachineStatus(cell)}\n");
			foreach (var (cell, _) in _grid.Blocks.Where(b => b.Value.Definition.Kind == BlockKind.Fabricator))
				sb.Append($"Fabricator: {_grid.FabricatorStatus(cell)}  ({_grid.FabricatorQueue(cell).Count} queued)\n");
			var inv = _grid.Inventory;
			sb.Append($"\nStorage {inv.Total:0} / {inv.Capacity:0} kg\n");
			foreach (var (item, amount) in inv.Items.Where(kv => kv.Value >= 1f).OrderBy(kv => kv.Key))
				sb.Append($"  {ItemCatalog.DisplayName(item),-14} {amount,7:0} kg\n");
		}
		else
		{
			_title.Text = "";
		}
		_details.Text = sb.ToString();
	}

	private void UpdateCost()
	{
		if (SelectedDesign is not { } design)
		{
			_cost.Text = "No station designs. Make one in the Forge.";
			_buildButton.Disabled = true;
			return;
		}
		var home = Colony.Home?.Inventory;
		var sb = new StringBuilder($"{design.Blocks.Count} blocks. Needs (bots fetch it from home as it comes in):\n");
		foreach (var (item, amount) in Colony.AutoBuildCost(design).OrderBy(kv => kv.Key))
		{
			float have = home?.Get(item) ?? 0f;
			sb.Append($"  {ItemCatalog.DisplayName(item),-14}{amount,7:0}   home {have,7:0}\n");
		}
		_cost.Text = sb.ToString();
		_buildButton.Disabled = _spot is null;
	}

	private void UpdateBotCost()
	{
		if (_botPicker.Selected < 0 || _botPicker.Selected >= _botDesigns.Count)
		{
			_botCost.Text = "No bot designs. In the Forge, set the kind to Bot and add a Bot Core.";
			return;
		}
		var design = _botDesigns[_botPicker.Selected];
		var home = Colony.Home?.Inventory;
		_botCost.Text = string.Join("\n", design.TotalCost().OrderBy(kv => kv.Key)
			.Select(kv => $"  {ItemCatalog.DisplayName(kv.Key),-14}{kv.Value,7:0}   home {home?.Get(kv.Key) ?? 0f,7:0}"));
	}

	private void UpdateJobs()
	{
		if (_jobsShown != Colony.Jobs.Count)
		{
			_jobsShown = Colony.Jobs.Count;
			foreach (var child in _jobs.GetChildren())
				child.QueueFree();
			foreach (var job in Colony.Jobs)
			{
				var row = new HBoxContainer();
				var label = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
				label.AddThemeFontSizeOverride("font_size", 13);
				row.AddChild(label);
				var cancel = new Button { Text = "Cancel" };
				cancel.Pressed += () =>
				{
					Colony.CancelJob(job);
					Say($"Cancelled {job.Name}; its stock goes back home.");
				};
				row.AddChild(cancel);
				_jobs.AddChild(row);
			}
			if (Colony.Jobs.Count == 0)
				_jobs.AddChild(new Label { Text = "Nothing under construction." });
		}
		for (int i = 0; i < Colony.Jobs.Count && i < _jobs.GetChildCount(); i++)
		{
			var job = Colony.Jobs[i];
			if (_jobs.GetChild(i) is HBoxContainer row && row.GetChild(0) is Label label)
				label.Text = $"{job.Name}: {job.Built}/{job.Order.Count} blocks - {JobStatus(job)}";
		}
	}

	private static string JobStatus(ConstructionJob job) =>
		job.Assembler is not null ? "building"
		: job.InTransit.Values.Any(v => v > 0.5f) ? "materials on the way"
		: job.Status;

	// ------------------------------------------------------------ labels in the world

	private void CreateLabels()
	{
		foreach (var (_, label, _) in _labels)
			label.QueueFree();
		_labels.Clear();
		foreach (var body in Colony.Bodies)
			AddLabel(body, 48, () => body.Name, UiTheme.HudText);
		if (Colony.Home is { } home)
			AddLabel(home, 40, () => "⌂ Home", new Color(1f, 0.8f, 0.45f));
		foreach (var site in Colony.Sites)
			AddLabel(site, 34, () => $"▣ {site.Label}", new Color(0.7f, 1f, 0.8f));
		foreach (var bot in Colony.Bots)
			AddLabel(bot.Grid, 26, () => $"{bot.Grid.Label}", new Color(1f, 0.75f, 0.6f));
		foreach (var job in Colony.Jobs.Where(j => j.Hologram is not null))
			AddLabel(job.Hologram!, 32, () => $"{job.Name}  {job.Progress:P0}", Palette.Lemon);
	}

	private void AddLabel(Node3D target, int size, Func<string> text, Color color)
	{
		var label = new Label3D
		{
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			NoDepthTest = true,
			FixedSize = true,
			PixelSize = 0.0009f,
			FontSize = size,
			OutlineSize = 10,
			Modulate = color,
			OutlineModulate = UiTheme.HudOutline,
			RenderPriority = 10,
		};
		Colony.World.AddChild(label);
		_labels.Add((target, label, text));
	}

	// ------------------------------------------------------------ input

	public override void _UnhandledInput(InputEvent e)
	{
		if (!Visible)
			return;
		if (e.IsActionPressed("open_nexus") || e.IsActionPressed("release_mouse"))
		{
			Close();
			GetViewport().SetInputAsHandled();
			return;
		}
		switch (e)
		{
			case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
				_distanceTarget = Mathf.Max(12f, _distanceTarget * 0.88f);
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
				_distanceTarget = Mathf.Min(1500f, _distanceTarget * 1.14f);
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Right or MouseButton.Middle } drag:
				_dragging = drag.Pressed;
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click:
				Pick(click.Position);
				break;
			case InputEventMouseMotion motion when _dragging || (motion.ButtonMask & MouseButtonMask.Left) != 0:
				_yaw -= motion.Relative.X * 0.005f;
				_pitch = Mathf.Clamp(_pitch - motion.Relative.Y * 0.005f, -1.5f, 1.5f);
				break;
			default:
				return;
		}
		GetViewport().SetInputAsHandled();
	}

	/// <summary>Click in the world: a body picks a build spot there, a station selects it.</summary>
	private void Pick(Vector2 screen)
	{
		Vector3 from = _camera.ProjectRayOrigin(screen);
		Vector3 to = from + _camera.ProjectRayNormal(screen) * 4000f;
		var hit = _camera.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to));
		if (hit.Count == 0)
			return;
		switch (hit["collider"].AsGodotObject())
		{
			case VoxelBody body:
				Select(body, null, hit["position"].AsVector3());
				_places.DeselectAll();
				if (_placeTargets.IndexOf(body) is var i and >= 0)
					_places.Select(i);
				break;
			case BlockGrid { Label: not null, IsBot: false } grid:
				Select(null, grid);
				_places.DeselectAll();
				if (_placeTargets.IndexOf(grid) is var j and >= 0)
					_places.Select(j);
				break;
		}
	}
}
