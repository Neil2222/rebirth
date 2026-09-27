using System.Collections.Generic;
using System.Linq;
using Rebirth.Building;
using Rebirth.Characters;
using Rebirth.Items;
using Godot;

namespace Rebirth.UI;

/// <summary>
/// The in-world overlay, kept light: a few status badges top left, what you carry as icons top right,
/// a hotbar of block pictures at the bottom, and a small card about whatever you are looking at.
/// </summary>
public partial class Hud : CanvasLayer
{
	public Player Player { get; set; } = null!;

	private static readonly string[] PageNames = ["Build", "Machines", "Life"];

	private Label _speed = null!;
	private readonly Dictionary<string, Label> _badges = new();
	private Label _carried = null!;
	private HFlowContainer _pockets = null!;
	private string _pocketsShown = "";
	private Label _message = null!;

	private HBoxContainer _hotbar = null!;
	private readonly List<HotbarSlot> _slots = new();
	private int _hotbarPage = -1;
	private Label _pageLabel = null!;
	private Label _selectedName = null!;
	private HBoxContainer _costRow = null!;
	private string _costShown = "";
	private Control _bottom = null!;

	private PanelContainer _card = null!;
	private Label _cardTitle = null!;
	private Label _cardStatus = null!;
	private HBoxContainer _cardItems = null!;
	private string _cardItemsShown = "";
	private Label _cardAction = null!;

	private PanelContainer _shipCard = null!;
	private Label _shipText = null!;

	public override void _Ready()
	{
		BlockIcons.Attach(this);
		var root = new Control { Theme = UiTheme.Create(), MouseFilter = Control.MouseFilterEnum.Ignore };
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(root);

		// Top left: speed and toggles.
		var status = new VBoxContainer { Position = new Vector2(20, 16) };
		status.AddThemeConstantOverride("separation", 6);
		root.AddChild(status);
		_speed = OutlinedLabel(18);
		status.AddChild(_speed);
		var badges = new HBoxContainer();
		badges.AddThemeConstantOverride("separation", 6);
		status.AddChild(badges);
		foreach (var (id, text) in new[] { ("jetpack", "Jetpack  X"), ("dampeners", "Dampers  Z"), ("light", "Light  L"), ("creative", "Creative  F2") })
		{
			var badge = new Label { Text = text };
			badge.AddThemeFontSizeOverride("font_size", 13);
			badges.AddChild(badge);
			_badges[id] = badge;
		}

		// Top right: pockets.
		var pockets = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
		pockets.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
		pockets.OffsetLeft = -320;
		pockets.OffsetRight = -20;
		pockets.OffsetTop = 16;
		root.AddChild(pockets);
		_carried = OutlinedLabel(15);
		_carried.HorizontalAlignment = HorizontalAlignment.Right;
		pockets.AddChild(_carried);
		_pockets = new HFlowContainer { Alignment = FlowContainer.AlignmentMode.End };
		_pockets.AddThemeConstantOverride("h_separation", 4);
		_pockets.AddThemeConstantOverride("v_separation", 4);
		pockets.AddChild(_pockets);

		// Crosshair.
		var crosshair = new Label { Text = "+", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
		crosshair.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		crosshair.AddThemeFontSizeOverride("font_size", 24);
		crosshair.AddThemeColorOverride("font_color", UiTheme.HudText);
		crosshair.MouseFilter = Control.MouseFilterEnum.Ignore;
		root.AddChild(crosshair);

		// Card about what is under the crosshair, just below and right of it.
		_card = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		_card.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
		_card.OffsetLeft = 40;
		_card.OffsetTop = 30;
		_card.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(1f, 0.97f, 0.9f, 0.9f), UiTheme.PanelEdge, 2, 12));
		root.AddChild(_card);
		var cardBox = new VBoxContainer();
		cardBox.AddThemeConstantOverride("separation", 4);
		_card.AddChild(cardBox);
		_cardTitle = new Label();
		_cardTitle.AddThemeColorOverride("font_color", UiTheme.Accent);
		_cardTitle.AddThemeFontSizeOverride("font_size", 17);
		cardBox.AddChild(_cardTitle);
		_cardStatus = new Label();
		_cardStatus.AddThemeFontSizeOverride("font_size", 14);
		cardBox.AddChild(_cardStatus);
		_cardItems = new HBoxContainer();
		_cardItems.AddThemeConstantOverride("separation", 4);
		cardBox.AddChild(_cardItems);
		_cardAction = new Label();
		_cardAction.AddThemeFontSizeOverride("font_size", 14);
		_cardAction.AddThemeColorOverride("font_color", UiTheme.Dim);
		cardBox.AddChild(_cardAction);

