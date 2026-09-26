using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rebirth.Building;
using Rebirth.Persistence;
using Rebirth.UI;

namespace Rebirth.Forge;

/// <summary>
/// The Forge: a neon hangar in its own 3D world where designs are built block by block, with a
/// parts palette, live stats, paint, mirror symmetry, undo, and blueprint save/load/print.
/// The game world keeps running behind it.
/// </summary>
public partial class ForgeScreen : CanvasLayer
{
	private const float FloorY = -BlockGrid.CellSize * 2.5f;   // origin cell floats two blocks above the floor
	private const int MaxUndo = 100;
	private const float DragThreshold = 5f;                    // pixels before a right-click becomes an orbit

	/// <summary>Called with the design when the player presses Print; returns a message for the player.</summary>
	public Func<Blueprint, string>? Printer { get; set; }
	/// <summary>Called with the design when the player chooses to wear it as their robot body.</summary>
	public Action<Blueprint>? BodySetter { get; set; }
	public event Action? Closed;

	public BlockGrid Design { get; private set; } = null!;
	public string DesignName => _name.Text.Trim().Length > 0 ? _name.Text.Trim() : "Untitled";

	/// <summary>The design on the bench as a blueprint, or null when the bench is empty.</summary>
	public Blueprint? CurrentBlueprint() => Design.BlockCount == 0 ? null : Blueprint.FromGrid(Design, DesignName, kind: _kind);

	private enum Mode { Build, Paint }

	private SubViewport _viewport = null!;
	private Camera3D _camera = null!;
	private Node3D _hangar = null!;
	private MeshInstance3D _mirrorPlane = null!;

	// Orbit camera.
	private Vector3 _target;
	private float _yaw = 0.7f, _pitch = -0.45f, _distance = 22f;

	// Tool state.
	private Mode _mode = Mode.Build;
	private BlockDefinition _selected = BlockCatalog.LightArmor;
	private Color? _paint;                                     // null: each block type's own colour
	private Basis _orientation = Basis.Identity;
	private bool _mirror;
	private Node3D? _ghost;
	private readonly StandardMaterial3D _ghostMaterial = BlockVisuals.CreateGhostMaterial();
	private readonly Stack<Blueprint> _undo = new();

	// Hover result, refreshed every physics tick.
	private bool _hasPlacement;
	private Vector3I _placeCell;
	private Vector3I? _hoverCell;

	// Mouse gestures.
	private bool _rightDown, _middleDown, _dragged;
	private Vector2 _rightPressAt;
	private bool _clickLeft, _clickRight;

	// UI.
	private LineEdit _name = null!;
	private OptionButton _kindPicker = null!;
	private DesignKind _kind = DesignKind.Ship;
	private Label _stats = null!;
	private Label _toast = null!;
	private double _toastUntil;
	private readonly Dictionary<BlockDefinition, Button> _partButtons = new();
	private Button _buildButton = null!, _paintButton = null!;
	private PopupPanel _loadPopup = null!;
	private ItemList _loadList = null!;
	private IReadOnlyList<BlueprintLibrary.Entry> _loadEntries = [];

	public bool IsOpen => Visible;

	public override void _Ready()
	{
		Layer = 10;
		Visible = false;
		BuildHangar();
		BuildUi();
		SelectPart(BlockCatalog.LightArmor);
		UpdateStats();
	}

