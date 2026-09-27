using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Rebirth.Building;
using Rebirth.Core;
using Rebirth.Items;
using Rebirth.Life;
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
	/// <summary>The player pressed "Fire the Breach Lance".</summary>
	public event Action? FireRequested;
	public bool IsOpen => Visible;

	public Colony Colony { get; set; } = null!;
	public People People { get; set; } = null!;
	public TalkPanel Talk { get; set; } = null!;
	public BoxMapPanel BoxMap { get; set; } = null!;
	public Threats.Threats Threats { get; set; } = null!;
	/// <summary>Opens the purge puzzle for a quarantined machine.</summary>
	public Action<BlockGrid, Vector3I> PurgeRequested { get; set; } = (_, _) => { };
	/// <summary>Opens the conversation with a swarm.</summary>
	public Action<Threats.Swarm> SwarmRequested { get; set; } = _ => { };
	/// <summary>A puzzle or swarm panel on top: the Nexus leaves the keys alone.</summary>
	public Func<bool> Covered { get; set; } = () => false;
	public Guide Guide { get; set; } = null!;
	public StatsPanel Stats { get; set; } = null!;

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
	/// <summary>Things drawn only while the Nexus is open: dark veils over unlinked bodies, flow lines.</summary>
	private readonly List<Node3D> _overlays = new();
	private readonly List<(HaulRoute Route, MeshInstance3D Line, ShaderMaterial Material)> _flows = new();
	private string _survey = "";
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
	private VBoxContainer _routeBox = null!;
	private VBoxContainer _routeRows = null!;
	private OptionButton _routeTarget = null!;
	private OptionButton _routeCargo = null!;
	private List<string> _routeTargets = new();
	private string _routesShownFor = "";
	private Label _news = null!;
	private Label _buildHeading = null!;
	private Button _uplinkButton = null!;
	private HBoxContainer _stock = null!;
	private string _stockShown = "";
	private string _linkedShown = "";
	private VBoxContainer _peopleBox = null!;
	private VBoxContainer _threatBox = null!;
	private string _threatsShown = "";
	private Label _goalTitle = null!;
	private Label _goalText = null!;
	private float _goalTimer;
	private VBoxContainer _lanceBox = null!;
	private Label _lanceText = null!;
	private Button _fireButton = null!;
	private string _peopleShown = "";
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
		_stock = new HBoxContainer();
		_stock.AddThemeConstantOverride("separation", 3);
		top.AddChild(_stock);
		_top = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center };
		top.AddChild(_top);
		AddButton(top, "Production", () => Stats.Open());
		AddButton(top, "Boxes & upgrades", () => BoxMap.Open());
		AddButton(top, "Close  [N]", Close);

		// Left: places.
		var left = new PanelContainer { CustomMinimumSize = new Vector2(290, 0) };
		left.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.LeftWide);
		left.OffsetLeft = 16; left.OffsetTop = 80; left.OffsetBottom = -16; left.OffsetRight = 306;
		root.AddChild(left);
		var leftBox = new VBoxContainer();
		leftBox.AddThemeConstantOverride("separation", 8);
		left.AddChild(leftBox);
		var goalCard = new PanelContainer();
		goalCard.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(1f, 0.9f, 0.72f), UiTheme.Accent, 2, 10));
		leftBox.AddChild(goalCard);
		var goalBox = new VBoxContainer();
		goalCard.AddChild(goalBox);
		_goalTitle = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_goalTitle.AddThemeColorOverride("font_color", UiTheme.Accent);
		_goalTitle.AddThemeFontSizeOverride("font_size", 15);
		goalBox.AddChild(_goalTitle);
		_goalText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(250, 0) };
		_goalText.AddThemeFontSizeOverride("font_size", 13);
		goalBox.AddChild(_goalText);
		var showMe = new Button { Text = "Show me" };
		showMe.Pressed += ShowGoal;
		goalBox.AddChild(showMe);
		leftBox.AddChild(UiTheme.Heading("PLACES"));
		_places = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		_places.ItemSelected += index => SelectPlace((int)index);
		leftBox.AddChild(_places);
		leftBox.AddChild(UiTheme.Heading("BOTS"));
		_bots = new Label { CustomMinimumSize = new Vector2(0, 150), AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_bots.AddThemeFontSizeOverride("font_size", 13);
		leftBox.AddChild(_bots);
		leftBox.AddChild(UiTheme.Heading("NEWS"));
		_news = new Label { CustomMinimumSize = new Vector2(0, 110), AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_news.AddThemeFontSizeOverride("font_size", 13);
		_news.AddThemeColorOverride("font_color", UiTheme.Dim);
		leftBox.AddChild(_news);

		// Right: details and actions for the selection.
		var right = new PanelContainer { CustomMinimumSize = new Vector2(450, 0) };
		right.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.RightWide);
		right.OffsetRight = -16; right.OffsetTop = 80; right.OffsetBottom = -16; right.OffsetLeft = -466;
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
		_lanceBox = new VBoxContainer();
		_lanceBox.AddChild(UiTheme.Heading("BREACH LANCE"));
		_lanceText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_lanceText.AddThemeFontSizeOverride("font_size", 14);
		_lanceBox.AddChild(_lanceText);
		_fireButton = new Button { Text = "Fire the Breach Lance" };
		_fireButton.Pressed += () => FireRequested?.Invoke();
		_lanceBox.AddChild(_fireButton);
		rightBox.AddChild(_lanceBox);
		_threatBox = new VBoxContainer();
		_threatBox.AddThemeConstantOverride("separation", 4);
		rightBox.AddChild(_threatBox);
		_uplinkButton = new Button { Text = "Send bots to set up an Uplink" };
		_uplinkButton.Pressed += OrderUplink;
		rightBox.AddChild(_uplinkButton);
		_peopleBox = new VBoxContainer();
		_peopleBox.AddThemeConstantOverride("separation", 4);
		rightBox.AddChild(_peopleBox);

		_routeBox = new VBoxContainer();
		_routeBox.AddChild(UiTheme.Heading("ROUTES FROM HERE  (bots haul on their own)"));
		_routeRows = new VBoxContainer();
		_routeBox.AddChild(_routeRows);
		var addRow = new HBoxContainer();
		addRow.AddThemeConstantOverride("separation", 6);
		addRow.AddChild(new Label { Text = "to" });
		_routeTarget = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		addRow.AddChild(_routeTarget);
		_routeCargo = new OptionButton();
		foreach (var cargo in Enum.GetValues<HaulCargo>())
			_routeCargo.AddItem(cargo.ToString(), (int)cargo);
		addRow.AddChild(_routeCargo);
		AddButton(addRow, "Add", AddRoute);
		_routeBox.AddChild(addRow);
		rightBox.AddChild(_routeBox);

		_buildBox = new VBoxContainer();
		_buildHeading = UiTheme.Heading("");
		_buildBox.AddChild(_buildHeading);
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
		_linkedShown = "";
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
		ClearOverlays();
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
			bool linked = Colony.IsLinked(body);
			int sites = Colony.Sites.Count(s => NearBody(s) == body);
			string symbol = !linked ? "◌" : body is MiniPlanet ? "◉" : "•";
			int index = _places.AddItem($"{symbol}  {body.Name}   {(linked ? kind : "dark")}, {body.GlobalPosition.DistanceTo(from):0} m{(sites > 0 ? $", {sites} site{(sites > 1 ? "s" : "")}" : "")}");
			if (!linked)
				_places.SetItemCustomFgColor(index, UiTheme.Dim);
			_placeTargets.Add(body);
		}
		foreach (var site in Colony.Sites)
		{
			int index = _places.AddItem($"▣  {site.Label}{(Colony.Problems(site).Count > 0 ? "  ⚠" : "")}");
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
			_frameTarget = grid.GlobalBasis.Orthonormalized().GetRotationQuaternion().Normalized();
			_distanceTarget = 45f;
		}
		_buildBox.Visible = body is not null && Colony.IsLinked(body);
		_botBox.Visible = grid is not null && grid.Label == Colony.HomeLabel;
		_routeBox.Visible = grid is not null && grid.Inventory.Capacity > 0f;
		_routesShownFor = "";
		if (_routeBox.Visible)
		{
			_routeTargets = Colony.Grids.Where(g => g != grid && g.IsStatic && !g.IsBot && g.Label is not null && g.Inventory.Capacity > 0f)
				.Select(g => g.Label!).OrderBy(l => l == Colony.HomeLabel ? "" : l).ToList();
			_routeTarget.Clear();
			foreach (var target in _routeTargets)
				_routeTarget.AddItem(target);
		}
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
		_survey = "";
		if (_body is null || SelectedDesign is not { } design || !Colony.IsLinked(_body))
			return;
		_spot = Colony.SuggestSite(_body, design, _spotNear);
		_survey = SurveyText(_body, _spot.Value);
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

	/// <summary>A planet lighting up (or a new site) redraws the list, veils and labels, keeping the selection.</summary>
	private void RefreshWhenLinksChange()
	{
		string key = string.Join(",", Colony.Bodies.Where(Colony.IsLinked).Select(b => b.Name)) + "|" + string.Join(",", Colony.Sites.Select(s => s.Label));
		if (key == _linkedShown)
			return;
		bool first = _linkedShown.Length == 0;
		_linkedShown = key;
		if (first)
			return;
		var (body, grid, near) = (_body, _grid, _spotNear);
		BuildPlaces();
		CreateLabels();
		Select(body, grid, near);
		int index = _placeTargets.IndexOf((Node3D?)body ?? grid!);
		if (index >= 0)
			_places.Select(index);
	}

	private void UpdateGoal(float dt)
	{
		_goalTimer -= dt;
		if (_goalTimer > 0f)
			return;
		_goalTimer = 0.5f;
		var goal = Guide.Current();
		_goalTitle.GetParent().GetParent<Control>().Visible = goal is not null;
		if (goal is null)
			return;
		_goalTitle.Text = "NEXT GOAL:  " + goal.Title;
		string? progress = goal.Progress?.Invoke();
		_goalText.Text = Keybinds.Fill(goal.How) + (progress is null ? "" : "\n" + progress);
	}

	/// <summary>Selects the place the goal is about and picks the design to build there.</summary>
	private void ShowGoal()
	{
		if (Guide.Current() is not { Show: { } show } goal)
			return;
		var (target, design) = show();
		switch (target)
		{
			case VoxelBody body:
				Select(body, null);
				break;
			case BlockGrid grid:
				Select(null, grid);
				break;
			default:
				return;
		}
		int index = _placeTargets.IndexOf(target);
		_places.DeselectAll();
		if (index >= 0)
			_places.Select(index);
		if (design is not null && _stationDesigns.FindIndex(d => d.Name == design) is var d and >= 0)
		{
			_designPicker.Select(d);
			RefreshSpot();
		}
		Say(target switch
		{
			VoxelBody b when !Colony.IsLinked(b) => $"{b.Name} is selected. Press \"Send bots to set up an Uplink\" on the right.",
			VoxelBody b when design is not null => $"{b.Name} is selected with \"{design}\". Press \"Send bots to build\" on the right.",
			BlockGrid { Label: Colony.HomeLabel } => "Home is selected. \"Print bot\" is on the right.",
			_ => $"{goal.Title}: see the panel on the right.",
		});
	}

	/// <summary>Viruses in the selected station's machines and a swarm over it, each with a button to deal with it.</summary>
	private void UpdateThreats()
	{
		var grid = _grid is not null && IsInstanceValid(_grid) && !_grid.IsQueuedForDeletion() ? _grid : null;
		var swarm = grid is null ? null : Threats.SwarmNear(grid.GlobalPosition, Rebirth.Threats.Threats.SwarmReach);
		string key = grid is null ? "" : string.Join(",", grid.Quarantined) + "|" + (swarm is null ? "" : swarm.Site);
		if (key == _threatsShown)
			return;
		_threatsShown = key;
		foreach (var child in _threatBox.GetChildren())
			child.QueueFree();
		if (grid is null || (grid.Quarantined.Count == 0 && swarm is null))
			return;
		_threatBox.AddChild(UiTheme.Heading("TROUBLE"));
		foreach (var cell in grid.Quarantined.ToList())
		{
			var name = grid.Blocks.First(b => b.Key == cell).Value.Definition.DisplayName;
			var purge = new Button { Text = $"Purge the virus in the {name}" };
			purge.Pressed += () => PurgeRequested(grid, cell);
			_threatBox.AddChild(purge);
		}
		if (swarm is not null)
		{
			var meet = new Button { Text = "Deal with the Curator swarm" };
			meet.Pressed += () => SwarmRequested(swarm);
			_threatBox.AddChild(meet);
		}
	}

	/// <summary>Home's ingots as item pictures in the top bar.</summary>
	private void UpdateStock()
	{
		var items = Colony.Home?.Inventory.Items.Where(kv => ItemCatalog.Get(kv.Key).Category == ItemCategory.Ingot && kv.Value >= 1f).OrderBy(kv => kv.Key).ToList()
			?? new List<KeyValuePair<string, float>>();
		string key = string.Join(";", items.Select(kv => $"{kv.Key}:{Icons.Short(kv.Value)}"));
		if (key == _stockShown)
			return;
		_stockShown = key;
		foreach (var child in _stock.GetChildren())
			child.QueueFree();
		foreach (var (item, amount) in items)
			_stock.AddChild(ItemSlot.Create(item, amount, 40f));
	}

	/// <summary>Dark planet: bots carry an Uplink Post there, after which the Nexus can see and build on it.</summary>
	private void OrderUplink()
	{
		if (_body is null || Colony.Home is null)
			return;
		var post = Presets.UplinkPost();
		var job = Colony.OrderBuild(post, _body, Colony.SuggestSite(_body, post, _spotNear));
		Say(Colony.Bots.Count == 0
			? $"{job.Name} is planned - print a bot at home first."
			: $"Bots are on their way to set up {job.Name}. {_body.Name} lights up once it stands.");
	}

	/// <summary>The Lance's charge, and the button that fires it once full.</summary>
	private void UpdateLance()
	{
		_lanceBox.Visible = _grid is not null && IsInstanceValid(_grid) && _grid.HasBlock(BlockKind.BreachLance);
		if (!_lanceBox.Visible)
			return;
		float fraction = Colony.LanceFraction;
		_lanceText.Text = $"Ball of light  {Bar(fraction)}  {fraction:P0}\n" + (fraction >= 0.999f
			? "Full. Everyone you woke is ready to lend their light."
			: $"It drinks your Resonance ({Colony.LanceCharge:0} / {Colony.LanceRequired:0}). Living planets and villages fill it.");
		_fireButton.Disabled = fraction < 0.999f;
	}

	/// <summary>Villages on the selected planet (or at the selected station): people, bond, and their request.</summary>
	private void UpdatePeople()
	{
		var villages = People.Settlements.Where(s =>
			(_body is not null && s.Planet == _body.Name) || (_grid is not null && s.Anchor == _grid.Label)).ToList();
		string key = string.Join(";", villages.Select(v => $"{v.Name}/{v.Population}/{v.Bond:0}/{v.Request?.Text}/{People.Progress(v)}"));
		if (key == _peopleShown)
			return;
		_peopleShown = key;
		foreach (var child in _peopleBox.GetChildren())
			child.QueueFree();
		if (villages.Count == 0)
			return;
		_peopleBox.AddChild(UiTheme.Heading("PEOPLE"));
		foreach (var village in villages)
		{
			var about = new Label { Text = $"{village.Name}: {village.Population} people   Bond {TalkPanel.Hearts(village.Bond)}", AutowrapMode = TextServer.AutowrapMode.WordSmart };
			about.AddThemeFontSizeOverride("font_size", 14);
			_peopleBox.AddChild(about);
			if (village.Request is { } request)
			{
				var ask = new Label { Text = $"{request.Person}: {request.Text}\n{People.Progress(village)}", AutowrapMode = TextServer.AutowrapMode.WordSmart };
				ask.AddThemeFontSizeOverride("font_size", 13);
				_peopleBox.AddChild(ask);
			}
			var visit = new Button { Text = village.Request is { Kind: RequestKind.Talk } talk ? $"Talk to {talk.Person}" : $"Visit {village.Name}" };
			visit.Pressed += () => Talk.Open(village);
			_peopleBox.AddChild(visit);
		}
	}

	private static string Bar(float fraction) =>
		new string('█', Mathf.RoundToInt(fraction * 12f)) + new string('░', 12 - Mathf.RoundToInt(fraction * 12f));

	/// <summary>How alive a planet is, and what to do about it.</summary>
	private static string VitalsText(MiniPlanet planet)
	{
		var sb = new StringBuilder($"\nAir    {Bar(planet.Air)}  {planet.Air:P0}\nWater  {Bar(planet.Water)}  {planet.Water:P0}\nSoil   {Bar(planet.Soil)}  {planet.Soil:P0}\n");
		if (planet.ResonancePerMinute > 0.01f)
			sb.Append($"Gives off {planet.ResonancePerMinute:0.0} Resonance/min\n");
		sb.Append(planet.Soil >= 0.999f ? "Fully alive.\n"
			: planet.Air < Colony.GardenThreshold || planet.Water < Colony.GardenThreshold
				? $"Heal it: Air Makers turn stone into air, Water Works melt ice (drill it on Frost) into seas. Gardens take at {Colony.GardenThreshold:P0} air and water.\n"
				: !People.IsHabitable(planet) ? $"Air and seas are back: Gardens can grow soil now. At {People.HabitableSoil:P0} soil, an Incubator can wake people here.\n"
				: "Habitable: an Incubator (Settlement Seed) wakes families here.\n");
		sb.Append($"Each vital takes {planet.KilogramsForFullVital / 1000f:0} t of stone or ice.\n");
		return sb.ToString();
	}

	/// <summary>What a drill under this spot would bring up: the ground in a ball below the site.</summary>
	private static string SurveyText(VoxelBody body, Transform3D site)
	{
		var found = body.Survey(site.Origin - site.Basis.Y * 7f, 6f);
		float total = found.Values.Sum();
		if (total < 1f)
			return "Ground under this spot: nothing solid within drill reach.";
		return "Ground under this spot: " + string.Join(", ", found.OrderByDescending(kv => kv.Value)
			.Where(kv => kv.Value / total >= 0.01f)
			.Select(kv => $"{ItemCatalog.DisplayName(kv.Key)} {kv.Value / total:P0}"));
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

	private void AddRoute()
	{
		if (_grid?.Label is not { } from || _routeTarget.Selected < 0 || _routeTarget.Selected >= _routeTargets.Count)
			return;
		var cargo = (HaulCargo)_routeCargo.GetSelectedId();
		string to = _routeTargets[_routeTarget.Selected];
		Say(Colony.AddRoute(from, to, cargo) ? $"Bots will haul {cargo.ToString().ToLowerInvariant()} from {from} to {to}." : "That route already runs.");
		_routesShownFor = "";
	}

	/// <summary>One row per route leaving the selected station, with on/off and remove.</summary>
	private void UpdateRoutes()
	{
		if (!_routeBox.Visible || _grid?.Label is not { } label)
			return;
		var routes = Colony.Routes.Where(r => r.From == label).ToList();
		string key = label + ":" + string.Join(";", routes.Select(r => $"{r.To}/{r.Cargo}/{r.Enabled}"));
		if (key == _routesShownFor)
			return;
		_routesShownFor = key;
		foreach (var child in _routeRows.GetChildren())
			child.QueueFree();
		if (routes.Count == 0)
			_routeRows.AddChild(new Label { Text = "None yet." });
		foreach (var route in routes)
		{
			var row = new HBoxContainer();
			var toggle = new CheckButton { Text = $"→ {route.To}  ({route.Cargo.ToString().ToLowerInvariant()})", ButtonPressed = route.Enabled, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			toggle.Toggled += on => route.Enabled = on;
			row.AddChild(toggle);
			var remove = new Button { Text = "✕", TooltipText = "Remove this route" };
			remove.Pressed += () =>
			{
				Colony.Routes.Remove(route);
				_routesShownFor = "";
			};
			row.AddChild(remove);
			_routeRows.AddChild(row);
		}
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
		_frame = _frame.Normalized().Slerp(_frameTarget.Normalized(), Mathf.Min(1f, dt * 4f)).Normalized();
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
			// Zoomed far out, only planets and Home keep their names; the rest would pile up.
			float seen = _camera.GlobalPosition.DistanceTo(label.GlobalPosition);
			label.Visible = target is VoxelBody || target == Colony.Home || seen < (target is BlockGrid { IsBot: true } ? 160f : 320f);
		}
		// Bots and sites come and go while the screen is open.
		if (Colony.Bots.Any(b => _labels.All(l => l.Target != b.Grid)) || Colony.Jobs.Any(j => j.Hologram is not null && _labels.All(l => l.Target != j.Hologram))
			|| Threats.Swarms.Any(s => _labels.All(l => l.Target != s)))
			CreateLabels();

		_top.Text = TopText();
		_bots.Text = Colony.Bots.Count == 0
			? "No bots yet. Select Home to print one."
			: string.Join("\n", Colony.Bots.Select(b => $"{b.Grid.Label}: {b.Status}"));
		RefreshWhenLinksChange();
		UpdateGoal((float)delta);
		UpdateDetails();
		UpdateThreats();
		UpdateStock();
		_uplinkButton.Visible = _body is not null && !Colony.IsLinked(_body) && Colony.Jobs.All(j => j.BodyName != _body.Name);
		UpdateLance();
		UpdatePeople();
		UpdateRoutes();
		UpdateJobs();
		UpdateFlows();
		_news.Text = string.Join("\n", Colony.RecentNews.Take(5).Select(n => $"· {n.Text}"));
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
		int idle = Colony.Bots.Count(b => b.Status is "Idle");
		var (made, received) = Colony.RatesPerMinute(home);
		float income = made.Concat(received).Where(kv => ItemCatalog.Get(kv.Key).Category == ItemCategory.Ingot).Sum(kv => kv.Value);
		return $"  +{income:0} kg/min      Bots {Colony.Bots.Count} ({idle} idle)      Sites {Colony.Sites.Count()}" +
			(People.Settlements.Count > 0 ? $"      People {People.Settlements.Sum(s => s.Population)}" : "") +
			$"      Resonance {Colony.Resonance:0}{(Colony.ResonancePerMinute > 0.01f ? $" (+{Colony.ResonancePerMinute:0.0}/min)" : "")}" +
			(Colony.HasLance ? $"      Lance {Colony.LanceFraction:P0}" : "") +
			$"      {Core.Campaign.Active.CurrentBox.Name}";
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
			if (!Colony.IsLinked(_body))
			{
				var cost = Colony.AutoBuildCost(Presets.UplinkPost());
				sb.Append($"\nDark: out of the Nexus's reach. Send bots to set up an Uplink ({string.Join(", ", cost.Select(kv => $"{kv.Value:0} {ItemCatalog.DisplayName(kv.Key)}"))}), or fly there and place one yourself (cheaper).\n");
				_details.Text = sb.ToString();
				return;
			}
			var sites = Colony.Sites.Where(s => NearBody(s) == _body).ToList();
			sb.Append(sites.Count == 0 ? "No sites yet.\n" : $"Sites: {string.Join(", ", sites.Select(s => s.Label))}\n");
			if (_body is MiniPlanet living)
				sb.Append(VitalsText(living));
			sb.Append(_spotNear is null ? "Spot: sunny side, facing home. Click the surface to choose another.\n" : "Spot: where you clicked.\n");
			if (_survey.Length > 0)
				sb.Append(_survey + "\n");
		}
		else if (_grid is not null && IsInstanceValid(_grid) && !_grid.IsQueuedForDeletion())
		{
			_title.Text = _grid.Label ?? "Station";
			foreach (var problem in Colony.Problems(_grid))
				sb.Append($"⚠ {problem}\n");
			var (made, received) = Colony.RatesPerMinute(_grid);
			if (made.Count > 0)
				sb.Append("Makes: " + string.Join(", ", made.OrderBy(kv => kv.Key).Select(kv => $"{ItemCatalog.DisplayName(kv.Key)} {kv.Value:0}/min")) + "\n");
			if (received.Count > 0)
				sb.Append("Bots bring: " + string.Join(", ", received.OrderBy(kv => kv.Key).Select(kv => $"{ItemCatalog.DisplayName(kv.Key)} {kv.Value:0}/min")) + "\n");
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
		_buildHeading.Text = $"BUILD HERE WITH BOTS  (costs {Colony.AutoBuildCostFactor:0.##}× hand-built)";
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
		ClearOverlays();
		foreach (var body in Colony.Bodies)
		{
			bool linked = Colony.IsLinked(body);
			AddLabel(body, linked ? 48 : 38, () => !Colony.IsLinked(body) ? $"◌ {body.Name} (dark)"
				: body is MiniPlanet { Vitality: > 0.005f } p ? $"{body.Name}  ♥ {p.Vitality:P0}" : body.Name,
				linked ? UiTheme.HudText : new Color(0.7f, 0.68f, 0.78f));
			if (!linked)
				AddVeil(body);
		}
		if (Colony.Home is { } home)
			AddLabel(home, 40, () => Colony.Problems(home).Count > 0 ? "⌂ Home ⚠" : "⌂ Home", new Color(1f, 0.8f, 0.45f));
		foreach (var site in Colony.Sites)
			AddLabel(site, 34, () => Colony.Problems(site).Count > 0 ? $"▣ {site.Label} ⚠" : $"▣ {site.Label}", new Color(0.7f, 1f, 0.8f));
		foreach (var bot in Colony.Bots)
			AddLabel(bot.Grid, 26, () => $"{bot.Grid.Label}", new Color(1f, 0.75f, 0.6f));
		foreach (var swarm in Threats.Swarms)
			AddLabel(swarm, 30, () => "✦ Curator swarm", new Color(0.85f, 0.75f, 1f));
		foreach (var job in Colony.Jobs.Where(j => j.Hologram is not null))
			AddLabel(job.Hologram!, 32, () => $"{job.Name}  {job.Progress:P0}", Palette.Lemon);
	}

	/// <summary>A dusky veil over a body the Nexus can't see into.</summary>
	private void AddVeil(VoxelBody body)
	{
		float r = body.OuterRadius * 1.06f;
		var veil = new MeshInstance3D
		{
			Mesh = new SphereMesh { Radius = r, Height = r * 2f, RadialSegments = 48, Rings = 24 },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.16f, 0.12f, 0.24f, 0.62f),
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		body.AddChild(veil);
		_overlays.Add(veil);
	}

	private void ClearOverlays()
	{
		foreach (var node in _overlays)
			node.QueueFree();
		_overlays.Clear();
		foreach (var (_, line, _) in _flows)
			line.QueueFree();
		_flows.Clear();
	}

	/// <summary>
	/// A glowing tube with travelling dashes for every running route, from source to destination, so
	/// the colony's traffic reads at a glance. Busy routes (a bot on them) pulse brighter.
	/// </summary>
	private void UpdateFlows()
	{
		var wanted = Colony.Routes.Where(r => r.Enabled && Colony.Find(r.From) is not null && Colony.Find(r.To) is not null).ToList();
		for (int i = _flows.Count - 1; i >= 0; i--)
		{
			if (wanted.Contains(_flows[i].Route))
				continue;
			_flows[i].Line.QueueFree();
			_flows.RemoveAt(i);
		}
		foreach (var route in wanted.Where(r => _flows.All(f => f.Route != r)))
		{
			var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/flow_line.gdshader") };
			material.SetShaderParameter("tint", route.Cargo switch
			{
				HaulCargo.Ore => new Color(0.95f, 0.55f, 0.35f),
				HaulCargo.Ice => new Color(0.6f, 0.85f, 1f),
				HaulCargo.Everything => new Color(0.75f, 0.85f, 1f),
				_ => new Color(1f, 0.85f, 0.45f),
			});
			var line = new MeshInstance3D
			{
				Mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f, RadialSegments = 8, Rings = 1 },
				MaterialOverride = material,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};
			Colony.World.AddChild(line);
			_flows.Add((route, line, material));
		}
		foreach (var (route, line, material) in _flows)
		{
			Vector3 a = Colony.Find(route.From)!.GlobalPosition, b = Colony.Find(route.To)!.GlobalPosition;
			float length = a.DistanceTo(b);
			if (length < 0.1f)
				continue;
			Vector3 dir = (b - a) / length;
			// Cylinders run along Y; a thin tube whose width follows the zoom so it stays visible.
			float width = Mathf.Clamp(_distance * 0.004f, 0.25f, 3f);
			var basis = Basis.LookingAt(dir, Mathf.Abs(dir.Y) > 0.99f ? Vector3.Forward : Vector3.Up) * new Basis(Vector3.Right, -Mathf.Pi / 2f);
			line.GlobalTransform = new Transform3D(basis * Basis.FromScale(new Vector3(width, length, width)), (a + b) * 0.5f);
			material.SetShaderParameter("length", length);
			material.SetShaderParameter("busy", route.Busy > 0 ? 1f : 0f);
		}
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
		if (Talk.IsOpen || BoxMap.IsOpen || Stats.IsOpen || Covered())
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
