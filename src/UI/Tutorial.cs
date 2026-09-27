using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rebirth.Building;
using Rebirth.Characters;
using Rebirth.Core;
using Rebirth.Items;
using Rebirth.Nexus;
using Rebirth.Persistence;

namespace Rebirth.UI;

/// <summary>
/// The one time you build by hand: mine, feed the refinery, lay a tube, set an auto drill, and print
/// two worker bots. A card at the top says what to do; glowing ghosts show where blocks go. Once the
/// bots exist the Nexus comes online and from then on everything can be run from its menus.
/// </summary>
public partial class Tutorial : CanvasLayer
{
	/// <summary>Home cells the tutorial builds on (see <see cref="Presets.Outpost"/>).</summary>
	public static readonly Vector3I RefineryCell = new(0, 1, 0);
	public static readonly Vector3I TubeCell = new(0, 1, 1);
	public static readonly Vector3I DrillCell = new(0, 1, 2);
	/// <summary>Drill head (-Z) pointing down.</summary>
	public static readonly Basis DrillDown = new(Vector3.Right, -Mathf.Pi / 2f);
	public const int BotsToPrint = 2;

	private sealed record Step(string Title, string Text, Func<bool> Done, Func<string?>? Hint = null, Action? Enter = null);

	public Player Player { get; set; } = null!;
	public Colony Colony { get; set; } = null!;
	/// <summary>Current step; -1 when finished.</summary>
	public int StepIndex { get; private set; } = -1;
	public bool NexusOpened { get; set; }
	/// <summary>Raised once the bots exist: the Nexus is available from then on.</summary>
	public event Action? NexusUnlocked;

	private List<Step> _steps = null!;
	private PanelContainer _card = null!;
	private Label _title = null!;
	private Label _text = null!;
	private Label _hint = null!;
	private Node3D? _marker;
	private StandardMaterial3D _ghostMaterial = null!;
	private double _stepStarted;
	private Vector3 _startPosition;
	private float _ingotsAtStart;

	private BlockGrid? Home => Colony.Home;
	private static double Now => Time.GetTicksMsec() / 1000.0;

	public override void _Ready()
	{
		Layer = 5;
		var root = new Control { Theme = UiTheme.Create(), MouseFilter = Control.MouseFilterEnum.Ignore };
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(root);
		_card = new PanelContainer { CustomMinimumSize = new Vector2(620, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
		_card.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
		_card.GrowHorizontal = Control.GrowDirection.Both;
		_card.OffsetTop = 16;
		root.AddChild(_card);
		var box = new VBoxContainer();
		_card.AddChild(box);
		_title = new Label();
		_title.AddThemeColorOverride("font_color", UiTheme.Accent);
		_title.AddThemeFontSizeOverride("font_size", 19);
		box.AddChild(_title);
		_text = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(596, 0) };
		box.AddChild(_text);
		_hint = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(596, 0) };
		_hint.AddThemeColorOverride("font_color", UiTheme.Dim);
		_hint.AddThemeFontSizeOverride("font_size", 14);
		box.AddChild(_hint);

		_ghostMaterial = BlockVisuals.CreateGhostMaterial();
		_steps = BuildSteps();
		Visible = false;
	}

	public void Begin(int step)
	{
		if (step < 0 || step >= _steps.Count)
		{
			Finish();
			return;
		}
		EnterStep(step);
	}

