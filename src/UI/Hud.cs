using System.Linq;
using Driftworks.Building;
using Driftworks.Characters;
using Driftworks.Items;
using Godot;

namespace Driftworks.UI;

public partial class Hud : CanvasLayer
{
	public Player Player { get; set; } = null!;

	private Label _status = null!;
	private Label _inventory = null!;

	public override void _Ready()
	{
		_status = new Label { Position = new Vector2(20, 20) };
		_status.AddThemeFontSizeOverride("font_size", 20);
		AddChild(_status);

		_inventory = new Label { HorizontalAlignment = HorizontalAlignment.Right };
		_inventory.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		_inventory.GrowHorizontal = Control.GrowDirection.Begin;
		_inventory.Position -= new Vector2(20, -20);
		_inventory.AddThemeFontSizeOverride("font_size", 20);
		AddChild(_inventory);

		var crosshair = new Label
		{
			Text = "+",
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
		};
		crosshair.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		crosshair.AddThemeFontSizeOverride("font_size", 24);
		AddChild(crosshair);
	}

	public override void _Process(double delta)
	{
		_status.Text = Player.PilotedGrid is { } ship ? ShipText(ship) : SuitText() + ToolbarText(Player.Equipped);
		_inventory.Text = InventoryText(Player.Inventory);
	}

	private static string InventoryText(Inventory inventory)
	{
		if (inventory.Items.Count == 0)
			return "Inventory: empty";
		var lines = inventory.Items.OrderBy(kv => kv.Key).Select(kv => $"{ItemCatalog.DisplayName(kv.Key)}: {kv.Value:0} kg");
		return "Inventory\n" + string.Join("\n", lines);
	}

	private string SuitText() =>
		$"Speed: {Player.LinearVelocity.Length(),6:0.0} m/s\n" +
		$"Jetpack [X]: {(Player.JetpackOn ? "ON" : "OFF")}\n" +
		$"Dampeners [Z]: {(Player.DampenersOn ? "ON" : "OFF")}\n\n";

	private string ShipText(BlockGrid ship)
	{
		Basis frame = ship.ControlFrame;
		string Thrust(Vector3 cockpitDir) => $"{ship.ThrustCapacity(BlockGrid.DominantAxis(frame * cockpitDir)) / 1000f:0} kN";
		return
			$"SHIP  Speed: {ship.LinearVelocity.Length(),6:0.0} m/s   Mass: {ship.Mass / 1000f:0.0} t\n" +
			$"Dampeners [Z]: {(Player.DampenersOn ? "ON" : "OFF")}\n" +
			$"Thrust  fwd {Thrust(Vector3.Forward)}  back {Thrust(Vector3.Back)}  up {Thrust(Vector3.Up)}  down {Thrust(Vector3.Down)}  " +
			$"left {Thrust(Vector3.Left)}  right {Thrust(Vector3.Right)}\n" +
			$"Gyro torque: {ship.GyroTorque / 1e6f:0.0} MN·m\n" +
			(ship.IsStatic ? "Station (static) - get out and press K on it to make it a ship\n" : "") +
			"[F] leave cockpit";
	}

	private static string ToolbarText(ToolbarItem? equipped)
	{
		var slots = Toolbar.Slots.Select((item, i) => item == equipped ? $"> [{i + 1}] {item.Name} <" : $"  [{i + 1}] {item.Name}");
		string hand = equipped is null ? "> [0] Empty hand <" : "  [0] Empty hand";
		string hint = equipped switch
		{
			null => "\n[F] enter cockpit   [K] station <-> ship",
			{ IsDrill: true } => "\nHold LMB to drill",
			_ => "\nLMB place   RMB remove   R/T rotate",
		};
		return string.Join("\n", slots.Append(hand)) + hint;
	}
}
