using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Rebirth.Building;
using Rebirth.Characters;
using Rebirth.Items;
using Rebirth.Persistence;

namespace Rebirth.UI;

/// <summary>
/// Opened with F on a fabricator: pick a design, see what it costs against the grid's cargo,
/// queue prints, and hand over the ingots you are carrying.
/// </summary>
public partial class FabricatorPanel : CanvasLayer
{
	public event Action? Closed;
	public bool IsOpen => Visible;

	private BlockGrid? _grid;
	private Vector3I _cell;
	private Player _player = null!;
	private List<Blueprint> _designs = new();

	private ItemList _list = null!;
	private Label _details = null!;
	private Label _queue = null!;
	private Label _cargo = null!;
	private Label _message = null!;

	public override void _Ready()
	{
		Layer = 9;
		Visible = false;

		var root = new Control { Theme = NeonTheme.Create() };
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		root.MouseFilter = Control.MouseFilterEnum.Ignore;
		AddChild(root);

		var panel = new PanelContainer { CustomMinimumSize = new Vector2(1040, 560) };
		root.AddChild(panel);
		panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center, Control.LayoutPresetMode.Minsize);
		panel.GrowHorizontal = Control.GrowDirection.Both;
		panel.GrowVertical = Control.GrowDirection.Both;

		var outer = new VBoxContainer();
		outer.AddThemeConstantOverride("separation", 10);
		panel.AddChild(outer);
		var title = new Label { Text = "FABRICATOR" };
		title.AddThemeColorOverride("font_color", Neon.Violet);
		title.AddThemeFontSizeOverride("font_size", 22);
		outer.AddChild(title);

		var columns = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		columns.AddThemeConstantOverride("separation", 16);
		outer.AddChild(columns);

		var left = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
		left.AddChild(NeonTheme.Heading("DESIGNS  (★ = preset)"));
		_list = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		_list.ItemSelected += _ => UpdateDetails();
		_list.ItemActivated += _ => QueueSelected();
		left.AddChild(_list);
		columns.AddChild(left);

