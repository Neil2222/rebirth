using System;
using System.Linq;
using Godot;
using Rebirth.Building;
using Rebirth.Characters;
using Rebirth.Items;

namespace Rebirth.UI;

/// <summary>
/// What you carry, as a grid of item pictures, next to the storage you are looking at (if any).
/// Click an item to move all of it to the other side.
/// </summary>
public partial class InventoryPanel : CanvasLayer
{
	public event Action? Closed;
	public bool IsOpen => Visible;

	private Player _player = null!;
	private BlockGrid? _storage;
	private Label _carriedTitle = null!;
	private GridContainer _carried = null!;
	private Label _storageTitle = null!;
	private GridContainer _stored = null!;
	private Label _storageHint = null!;
	private string _shown = "";

	public override void _Ready()
	{
		Layer = 9;
		Visible = false;
		var root = new Control { Theme = UiTheme.Create(), MouseFilter = Control.MouseFilterEnum.Ignore };
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(root);
		var panel = new PanelContainer { CustomMinimumSize = new Vector2(860, 440) };
		root.AddChild(panel);
		panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center, Control.LayoutPresetMode.Minsize);
		panel.GrowHorizontal = Control.GrowDirection.Both;
		panel.GrowVertical = Control.GrowDirection.Both;
		var outer = new VBoxContainer();
		outer.AddThemeConstantOverride("separation", 10);
		panel.AddChild(outer);
		var title = new Label { Text = "INVENTORY" };
		title.AddThemeColorOverride("font_color", UiTheme.Accent);
		title.AddThemeFontSizeOverride("font_size", 22);
		outer.AddChild(title);

		var columns = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		columns.AddThemeConstantOverride("separation", 30);
		outer.AddChild(columns);
		var left = new VBoxContainer { CustomMinimumSize = new Vector2(400, 0) };
		_carriedTitle = UiTheme.Heading("");
		left.AddChild(_carriedTitle);
		_carried = new GridContainer { Columns = 5 };
		_carried.AddThemeConstantOverride("h_separation", 6);
		_carried.AddThemeConstantOverride("v_separation", 6);
		left.AddChild(_carried);
		columns.AddChild(left);
		var right = new VBoxContainer { CustomMinimumSize = new Vector2(400, 0) };
		_storageTitle = UiTheme.Heading("");
		right.AddChild(_storageTitle);
		_stored = new GridContainer { Columns = 5 };
		_stored.AddThemeConstantOverride("h_separation", 6);
		_stored.AddThemeConstantOverride("v_separation", 6);
		right.AddChild(_stored);
		_storageHint = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_storageHint.AddThemeColorOverride("font_color", UiTheme.Dim);
		right.AddChild(_storageHint);
		columns.AddChild(right);

		var bottom = new HBoxContainer();
		bottom.AddThemeConstantOverride("separation", 8);
		var hint = new Label { Text = "Click an item to move it across.", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		hint.AddThemeColorOverride("font_color", UiTheme.Dim);
		bottom.AddChild(hint);
		AddButton(bottom, "Unload all ore", () => MoveAll(ItemCategory.Ore, toStorage: true));
		AddButton(bottom, "Take all ingots", () => MoveAll(ItemCategory.Ingot, toStorage: false));
		AddButton(bottom, "Close  [I]", Close);
		outer.AddChild(bottom);
	}

	private static void AddButton(Container parent, string text, Action action)
	{
		var button = new Button { Text = text };
		button.Pressed += action;
		parent.AddChild(button);
	}

	/// <param name="storage">The grid whose storage you are looking at, or null.</param>
	public void Open(Player player, BlockGrid? storage)
	{
		_player = player;
		_storage = storage is { Inventory.Capacity: > 0f } ? storage : null;
		_shown = "";
		Visible = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	public void Close()
	{
		if (!Visible)
			return;
		Visible = false;
		Closed?.Invoke();
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (Visible && (e.IsActionPressed("open_inventory") || e.IsActionPressed("release_mouse")))
		{
			Close();
			GetViewport().SetInputAsHandled();
		}
	}

	public override void _Process(double delta)
	{
		if (!Visible)
			return;
		if (_storage is not null && (!IsInstanceValid(_storage) || _storage.IsQueuedForDeletion()))
			_storage = null;
		var pockets = _player.Inventory;
		string key = Key(pockets) + "|" + (_storage is null ? "" : Key(_storage.Inventory));
		if (key == _shown)
			return;
		_shown = key;
		_carriedTitle.Text = $"CARRIED   {pockets.Total:0} / {pockets.Capacity:0} kg";
		Fill(_carried, pockets, _storage?.Inventory);
		_storageTitle.Text = _storage is null ? "STORAGE" : $"{(_storage.Label ?? "STORAGE").ToUpperInvariant()}   {_storage.Inventory.Total:0} / {_storage.Inventory.Capacity:0} kg";
		if (_storage is null)
		{
			foreach (var child in _stored.GetChildren())
				child.QueueFree();
			_storageHint.Text = "Look at a cargo container and press I to trade with it.";
		}
		else
		{
			Fill(_stored, _storage.Inventory, pockets);
			_storageHint.Text = "";
		}
	}

	private static string Key(Inventory inventory) => string.Join(";", inventory.Items.Select(kv => $"{kv.Key}:{kv.Value:0}"));

	private static void Fill(GridContainer grid, Inventory from, Inventory? to)
	{
		foreach (var child in grid.GetChildren())
			child.QueueFree();
		foreach (var (item, amount) in from.Items.Where(kv => kv.Value >= 0.5f).OrderBy(kv => ItemCatalog.Get(kv.Key).Category).ThenBy(kv => kv.Key))
		{
			var slot = ItemSlot.Create(item, amount, 72f);
			slot.MouseFilter = Control.MouseFilterEnum.Stop;
			slot.TooltipText = $"{ItemCatalog.DisplayName(item)}  {amount:0} kg" + (to is null ? "" : "\nClick to move");
			if (to is not null)
				slot.GuiInput += e =>
				{
					if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
						from.TransferTo(to, item, from.Get(item));
				};
			grid.AddChild(slot);
		}
	}

	private void MoveAll(ItemCategory category, bool toStorage)
	{
		if (_storage is null)
			return;
		var (from, to) = toStorage ? (_player.Inventory, _storage.Inventory) : (_storage.Inventory, _player.Inventory);
		foreach (var (item, amount) in from.Items.Where(kv => ItemCatalog.Get(kv.Key).Category == category).ToArray())
			from.TransferTo(to, item, amount);
	}
}