	private List<Step> BuildSteps() =>
	[
		new("Welcome, Custodian",
			"This little base is Home: storage, a refinery and a fabricator on a rock. These cards tell you what to do next. Fly with {move_forward}{move_left}{move_back}{move_right}, rise with {move_up}, sink with {move_down}, look with the mouse.",
			() => Player.GlobalPosition.DistanceTo(_startPosition) > 4f || Now - _stepStarted > 12,
			Enter: () =>
			{
				_startPosition = Player.GlobalPosition;
				Point(() => Home?.GlobalPosition + Home?.GlobalBasis.Y * 5f, "Home");
			}),
		new("1 / 6   Mine some ore",
			"Press {slot_1} to take the Hand Drill. Fly to the glowing marker on the rock under Home and hold {primary_action} on the rock until you carry 100 kg of ore.",
			() => OreCarried() >= 100f,
			() => $"Carrying {OreCarried():0} / 100 kg of ore",
			() => Point(RockSpot, "Drill here")),
		new("2 / 6   Feed the refinery",
			"Fly to the purple Cargo Container (marked) and press {use} to unload your ore. The refinery takes ore from storage by itself and turns it into ingots.",
			() => OreCarried() < 1f,
			Enter: () => Point(() => HomeCell(new Vector3I(1, 1, 0), 2.2f), "Unload here  [{use}]")),
		new("3 / 6   Lay a tube",
			"Scroll to hotbar page 2 ({toolbar_page}), take the Tube and place it with {primary_action} on the glowing spot next to the refinery. Touching machines, tubes and storage form one network.",
			() => Home is { } home && home.Has(TubeCell) && home.SameNetwork(TubeCell, RefineryCell),
			Enter: () => ShowGhost(BlockCatalog.Tube, TubeCell, Basis.Identity)),
		new("4 / 6   Set an Auto Drill",
			"Take the Auto Drill and place it on the end of the tube. Turn it with {rotate_block_yaw} and {rotate_block_pitch} until its drill head points down at the rock, like the glowing example.",
			DrillWorking,
			DrillHint,
			() => ShowGhost(BlockCatalog.AutoDrill, DrillCell, DrillDown)),
		new("5 / 6   Watch it flow",
			"Look at the tube: ore rides to the refinery as little pods, ingots go on to storage. Wait for 40 kg of fresh ingots.",
			() => IngotsAtHome() - _ingotsAtStart >= 40f,
			() => $"Fresh ingots: {Mathf.Max(0f, IngotsAtHome() - _ingotsAtStart):0} / 40 kg",
			() =>
			{
				_ingotsAtStart = IngotsAtHome();
				Point(() => HomeCell(TubeCell, 2f), "Pods ride here");
			}),
		new("6 / 6   Machines that build machines",
			$"Go to the Fabricator (marked) and press {{use}}. Press \"Deposit my ingots\", select Worker Bot and queue it {BotsToPrint} times. Bots will do the building from now on.",
			() => Colony.Bots.Count >= BotsToPrint,
			() => $"Bots: {Colony.Bots.Count} / {BotsToPrint}",
			() => Point(() => HomeCell(new Vector3I(0, 1, -1), 2.2f), "Fabricator  [{use}]")),
		new("The Nexus is online",
			"Press {open_nexus} for the Nexus: every planet and site at a glance. Pick a planet, choose a design and send your bots. Building this way costs more than by hand, but you only have to decide.",
			() => NexusOpened,
			Enter: () => NexusUnlocked?.Invoke()),
	];

	/// <summary>World position above a Home cell.</summary>
	private Vector3? HomeCell(Vector3I cell, float above) =>
		Home is { } home ? home.GlobalTransform * BlockGrid.CellCenter(cell) + home.GlobalBasis.Y * above : null;

	/// <summary>A spot on the rock right in front of Home, easy to reach and drill.</summary>
	private Vector3? RockSpot()
	{
		if (Home is not { } home || Colony.Bodies.FirstOrDefault(b => b.Name == "Home Rock") is not { } rock)
			return null;
		Vector3 toward = home.GlobalPosition + home.GlobalBasis.Z * 7f - rock.GlobalPosition;
		return rock.SurfacePoint(toward) + (rock.SurfacePoint(toward) - rock.GlobalPosition).Normalized() * 1.2f;
	}

	private void EnterStep(int index)
	{
		StepIndex = index;
		_stepStarted = Now;
		ClearGhost();
		ClearPointer();
		var step = _steps[index];
		_title.Text = step.Title;
		_text.Text = Keybinds.Fill(step.Text);
		step.Enter?.Invoke();
		Visible = true;
	}

	private void Finish()
	{
		StepIndex = -1;
		ClearGhost();
		ClearPointer();
		Visible = false;
		NexusUnlocked?.Invoke();
	}

