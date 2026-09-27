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
			"This is Home: a refinery, storage and a fabricator on a small rock. Fly with W A S D, rise with Space, sink with C, look with the mouse.",
			() => Player.GlobalPosition.DistanceTo(_startPosition) > 4f || Now - _stepStarted > 10,
			Enter: () => _startPosition = Player.GlobalPosition),
		new("1 / 6   Mine some ore",
			"Take the Hand Drill with [1] and hold the left mouse button on the rock under Home until you carry 100 kg of ore.",
			() => OreCarried() >= 100f,
			() => $"Carrying {OreCarried():0} / 100 kg of ore"),
		new("2 / 6   Feed the refinery",
			"Fly to the purple Cargo Container and press [F] to unload. The refinery pulls ore out of storage by itself and turns it into ingots.",
			() => OreCarried() < 1f),
		new("3 / 6   Lay a tube",
			"Press [Tab] for toolbar page 2, take the Tube and place it on the glowing spot next to the refinery. Touching machines, tubes and storage form one network.",
			() => Home is { } home && home.Has(TubeCell) && home.SameNetwork(TubeCell, RefineryCell),
			Enter: () => ShowGhost(BlockCatalog.Tube, TubeCell, Basis.Identity)),
		new("4 / 6   Set an Auto Drill",
			"Take the Auto Drill and place it on the end of the tube. Turn it with [R] and [T] until its drill head points down at the rock.",
			DrillWorking,
			DrillHint,
			() => ShowGhost(BlockCatalog.AutoDrill, DrillCell, DrillDown)),
		new("5 / 6   Watch it flow",
			"Look at the tube: ore rides to the refinery as little pods, ingots go on to storage. Wait for 40 kg of fresh ingots in storage.",
			() => IngotsAtHome() - _ingotsAtStart >= 40f,
			() => $"Fresh ingots: {Mathf.Max(0f, IngotsAtHome() - _ingotsAtStart):0} / 40 kg",
			() => _ingotsAtStart = IngotsAtHome()),
		new("6 / 6   Machines that build machines",
			$"Open the Fabricator with [F], press \"Deposit my ingots\", select Worker Bot and queue it {BotsToPrint} times. Bots will do the building from now on.",
			() => Colony.Bots.Count >= BotsToPrint,
			() => $"Bots: {Colony.Bots.Count} / {BotsToPrint}"),
		new("The Nexus is online",
			"Press [N] for the Nexus: every planet and site at a glance. Pick a planet, choose a design and send your bots. Building this way costs more than by hand, but you only have to decide.",
			() => NexusOpened,
			Enter: () => NexusUnlocked?.Invoke()),
	];

	private void EnterStep(int index)
	{
		StepIndex = index;
		_stepStarted = Now;
		ClearGhost();
		var step = _steps[index];
		_title.Text = step.Title;
		_text.Text = step.Text;
		step.Enter?.Invoke();
		Visible = true;
	}

	private void Finish()
	{
		StepIndex = -1;
		ClearGhost();
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
		return home.MachineStatus(cell) == "No rock within reach" ? "The drill can't reach rock: turn it (R / T) so the head points down." : null;
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
			Text = $"{block.DisplayName} here",
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

	private void ClearGhost()
	{
		_marker?.QueueFree();
		_marker = null;
	}
}