		var middle = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
		middle.AddChild(NeonTheme.Heading("SELECTED"));
		_details = new Label { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		middle.AddChild(_details);
		var buttons = new HBoxContainer();
		buttons.AddThemeConstantOverride("separation", 8);
		AddButton(buttons, "Queue print", QueueSelected);
		AddButton(buttons, "Cancel last", () => Say(_grid!.CancelLastPrint(_cell) ? "Cancelled the last queued print" : "Nothing waiting to cancel"));
		middle.AddChild(buttons);
		columns.AddChild(middle);

		var right = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
		right.AddChild(NeonTheme.Heading("QUEUE"));
		_queue = new Label();
		right.AddChild(_queue);
		right.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });
		right.AddChild(NeonTheme.Heading("GRID CARGO"));
		_cargo = new Label { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		right.AddChild(_cargo);
		var deposit = new HBoxContainer();
		AddButton(deposit, "Deposit my ingots", DepositIngots);
		right.AddChild(deposit);
		columns.AddChild(right);

		var bottom = new HBoxContainer();
		_message = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_message.AddThemeColorOverride("font_color", NeonTheme.Accent);
		bottom.AddChild(_message);
		AddButton(bottom, "Close  [F]", Close);
		outer.AddChild(bottom);
	}

	private static void AddButton(Container parent, string text, Action action)
	{
		var button = new Button { Text = text };
		button.Pressed += action;
		parent.AddChild(button);
	}

	public void Open(BlockGrid grid, Vector3I cell, Player player)
	{
		_grid = grid;
		_cell = cell;
		_player = player;
		_designs = BlueprintLibrary.List()
			.Select(entry => (entry, design: BlueprintLibrary.Load(entry)))
			.Where(x => x.design is { Kind: not DesignKind.Body })
			.Select(x => x.design!)
			.ToList();
		var presets = Presets.All.Select(p => p.Name).ToHashSet();
		_list.Clear();
		foreach (var design in _designs)
			_list.AddItem((presets.Contains(design.Name) ? "★  " : "     ") + design.Name + (design.Kind == DesignKind.Station ? "  (station)" : ""));
		if (_designs.Count > 0)
			_list.Select(0);
		_message.Text = "";
		UpdateDetails();
		Visible = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	public void Close()
	{
		if (!Visible)
			return;
		Visible = false;
		_grid = null;
		Closed?.Invoke();
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (!Visible)
			return;
		if (e.IsActionPressed("use") || e.IsActionPressed("release_mouse"))
		{
			Close();
			GetViewport().SetInputAsHandled();
		}
	}

	public override void _Process(double delta)
	{
		if (!Visible)
			return;
		if (_grid is null || !IsInstanceValid(_grid) || _grid.IsQueuedForDeletion() || !_grid.Has(_cell))
		{
			Close();
			return;
		}
		UpdateDetails();
		UpdateQueue();
	}

	private Blueprint? Selected =>
		_list.GetSelectedItems() is { Length: > 0 } selected && selected[0] < _designs.Count ? _designs[selected[0]] : null;

	private void UpdateDetails()
	{
		if (Selected is not { } design || _grid is null)
		{
			_details.Text = "No designs yet. Make one in the Forge [B].";
			return;
		}
		float mass = design.TotalMass();
		float seconds = Mathf.Max(BlockGrid.MinPrintSeconds, mass / 1000f * BlockGrid.PrintSecondsPerTonne);
		var sb = new StringBuilder();
		sb.Append($"{design.Name}\n{design.Kind}, {design.Blocks.Count} blocks, {mass / 1000f:0.0} t\n");
		sb.Append($"Print time  {seconds:0} s at full power\n\nCOST        needed    in cargo\n");
		foreach (var (item, amount) in design.TotalCost().OrderBy(kv => kv.Key))
		{
			float have = _grid.Inventory.Get(item);
			sb.Append($"{ItemCatalog.DisplayName(item),-14}{amount,6:0}   {have,6:0}{(have >= amount ? "" : "  missing")}\n");
		}
		_details.Text = sb.ToString();
	}

	private void UpdateQueue()
	{
		var queue = _grid!.FabricatorQueue(_cell);
		var sb = new StringBuilder($"Status: {_grid.FabricatorStatus(_cell)}\n");
		sb.Append($"Power: {_grid.PowerDelivered:0.00} / {_grid.PowerDemand:0.00} MW\n\n");
		for (int i = 0; i < queue.Count; i++)
			sb.Append(i == 0 && queue[0].Paid ? $"> {queue[i].Design.Name}  {queue[i].Progress:P0}\n" : $"  {queue[i].Design.Name}\n");
		if (queue.Count == 0)
			sb.Append("  (empty)\n");
		_queue.Text = sb.ToString();

		var cargo = _grid.Inventory;
		_cargo.Text = $"{cargo.Total:0} / {cargo.Capacity:0} kg\n" + string.Join("\n",
			cargo.Items.Where(kv => kv.Value >= 0.5f).OrderBy(kv => kv.Key).Select(kv => $"{ItemCatalog.DisplayName(kv.Key),-14}{kv.Value,7:0} kg"));
	}

	private void QueueSelected()
	{
		if (Selected is not { } design || _grid is null)
			return;
		_grid.EnqueuePrint(_cell, design);
		Say($"Queued \"{design.Name}\"");
	}

	private void DepositIngots()
	{
		float moved = 0f;
		foreach (var (id, amount) in _player.Inventory.Items.ToArray())
			if (ItemCatalog.Get(id).Category == ItemCategory.Ingot)
				moved += _player.Inventory.TransferTo(_grid!.Inventory, id, amount);
		Say(moved > 0f ? $"Deposited {moved:0} kg of ingots" : "You carry no ingots (or the cargo is full)");
	}

	private void Say(string message) => _message.Text = message;
}