	public override void _Process(double delta)
	{
		if (StepIndex < 0)
			return;
		_card.Visible = !GameState.WorldInputBlocked;
		var step = _steps[StepIndex];
		_hint.Text = step.Hint?.Invoke() ?? "";
		if (_pointer is not null && _pointerAt?.Invoke() is { } at)
			// A gentle bob so it catches the eye.
			_pointer.GlobalPosition = at + Vector3.Up * (0.4f * Mathf.Sin((float)Now * 3f));
		if (_marker is not null && Home is { } home)
		{
			// Follow the home grid and breathe gently so it catches the eye.
			float pulse = 0.25f + 0.15f * Mathf.Sin((float)Now * 4f);
			_ghostMaterial.AlbedoColor = new Color(1f, 0.85f, 0.35f, pulse);
			_marker.GlobalTransform = home.GlobalTransform * _markerLocal;
		}
		if (step.Done())
		{
			if (StepIndex + 1 < _steps.Count)
				EnterStep(StepIndex + 1);
			else
				Finish();
		}
	}

	// ------------------------------------------------------------ conditions

	private float OreCarried() =>
		Player.Inventory.Items.Where(kv => ItemCatalog.Get(kv.Key).Category == ItemCategory.Ore).Sum(kv => kv.Value);

	private float IngotsAtHome() =>
		Home?.Inventory.Items.Where(kv => ItemCatalog.Get(kv.Key).Category == ItemCategory.Ingot).Sum(kv => kv.Value) ?? 0f;

	private IEnumerable<Vector3I> Drills() =>
		Home?.Blocks.Where(b => b.Value.Definition.Kind == BlockKind.AutoDrill).Select(b => b.Key) ?? [];

	private bool DrillWorking() =>
		Home is { } home && Drills().Any(cell => home.SameNetwork(cell, RefineryCell) && home.MachineStatus(cell) is var s && (s.StartsWith("Drilling") || s == "Output full"));

	private string? DrillHint()
	{
		if (Home is not { } home || !Drills().Any())
			return null;
		var cell = Drills().First();
		if (!home.SameNetwork(cell, RefineryCell))
			return "The drill must touch the tube, so it joins the refinery's network.";
		return home.MachineStatus(cell) == "No rock within reach" ? Keybinds.Fill("The drill can't reach rock: turn it ({rotate_block_yaw} / {rotate_block_pitch}) so the head points down.") : null;
	}

	// ------------------------------------------------------------ ghost marker

	private Transform3D _markerLocal;

	private void ShowGhost(BlockDefinition block, Vector3I cell, Basis orientation)
	{
		ClearGhost();
		if (Home is null)
			return;
		_marker = BlockVisuals.CreateGhost(block, block.Paint, _ghostMaterial);
		_markerLocal = new Transform3D(orientation, BlockGrid.CellCenter(cell));
		var label = new Label3D
		{
			Text = $"{block.DisplayName} here\n▼",
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			NoDepthTest = true,
			FixedSize = true,
			PixelSize = 0.0012f,
			FontSize = 28,
			OutlineSize = 8,
			Modulate = new Color(1f, 0.9f, 0.6f),
			// Keep the text upright above the block whichever way the block is turned.
			TopLevel = true,
		};
		_marker.AddChild(label);
		Home.GetParent().AddChild(_marker);
		_marker.GlobalTransform = Home.GlobalTransform * _markerLocal;
		label.GlobalPosition = _marker.GlobalPosition + Home.GlobalBasis.Y * 2.2f;
	}

	private Label3D? _pointer;
	private Func<Vector3?>? _pointerAt;

	/// <summary>A floating label with an arrow, following <paramref name="at"/>, pointing at where to go.</summary>
	private void Point(Func<Vector3?> at, string text)
	{
		ClearPointer();
		if (at() is not { } start)
			return;
		_pointerAt = at;
		_pointer = new Label3D
		{
			Text = Keybinds.Fill(text) + "\n▼",
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			NoDepthTest = true,
			FixedSize = true,
			PixelSize = 0.0012f,
			FontSize = 30,
			OutlineSize = 9,
			Modulate = new Color(1f, 0.88f, 0.5f),
			OutlineModulate = new Color(0.25f, 0.15f, 0.2f),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Bottom,
		};
		Colony.World.AddChild(_pointer);
		_pointer.GlobalPosition = start;
	}

	private void ClearPointer()
	{
		_pointer?.QueueFree();
		_pointer = null;
		_pointerAt = null;
	}

	private void ClearGhost()
	{
		_marker?.QueueFree();
		_marker = null;
	}
}