		// Piloting: one small card instead.
		_shipCard = new PanelContainer { Position = new Vector2(20, 16), Visible = false };
		_shipCard.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(1f, 0.97f, 0.9f, 0.88f), UiTheme.PanelEdge, 2, 12));
		root.AddChild(_shipCard);
		_shipText = new Label();
		_shipText.AddThemeFontSizeOverride("font_size", 15);
		_shipCard.AddChild(_shipText);

		// Bottom: selected item, its cost, the hotbar.
		var bottom = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		bottom.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
		bottom.GrowHorizontal = Control.GrowDirection.Both;
		bottom.GrowVertical = Control.GrowDirection.Begin;
		bottom.OffsetBottom = -14;
		bottom.AddThemeConstantOverride("separation", 6);
		root.AddChild(bottom);
		_bottom = bottom;
		_message = OutlinedLabel(18);
		_message.HorizontalAlignment = HorizontalAlignment.Center;
		bottom.AddChild(_message);
		var selected = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		selected.AddThemeConstantOverride("separation", 10);
		bottom.AddChild(selected);
		_selectedName = OutlinedLabel(16);
		selected.AddChild(_selectedName);
		_costRow = new HBoxContainer();
		_costRow.AddThemeConstantOverride("separation", 3);
		selected.AddChild(_costRow);
		var bar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		bar.AddThemeConstantOverride("separation", 10);
		bottom.AddChild(bar);
		_pageLabel = OutlinedLabel(14);
		_pageLabel.VerticalAlignment = VerticalAlignment.Center;
		bar.AddChild(_pageLabel);
		_hotbar = new HBoxContainer();
		_hotbar.AddThemeConstantOverride("separation", 4);
		bar.AddChild(_hotbar);
		for (int i = 0; i < 10; i++)
		{
			var slot = new HotbarSlot { Key = Toolbar.KeyFor(i), CustomMinimumSize = new Vector2(62, 62) };
			_hotbar.AddChild(slot);
			_slots.Add(slot);
		}

		// Bottom right: the few keys worth remembering.
		var keys = OutlinedLabel(13);
		keys.Text = "N  Nexus    B  Forge    I  Inventory    Esc  Menu";
		keys.Modulate = new Color(1, 1, 1, 0.7f);
		keys.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight);
		keys.GrowHorizontal = Control.GrowDirection.Begin;
		keys.GrowVertical = Control.GrowDirection.Begin;
		keys.OffsetRight = -20;
		keys.OffsetBottom = -12;
		root.AddChild(keys);
	}

	private static Label OutlinedLabel(int size)
	{
		var label = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
		label.AddThemeFontSizeOverride("font_size", size);
		label.AddThemeColorOverride("font_color", UiTheme.HudText);
		label.AddThemeConstantOverride("outline_size", 7);
		label.AddThemeColorOverride("font_outline_color", UiTheme.HudOutline);
		return label;
	}

	public override void _Process(double delta)
	{
		bool piloting = Player.PilotedGrid is not null;
		_shipCard.Visible = piloting;
		_speed.GetParent<Control>().Visible = !piloting;
		_bottom.GetChild<Control>(1).Visible = !piloting;
		_bottom.GetChild<Control>(2).Visible = !piloting;
		if (Player.PilotedGrid is { } ship)
			_shipText.Text = ShipText(ship);
		else
		{
			UpdateStatus();
			UpdateHotbar();
		}
		UpdatePockets();
		UpdateCard();
		_message.Text = Player.Message ?? (Player.Drill.InventoryFull && Player.Drill.Equipped ? "Pockets full: unload at a cargo container [F]" : "");
	}

	// ------------------------------------------------------------ status

	private void UpdateStatus()
	{
		float g = Player.Gravity.Length() / 9.81f;
		_speed.Text = $"{Player.LinearVelocity.Length():0.0} m/s" + (g > 0.005f ? $"    {g:0.00} g" : "") +
			(Player.Walking ? (Player.Grounded ? "    walking" : "    airborne") : "");
		SetBadge("jetpack", Player.JetpackOn);
		SetBadge("dampeners", Player.DampenersOn);
		SetBadge("light", Player.HelmetLight.Visible);
		SetBadge("creative", Player.Creative);
		_badges["creative"].Visible = Player.Creative;
	}

	private void SetBadge(string id, bool on)
	{
		var badge = _badges[id];
		badge.AddThemeStyleboxOverride("normal", UiTheme.Box(on ? new Color(1f, 0.84f, 0.6f, 0.92f) : new Color(0.25f, 0.2f, 0.3f, 0.55f), on ? UiTheme.Accent : new Color(0, 0, 0, 0), 1, 8));
		badge.AddThemeColorOverride("font_color", on ? UiTheme.Text : new Color(1f, 0.95f, 0.9f, 0.7f));
	}

	// ------------------------------------------------------------ hotbar

	private void UpdateHotbar()
	{
		var page = Toolbar.Pages[Player.ToolbarPage];
		if (_hotbarPage != Player.ToolbarPage)
		{
			_hotbarPage = Player.ToolbarPage;
			for (int i = 0; i < _slots.Count; i++)
			{
				var item = i < page.Count ? page[i] : null;
				_slots[i].Item = item;
				_slots[i].TooltipText = item?.Name ?? "";
			}
			_pageLabel.Text = $"{PageNames[Player.ToolbarPage % PageNames.Length]}\n{Player.ToolbarPage + 1}/{Toolbar.Pages.Count}  Tab";
		}
		foreach (var slot in _slots)
			slot.Selected = slot.Item is not null && slot.Item == Player.Equipped;

		var equipped = Player.Equipped;
		_selectedName.Text = equipped switch
		{
			null => "",
			{ IsDrill: true } => "Hand Drill   hold left mouse to drill",
			{ Block: { } block } => $"{block.DisplayName}   LMB place · RMB remove · R/T turn",
			_ => equipped.Name,
		};
		string key = equipped?.Block is { } b && !Player.Creative ? b.Id + string.Join(",", b.Cost.Select(kv => Player.Inventory.Get(kv.Key) >= kv.Value)) : "";
		if (key != _costShown)
		{
			_costShown = key;
			foreach (var child in _costRow.GetChildren())
				child.QueueFree();
			if (equipped?.Block is { } costly && !Player.Creative)
				foreach (var (item, amount) in costly.Cost)
				{
					var chip = ItemSlot.Create(item, amount, 40f);
					chip.Warn = Player.Inventory.Get(item) < amount;
					_costRow.AddChild(chip);
				}
		}
	}

	// ------------------------------------------------------------ pockets

	private void UpdatePockets()
	{
		var inventory = Player.Inventory;
		_carried.Text = inventory.Total > 0.5f ? $"Carrying {inventory.Total:0} / {inventory.Capacity:0} kg" : "";
		string key = string.Join(";", inventory.Items.Where(kv => kv.Value >= 0.5f).OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value:0}"));
		if (key == _pocketsShown)
			return;
		_pocketsShown = key;
		foreach (var child in _pockets.GetChildren())
			child.QueueFree();
		foreach (var (item, amount) in inventory.Items.Where(kv => kv.Value >= 0.5f).OrderBy(kv => ItemCatalog.Get(kv.Key).Category).ThenBy(kv => kv.Key))
			_pockets.AddChild(ItemSlot.Create(item, amount, 48f));
	}

	// ------------------------------------------------------------ aim card

	private void UpdateCard()
	{
		if (Player.PilotedGrid is not null || Player.BuildTool.AimedGrid is not { } grid || !grid.TryGet(Player.BuildTool.AimedCell, out var block))
		{
			_card.Visible = false;
			return;
		}
		_card.Visible = true;
		var cell = Player.BuildTool.AimedCell;
		var def = block.Definition;
		var state = grid.StateOf(cell);
		float health = grid.Integrity(cell) / def.MaxIntegrity;
		_cardTitle.Text = def.DisplayName + (health < 0.99f ? $"   {health:P0}" : "") + (grid.Label is { } label ? $"   · {label}" : "");

		string status = def.Kind switch
		{
			BlockKind.Fabricator => grid.FabricatorStatus(cell),
			BlockKind.Tube => $"{grid.ParcelCount} parcels moving",
			_ when state.Input is not null || state.Output is not null || def.Kind == BlockKind.Incubator => grid.MachineStatus(cell),
			_ => "",
		};
		if (def.Logistics && def.Kind != BlockKind.Tube && grid.NetworkSize(cell) <= 1 && def.Kind != BlockKind.CargoContainer)
			status += (status.Length > 0 ? "\n" : "") + "Not connected: needs tubes or storage touching it";
		if (grid.PowerDemand > 0.01f && grid.PowerSatisfaction < 0.9f)
			status += (status.Length > 0 ? "\n" : "") + $"Low power {grid.PowerSatisfaction:P0}";
		_cardStatus.Text = status;
		_cardStatus.Visible = status.Length > 0;

		// Contents: machine input → output, or the storage.
		var shown = new List<(string Item, float Amount)>();
		bool arrow = false;
		if (state.Input is not null || state.Output is not null)
		{
			foreach (var kv in state.Input?.Items ?? new Dictionary<string, float>())
				shown.Add((kv.Key, kv.Value));
			arrow = state.Input is not null && state.Output is not null;
			if (arrow)
				shown.Add(("→", 0f));
			foreach (var kv in state.Output?.Items ?? new Dictionary<string, float>())
				shown.Add((kv.Key, kv.Value));
		}
		else if (def.CargoCapacity > 0f)
			shown.AddRange(grid.Inventory.Items.Where(kv => kv.Value >= 0.5f).OrderByDescending(kv => kv.Value).Take(8).Select(kv => (kv.Key, kv.Value)));
		string key = string.Join(";", shown.Select(s => $"{s.Item}:{s.Amount:0}"));
		if (key != _cardItemsShown)
		{
			_cardItemsShown = key;
			foreach (var child in _cardItems.GetChildren())
				child.QueueFree();
			foreach (var (item, amount) in shown)
			{
				if (item == "→")
				{
					var arrowLabel = new Label { Text = "→", VerticalAlignment = VerticalAlignment.Center };
					arrowLabel.AddThemeFontSizeOverride("font_size", 22);
					_cardItems.AddChild(arrowLabel);
				}
				else if (amount >= 0.5f)
					_cardItems.AddChild(ItemSlot.Create(item, amount, 44f));
			}
		}
		_cardItems.Visible = _cardItems.GetChildCount() > 0;

		_cardAction.Text = def.Kind switch
		{
			BlockKind.Cockpit => "F  sit in cockpit",
			BlockKind.Fabricator => "F  open fabricator",
			BlockKind.Incubator => "F  visit the village",
			_ when def.CargoCapacity > 0f => "F  unload ore, take ingots    I  inventory",
			_ when state.Output is not null => "F  take output",
			_ => "",
		} + (grid.IsBot ? "" : $"{(def.CargoCapacity > 0f || state.Output is not null || def.Kind is BlockKind.Cockpit or BlockKind.Fabricator or BlockKind.Incubator ? "    " : "")}K  {(grid.IsStatic ? "station" : "ship")}");
	}

	private static string ShipText(BlockGrid ship)
	{
		string power = ship.PowerSatisfaction < 0.999f ? $"   ⚠ power {ship.PowerSatisfaction:P0}" : "";
		string battery = ship.EnergyCapacity > 0f ? $"   battery {ship.StoredEnergy / ship.EnergyCapacity:P0}" : "";
		float g = ship.GetGravity().Length() / 9.81f;
		return $"{ship.LinearVelocity.Length():0.0} m/s   {ship.Mass / 1000f:0.0} t{(g > 0.005f ? $"   {g:0.00} g" : "")}{battery}{power}\n" +
			(ship.IsStatic ? "Anchored as a station: get out and press K on it to fly\n" : "") +
			"F  leave    V  view    Z  dampers    Alt  look around";
	}
}

