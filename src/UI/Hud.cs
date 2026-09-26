using System.Linq;
using Rebirth.Building;
using Rebirth.Characters;
using Rebirth.Items;
using Godot;

namespace Rebirth.UI;

public partial class Hud : CanvasLayer
{
	public Player Player { get; set; } = null!;

	private Label _status = null!;
	private Label _inventory = null!;
	private Label _message = null!;

	public override void _Ready()
	{
		_status = AddLabel(Control.LayoutPreset.TopLeft, HorizontalAlignment.Left, new Vector2(20, 20));
		_inventory = AddLabel(Control.LayoutPreset.TopRight, HorizontalAlignment.Right, new Vector2(-20, 20));
		_message = AddLabel(Control.LayoutPreset.CenterBottom, HorizontalAlignment.Center, new Vector2(0, -80));

		var crosshair = new Label { Text = "+", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
		crosshair.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		crosshair.AddThemeFontSizeOverride("font_size", 24);
		AddChild(crosshair);
	}

	private Label AddLabel(Control.LayoutPreset anchor, HorizontalAlignment align, Vector2 offset)
	{
		var label = new Label { HorizontalAlignment = align };
		label.AddThemeFontSizeOverride("font_size", 20);
		label.AddThemeColorOverride("font_color", UiTheme.HudText);
		label.AddThemeConstantOverride("outline_size", 7);
		label.AddThemeColorOverride("font_outline_color", UiTheme.HudOutline);
		label.SetAnchorsPreset(anchor);
		label.GrowHorizontal = align switch
		{
			HorizontalAlignment.Right => Control.GrowDirection.Begin,
			HorizontalAlignment.Center => Control.GrowDirection.Both,
			_ => Control.GrowDirection.End,
		};
		label.GrowVertical = anchor == Control.LayoutPreset.CenterBottom ? Control.GrowDirection.Begin : Control.GrowDirection.End;
		label.Position += offset;
		AddChild(label);
		return label;
	}

	public override void _Process(double delta)
	{
		_status.Text = Player.PilotedGrid is { } ship
			? ShipText(ship)
			: SuitText() + ToolbarText() + AimText();
		_inventory.Text = InventoryText("Inventory", Player.Inventory);
		_message.Text = Player.Message ?? (Player.Drill.InventoryFull && Player.Drill.Equipped ? "Inventory full - unload at a cargo container [F]" : "");
	}

	private string SuitText() =>
		$"Speed: {Player.LinearVelocity.Length(),6:0.0} m/s\n" +
		$"Jetpack [X]: {OnOff(Player.JetpackOn)}   Dampeners [Z]: {OnOff(Player.DampenersOn)}   Light [L]: {OnOff(Player.HelmetLight.Visible)}\n" +
		$"Mode [F2]: {(Player.Creative ? "Creative" : "Survival")}\n" +
		GravityText(Player.Gravity) +
		(Player.Walking ? $"Walking{(Player.Grounded ? "" : " (airborne)")}: WASD, Shift sprint, Space jump, X jetpack\n" : "") + "\n";

	private static string GravityText(Vector3 gravity) =>
		gravity.Length() > 0.05f ? $"Gravity: {gravity.Length() / 9.81f:0.00} g\n" : "";

	private string ShipText(BlockGrid ship)
	{
		Basis frame = ship.ControlFrame;
		string Thrust(Vector3 cockpitDir) => $"{ship.ThrustCapacity(BlockGrid.DominantAxis(frame * cockpitDir)) / 1000f:0}";
		return
			$"SHIP  Speed: {ship.LinearVelocity.Length(),6:0.0} m/s   Mass: {ship.Mass / 1000f:0.0} t\n" +
			$"Dampeners [Z]: {OnOff(Player.DampenersOn)}\n" +
			$"Thrust kN  fwd {Thrust(Vector3.Forward)}  back {Thrust(Vector3.Back)}  up {Thrust(Vector3.Up)}  " +
			$"down {Thrust(Vector3.Down)}  left {Thrust(Vector3.Left)}  right {Thrust(Vector3.Right)}\n" +
			$"Gyro torque: {ship.GyroTorque / 1e6f:0.0} MN·m\n" +
			GravityText(ship.GetGravity()) +
			PowerText(ship) +
			(ship.IsStatic ? "Station (static) - get out and press K on it to make it a ship\n" : "") +
			"[F] leave cockpit   [V] cockpit/chase view   hold [Alt] look around";
	}

	private static string PowerText(BlockGrid grid)
	{
		string text = $"Power: {grid.PowerDelivered:0.00} / {grid.PowerDemand:0.00} MW  (solar {grid.SolarProduction:0.00} MW)";
		if (grid.EnergyCapacity > 0f)
			text += $"   Battery: {100f * grid.StoredEnergy / grid.EnergyCapacity:0}%";
		if (grid.PowerSatisfaction < 0.999f)
			text += $"   LOW POWER ({100f * grid.PowerSatisfaction:0}%)";
		return text + "\n";
	}

	private string ToolbarText()
	{
		var equipped = Player.Equipped;
		var slots = Toolbar.Pages[Player.ToolbarPage].Select((item, i) =>
			item == equipped ? $"> [{Toolbar.KeyFor(i)}] {item.Name} <" : $"  [{Toolbar.KeyFor(i)}] {item.Name}");
		string text = $"Toolbar page {Player.ToolbarPage + 1}/{Toolbar.Pages.Count}  [Tab]\n" + string.Join("\n", slots) + "\n";
		if (equipped?.Block is { } block)
		{
			text += "LMB place   RMB remove   R/T rotate\n";
			if (!Player.Creative)
				text += "Cost: " + string.Join(", ", block.Cost.Select(kv =>
					$"{kv.Value:0} {ItemCatalog.DisplayName(kv.Key)}{(Player.Inventory.Get(kv.Key) < kv.Value ? " (missing!)" : "")}")) + "\n";
		}
		else if (equipped?.IsDrill == true)
			text += "Hold LMB to drill\n";
		else
			text += "Press a number to take an item; again to put it away\n" +
				"[B] Forge   [V] first/third person   hold [Alt] look around\n[F5] quicksave   [F9] quickload   [F8] new world\n";
		return text;
	}

	/// <summary>Details of the grid under the crosshair: what F does, power, and cargo.</summary>
	private string AimText()
	{
		if (Player.BuildTool.AimedGrid is not { } grid || !grid.TryGet(Player.BuildTool.AimedCell, out var block))
			return "";
		var def = block.Definition;
		string text = $"\n{def.DisplayName}  ({grid.Integrity(Player.BuildTool.AimedCell):0}/{def.MaxIntegrity:0})   " +
			$"{(grid.IsStatic ? "Station" : "Ship")} [K: toggle]\n";
		if (def.Kind == BlockKind.Cockpit)
			text += "[F] sit in cockpit\n";
		else if (def.Kind == BlockKind.Fabricator)
			text += $"[F] open fabricator   ({grid.FabricatorStatus(Player.BuildTool.AimedCell)})\n";
		else if (def.CargoCapacity > 0f)
			text += "[F] unload ore, take ingots\n";
		text += PowerText(grid);
		if (grid.Inventory.Capacity > 0f)
			text += InventoryText($"Grid cargo{(grid.Refining ? " - refining" : "")}", grid.Inventory) + "\n";
		return text;
	}

	private static string InventoryText(string title, Inventory inventory)
	{
		string header = $"{title}: {inventory.Total:0} / {inventory.Capacity:0} kg";
		// Refining trickles in fractions of a kilogram; hide the crumbs.
		var lines = inventory.Items.Where(kv => kv.Value >= 0.5f).OrderBy(kv => kv.Key).Select(kv => $"{ItemCatalog.DisplayName(kv.Key)}: {kv.Value:0} kg");
		return string.Join("\n", lines.Prepend(header));
	}

	private static string OnOff(bool value) => value ? "ON" : "OFF";
}