	public void Open()
	{
		Visible = true;
		_viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	public void Close()
	{
		Visible = false;
		_viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
		_loadPopup.Hide();
		Closed?.Invoke();
	}

	/// <summary>Replaces the current design (e.g. loaded from the library).</summary>
	public void LoadDesign(Blueprint blueprint)
	{
		PushUndo();
		Design.Clear();
		blueprint.BuildInto(Design);
		_name.Text = blueprint.Name;
		_kind = blueprint.Kind;
		_kindPicker.Select((int)blueprint.Kind);
		FocusCamera();
		UpdateStats();
	}

	// ---------------------------------------------------------------- hangar

	private void BuildHangar()
	{
		_viewport = new SubViewport
		{
			OwnWorld3D = true,
			Msaa3D = Viewport.Msaa.Msaa2X,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
		};
		var container = new SubViewportContainer { Stretch = true, MouseFilter = Control.MouseFilterEnum.Ignore };
		container.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		container.AddChild(_viewport);
		AddChild(container);

		_hangar = new Node3D { Name = "Hangar" };
		_viewport.AddChild(_hangar);

		var env = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Color,
			BackgroundColor = new Color(0.006f, 0.01f, 0.018f),
			AmbientLightSource = Godot.Environment.AmbientSource.Color,
			AmbientLightColor = new Color(0.12f, 0.14f, 0.2f),
			TonemapMode = Godot.Environment.ToneMapper.Aces,
			GlowEnabled = true,
			GlowIntensity = 0.9f,
			GlowHdrThreshold = 1.0f,
			GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Screen,
		};
		env.SetGlowLevel(0, 1f);
		env.SetGlowLevel(2, 1f);
		env.SetGlowLevel(4, 0.6f);
		_hangar.AddChild(new WorldEnvironment { Environment = env });

		var key = new DirectionalLight3D { LightEnergy = 1.1f, ShadowEnabled = true };
		_hangar.AddChild(key);
		key.LookAt(new Vector3(-0.5f, -1f, -0.7f), Vector3.Up);
		var rim = new DirectionalLight3D { LightEnergy = 0.35f, LightColor = new Color(0.4f, 0.7f, 1f) };
		_hangar.AddChild(rim);
		rim.LookAt(new Vector3(0.6f, -0.2f, 0.8f), Vector3.Up);

		var floorMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/hangar_floor.gdshader") };
		_hangar.AddChild(new MeshInstance3D
		{
			Mesh = new PlaneMesh { Size = new Vector2(160, 160), Material = floorMaterial },
			Position = new Vector3(0, FloorY, 0),
		});

		// Which way is forward: the Control Core looks towards -Z.
		var arrowMaterial = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.45f, 0.1f) };
		var arrow = new MeshInstance3D
		{
			Mesh = new PrismMesh { Size = new Vector3(2f, 3f, 0.05f), Material = arrowMaterial },
			Transform = new Transform3D(new Basis(Vector3.Right, -Mathf.Pi / 2f), new Vector3(0, FloorY + 0.03f, -16f)),
		};
		_hangar.AddChild(arrow);
		_hangar.AddChild(new Label3D
		{
			Text = "FRONT",
			FontSize = 96,
			Modulate = new Color(1f, 0.45f, 0.1f),
			Transform = new Transform3D(new Basis(Vector3.Right, -Mathf.Pi / 2f), new Vector3(0, FloorY + 0.03f, -19.5f)),
		});

		_mirrorPlane = new MeshInstance3D
		{
			Mesh = new PlaneMesh
			{
				Size = new Vector2(30, 16),
				Orientation = PlaneMesh.OrientationEnum.X,
				Material = new StandardMaterial3D
				{
					ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
					Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
					AlbedoColor = new Color(1f, 0.25f, 0.85f, 0.035f),
					CullMode = BaseMaterial3D.CullModeEnum.Disabled,
				},
			},
			Visible = false,
		};
		_hangar.AddChild(_mirrorPlane);

		Design = BlockGrid.Create(_hangar, Transform3D.Identity, isStatic: true, designMode: true);
		_camera = new Camera3D { Fov = 55f, Current = true, Far = 1000f };
		_hangar.AddChild(_camera);
		UpdateCamera();
	}

	private void UpdateCamera()
	{
		var basis = Basis.FromEuler(new Vector3(_pitch, _yaw, 0f));
		_camera.Position = _target + basis * new Vector3(0, 0, _distance);
		_camera.LookAt(_target, Vector3.Up);
	}

	private void FocusCamera()
	{
		if (Design.BlockCount == 0)
		{
			_target = Vector3.Zero;
		}
		else
		{
			var aabb = new Aabb(BlockGrid.CellCenter(Design.Blocks.First().Key), Vector3.Zero);
			foreach (var (cell, _) in Design.Blocks)
				aabb = aabb.Expand(BlockGrid.CellCenter(cell));
			_target = aabb.GetCenter();
			_distance = Mathf.Clamp(aabb.Size.Length() * 1.3f + 12f, 10f, 120f);
		}
		UpdateCamera();
	}

	// ---------------------------------------------------------------- UI

	private void BuildUi()
	{
		var root = new Control { Theme = NeonTheme.Create(), MouseFilter = Control.MouseFilterEnum.Ignore };
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(root);

		// Top bar.
		var top = Panel(root, Control.LayoutPreset.TopWide);
		var bar = new HBoxContainer();
		bar.AddThemeConstantOverride("separation", 8);
		top.AddChild(bar);
		var title = new Label { Text = "REBIRTH  //  FORGE", VerticalAlignment = VerticalAlignment.Center };
		title.AddThemeColorOverride("font_color", NeonTheme.Accent);
		title.AddThemeFontSizeOverride("font_size", 20);
		bar.AddChild(title);
		bar.AddChild(new Control { CustomMinimumSize = new Vector2(24, 0) });
		_name = new LineEdit { Text = "New Design", CustomMinimumSize = new Vector2(260, 0), PlaceholderText = "Design name" };
		bar.AddChild(_name);
		_kindPicker = new OptionButton { TooltipText = "Ship: printed free-flying. Station: printed anchored. Body: worn by you." };
		foreach (var kind in System.Enum.GetValues<DesignKind>())
			_kindPicker.AddItem(kind.ToString(), (int)kind);
		_kindPicker.ItemSelected += index => _kind = (DesignKind)(int)index;
		bar.AddChild(_kindPicker);
		AddButton(bar, "New", NewDesign);
		AddButton(bar, "Save", SaveDesign);
		AddButton(bar, "Load", ShowLoadDialog);
		AddButton(bar, "Print (creative)", PrintDesign);
		AddButton(bar, "Use as my body", WearDesign);
		bar.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
		AddButton(bar, "Close  [B]", Close);

		// Parts and tools, left.
		var left = Panel(root, Control.LayoutPreset.LeftWide, new Vector2(250, 0));
		left.OffsetTop = 64;
		left.OffsetBottom = -64;
		var parts = new VBoxContainer();
		parts.AddThemeConstantOverride("separation", 4);
		left.AddChild(parts);
		parts.AddChild(NeonTheme.Heading("PARTS"));
		var group = new ButtonGroup();
		foreach (var block in BlockCatalog.All)
		{
			var button = new Button { Text = "  " + block.DisplayName, ToggleMode = true, ButtonGroup = group, Alignment = HorizontalAlignment.Left };
			button.AddThemeColorOverride("font_color", block.Paint);
			button.Pressed += () => SelectPart(block);
			parts.AddChild(button);
			_partButtons[block] = button;
		}
		parts.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });
		parts.AddChild(NeonTheme.Heading("TOOL"));
		var modeGroup = new ButtonGroup();
		_buildButton = new Button { Text = "Build", ToggleMode = true, ButtonGroup = modeGroup, ButtonPressed = true };
		_buildButton.Pressed += () => SetMode(Mode.Build);
		_paintButton = new Button { Text = "Paint", ToggleMode = true, ButtonGroup = modeGroup };
		_paintButton.Pressed += () => SetMode(Mode.Paint);
		var modes = new HBoxContainer();
		modes.AddChild(_buildButton);
		modes.AddChild(_paintButton);
		parts.AddChild(modes);
		var mirror = new CheckButton { Text = "Mirror left/right" };
		mirror.Toggled += on =>
		{
			_mirror = on;
			_mirrorPlane.Visible = on;
		};
		parts.AddChild(mirror);

		// Stats, right.
		var right = Panel(root, Control.LayoutPreset.RightWide, new Vector2(320, 0));
		right.OffsetTop = 64;
		right.OffsetBottom = -64;
		var statsBox = new VBoxContainer();
		right.AddChild(statsBox);
		statsBox.AddChild(NeonTheme.Heading("DESIGN READOUT"));
		_stats = new Label();
		_stats.AddThemeFontSizeOverride("font_size", 14);
		statsBox.AddChild(_stats);

		// Paint palette and controls help, bottom.
		var bottom = Panel(root, Control.LayoutPreset.BottomWide);
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);
		bottom.AddChild(row);
		row.AddChild(NeonTheme.Heading("PAINT"));
		var defaultPaint = new Button { Text = "Default", ToggleMode = true, ButtonPressed = true };
		var paintGroup = new ButtonGroup();
		defaultPaint.ButtonGroup = paintGroup;
		defaultPaint.Pressed += () => SetPaint(null);
		row.AddChild(defaultPaint);
		foreach (var color in Neon.Palette)
		{
			var swatch = new Button { ToggleMode = true, ButtonGroup = paintGroup, CustomMinimumSize = new Vector2(34, 30) };
			swatch.AddThemeStyleboxOverride("normal", NeonTheme.Box(color * 0.55f, color));
			swatch.AddThemeStyleboxOverride("hover", NeonTheme.Box(color * 0.8f, Colors.White));
			swatch.AddThemeStyleboxOverride("pressed", NeonTheme.Box(color, Colors.White, 3));
			swatch.Pressed += () => SetPaint(color);
			row.AddChild(swatch);
		}
		row.AddChild(new Control { CustomMinimumSize = new Vector2(24, 0) });
		var help = new Label
		{
			Text = "LMB place/paint   RMB remove   RMB-drag orbit   MMB-drag pan   Wheel zoom   R/T rotate   F focus   Ctrl+Z undo",
			VerticalAlignment = VerticalAlignment.Center,
		};
		help.AddThemeColorOverride("font_color", NeonTheme.Dim);
		help.AddThemeFontSizeOverride("font_size", 13);
		row.AddChild(help);

		_toast = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_toast.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		_toast.GrowHorizontal = Control.GrowDirection.Both;
		_toast.GrowVertical = Control.GrowDirection.Begin;
		_toast.Position += new Vector2(0, -80);
		_toast.AddThemeFontSizeOverride("font_size", 20);
		_toast.AddThemeConstantOverride("outline_size", 6);
		_toast.AddThemeColorOverride("font_outline_color", Colors.Black);
		root.AddChild(_toast);

		BuildLoadDialog(root);
	}

	/// <summary>A panel docked to a screen edge, growing inwards from that edge.</summary>
	private static PanelContainer Panel(Control parent, Control.LayoutPreset preset, Vector2 minSize = default)
	{
		var panel = new PanelContainer { CustomMinimumSize = minSize };
		parent.AddChild(panel);
		panel.SetAnchorsAndOffsetsPreset(preset, Control.LayoutPresetMode.Minsize);
		if (preset == Control.LayoutPreset.RightWide)
			panel.GrowHorizontal = Control.GrowDirection.Begin;
		if (preset == Control.LayoutPreset.BottomWide)
			panel.GrowVertical = Control.GrowDirection.Begin;
		return panel;
	}

	private static void AddButton(Container parent, string text, Action action)
	{
		var button = new Button { Text = text };
		button.Pressed += action;
		parent.AddChild(button);
	}

	private void BuildLoadDialog(Control root)
	{
		_loadPopup = new PopupPanel { Size = new Vector2I(420, 440) };
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 8);
		_loadPopup.AddChild(box);
		box.AddChild(NeonTheme.Heading("LOAD DESIGN   (★ = preset)"));
		_loadList = new ItemList { CustomMinimumSize = new Vector2(400, 340) };
		_loadList.ItemActivated += index => LoadEntry((int)index);
		box.AddChild(_loadList);
		var buttons = new HBoxContainer();
		box.AddChild(buttons);
		AddButton(buttons, "Load", () =>
		{
			var selected = _loadList.GetSelectedItems();
			if (selected.Length > 0)
				LoadEntry(selected[0]);
		});
		AddButton(buttons, "Cancel", () => _loadPopup.Hide());
		root.AddChild(_loadPopup);
	}

	private void ShowLoadDialog()
	{
		_loadEntries = BlueprintLibrary.List();
		_loadList.Clear();
		foreach (var entry in _loadEntries)
			_loadList.AddItem((entry.IsPreset ? "★  " : "     ") + entry.Name);
		_loadPopup.PopupCentered();
	}

	private void LoadEntry(int index)
	{
		_loadPopup.Hide();
		if (BlueprintLibrary.Load(_loadEntries[index]) is { } blueprint)
		{
			LoadDesign(blueprint);
			Toast($"Loaded \"{blueprint.Name}\"");
		}
	}

	private void NewDesign()
	{
		PushUndo();
		Design.Clear();
		_name.Text = "New Design";
		FocusCamera();
		UpdateStats();
	}

	private void SaveDesign()
	{
		if (Design.BlockCount == 0)
		{
			Toast("Nothing to save yet");
			return;
		}
		string path = BlueprintLibrary.Save(CurrentBlueprint()!);
		Toast($"Saved \"{DesignName}\"  ({path})");
	}

	private void PrintDesign()
	{
		if (Design.BlockCount == 0)
			Toast("Nothing to print yet");
		else if (Printer is not null)
			Toast(Printer(CurrentBlueprint()!));
	}

	private void WearDesign()
	{
		if (Design.BlockCount == 0)
		{
			Toast("Nothing to wear yet");
			return;
		}
		BodySetter?.Invoke(CurrentBlueprint()!);
		Toast($"You are now \"{DesignName}\" (shown at 1.9 m tall; V switches first/third person)");
	}

	private void Toast(string message)
	{
		_toast.Text = message;
		_toastUntil = Time.GetTicksMsec() / 1000.0 + 3.0;
	}

	private void SelectPart(BlockDefinition block)
	{
		_selected = block;
		_partButtons[block].ButtonPressed = true;
		SetMode(Mode.Build);
	}

	private void SetMode(Mode mode)
	{
		_mode = mode;
		(_mode == Mode.Build ? _buildButton : _paintButton).ButtonPressed = true;
		RebuildGhost();
	}

	private void SetPaint(Color? paint)
	{
		_paint = paint;
		RebuildGhost();
	}

	private void RebuildGhost()
	{
		_ghost?.QueueFree();
		_ghost = _mode == Mode.Build
			? BlockVisuals.CreateGhost(_selected, _paint ?? _selected.Paint, _ghostMaterial)
			: new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One * BlockGrid.CellSize * 1.02f }, MaterialOverride = _ghostMaterial };
		_ghost.Visible = false;
		_hangar.AddChild(_ghost);
	}

	private void UpdateStats() => _stats.Text = ForgeStats.Describe(Design, DesignName);

	// ---------------------------------------------------------------- input & editing

	public override void _UnhandledInput(InputEvent e)
	{
		if (!Visible)
			return;
		switch (e)
		{
			case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }:
				_clickLeft = true;
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Right } right:
				if (right.Pressed)
				{
					_rightDown = true;
					_dragged = false;
					_rightPressAt = right.Position;
				}
				else
				{
					_rightDown = false;
					if (!_dragged)
						_clickRight = true;
				}
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Middle } middle:
				_middleDown = middle.Pressed;
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
				_distance = Mathf.Max(4f, _distance * 0.9f);
				UpdateCamera();
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
				_distance = Mathf.Min(200f, _distance * 1.1f);
				UpdateCamera();
				break;
			case InputEventMouseMotion motion:
				if (_rightDown && (_dragged || motion.Position.DistanceTo(_rightPressAt) > DragThreshold))
				{
					_dragged = true;
					_yaw -= motion.Relative.X * 0.008f;
					_pitch = Mathf.Clamp(_pitch - motion.Relative.Y * 0.008f, -1.5f, 1.5f);
					UpdateCamera();
				}
				else if (_middleDown)
				{
					float scale = _distance * 0.0015f;
					_target += (-_camera.GlobalBasis.X * motion.Relative.X + _camera.GlobalBasis.Y * motion.Relative.Y) * scale;
					UpdateCamera();
				}
				break;
			case InputEventKey { Pressed: true, Echo: false } key:
				HandleKey(key);
				break;
		}
	}

	private void HandleKey(InputEventKey key)
	{
		if (key.Keycode == Key.Z && key.CtrlPressed)
			Undo();
		else if (key.Keycode == Key.R)
			RotateGhost(_camera.GlobalBasis.Y);
		else if (key.Keycode == Key.T)
			RotateGhost(_camera.GlobalBasis.X);
		else if (key.Keycode == Key.F)
			FocusCamera();
		else if (key.Keycode is Key.B or Key.Escape)
			Close();
		else
			return;
		GetViewport().SetInputAsHandled();
	}

	private void RotateGhost(Vector3 worldAxis) =>
		_orientation = BlockGrid.RotateOrientation(_orientation, Design.GlobalBasis, worldAxis);

	public override void _Process(double delta)
	{
		if (!Visible)
			return;
		if (Time.GetTicksMsec() / 1000.0 > _toastUntil)
			_toast.Text = "";
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!Visible)
			return;
		UpdateHover();

		if (_clickLeft && _mode == Mode.Build && _hasPlacement)
			Place();
		else if (_clickLeft && _mode == Mode.Paint && _hoverCell is { } paintCell)
			Paint(paintCell);
		else if (_clickRight && _hoverCell is { } removeCell)
			Remove(removeCell);
		_clickLeft = _clickRight = false;

		if (_ghost is null)
			return;
		if (_mode == Mode.Build)
		{
			_ghost.Visible = _hasPlacement;
			_ghost.Transform = new Transform3D(_orientation, BlockGrid.CellCenter(_placeCell));
			_ghostMaterial.AlbedoColor = BlockVisuals.GhostValid;
		}
		else
		{
			_ghost.Visible = _hoverCell is not null;
			if (_hoverCell is { } cell)
				_ghost.Transform = new Transform3D(Basis.Identity, BlockGrid.CellCenter(cell));
			_ghostMaterial.AlbedoColor = new Color(_paint ?? Colors.White, 0.35f);
		}
	}

	/// <summary>Where the mouse points: the block under it, and the free cell against the face it hits.</summary>
	private void UpdateHover()
	{
		_hasPlacement = false;
		_hoverCell = null;
		// The sub-viewport fills the screen 1:1, so screen coordinates are viewport coordinates.
		Vector2 mouse = GetViewport().GetMousePosition();
		Vector3 from = _camera.ProjectRayOrigin(mouse);
		Vector3 to = from + _camera.ProjectRayNormal(mouse) * 1000f;
		var hit = _viewport.FindWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to));

		if (hit.Count > 0 && hit["collider"].AsGodotObject() == Design)
		{
			Vector3I normal = BlockGrid.DominantAxis(hit["normal"].AsVector3());
			Vector3 local = Design.ToLocal(hit["position"].AsVector3());
			var cell = BlockGrid.LocalToCell(local - (Vector3)normal * (BlockGrid.CellSize * 0.5f));
			_hoverCell = cell;
			_placeCell = cell + normal;
			_hasPlacement = !Design.Has(_placeCell);
		}
		else if (Design.BlockCount == 0)
		{
			_placeCell = Vector3I.Zero;
			_hasPlacement = true;
		}
	}

	private static Vector3I Mirror(Vector3I cell) => new(-cell.X, cell.Y, cell.Z);

	/// <summary>Reflection in the X=0 plane, conjugated so the result is again a proper rotation.</summary>
	private static Basis Mirror(Basis b)
	{
		var m = new Basis(new Vector3(-1, 0, 0), Vector3.Up, Vector3.Back);
		return m * b * m;
	}

	private void Place()
	{
		PushUndo();
		Design.TryAdd(_placeCell, _selected, _orientation, paint: _paint);
		if (_mirror && _placeCell.X != 0)
			Design.TryAdd(Mirror(_placeCell), _selected, Mirror(_orientation), paint: _paint);
		UpdateStats();
	}

	private void Paint(Vector3I cell)
	{
		PushUndo();
		foreach (var target in _mirror ? new[] { cell, Mirror(cell) } : [cell])
			if (Design.TryGet(target, out var block))
				Design.Repaint(target, _paint ?? block.Definition.Paint);
		UpdateStats();
	}

	private void Remove(Vector3I cell)
	{
		PushUndo();
		Design.Remove(cell);
		if (_mirror)
			Design.Remove(Mirror(cell));
		UpdateStats();
	}

	private void PushUndo()
	{
		_undo.Push(Blueprint.FromGrid(Design, DesignName, kind: _kind));
		if (_undo.Count > MaxUndo)
		{
			var keep = _undo.Take(MaxUndo).Reverse().ToList();
			_undo.Clear();
			foreach (var snapshot in keep)
				_undo.Push(snapshot);
		}
	}

	private void Undo()
	{
		if (_undo.Count == 0)
			return;
		var snapshot = _undo.Pop();
		Design.Clear();
		snapshot.BuildInto(Design);
		UpdateStats();
	}
}