/// <summary>One hotbar square: a picture of the block (or the drill), its key, and a highlight when held.</summary>
public partial class HotbarSlot : Control
{
	private ToolbarItem? _item;
	private bool _selected;
	private TextureRect _picture = null!;

	public int Key { get; set; }

	public ToolbarItem? Item
	{
		get => _item;
		set
		{
			_item = value;
			_picture.Texture = value?.Block is { } block ? BlockIcons.For(block) : null;
			QueueRedraw();
		}
	}

	public bool Selected
	{
		get => _selected;
		set
		{
			if (_selected == value)
				return;
			_selected = value;
			QueueRedraw();
		}
	}

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		_picture = new TextureRect
		{
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		_picture.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_picture.OffsetLeft = _picture.OffsetTop = 4;
		_picture.OffsetRight = _picture.OffsetBottom = -4;
		AddChild(_picture);
	}

	public override void _Draw()
	{
		var rect = new Rect2(Vector2.Zero, Size);
		var face = _selected ? new Color(1f, 0.86f, 0.62f, 0.95f) : new Color(1f, 0.97f, 0.9f, _item is null ? 0.35f : 0.8f);
		DrawStyleBox(UiTheme.Box(face, _selected ? UiTheme.Accent : UiTheme.PanelEdge, _selected ? 3 : 2, 12), rect);
		if (_item is { IsDrill: true })
			Icons.DrawDrill(this, Size * 0.5f, Size.X * 0.36f);
		var font = ThemeDB.FallbackFont;
		DrawStringOutline(font, new Vector2(6, 17), Key.ToString(), HorizontalAlignment.Left, -1, 14, 4, new Color(1f, 0.97f, 0.9f));
		DrawString(font, new Vector2(6, 17), Key.ToString(), HorizontalAlignment.Left, -1, 14, UiTheme.Text);
	}
}
