using System.Linq;
using Driftworks.Building;
using Driftworks.Characters;
using Godot;

namespace Driftworks.UI;

public partial class Hud : CanvasLayer
{
	public Player Player { get; set; } = null!;

	private Label _status = null!;

	public override void _Ready()
	{
		_status = new Label { Position = new Vector2(20, 20) };
		_status.AddThemeFontSizeOverride("font_size", 20);
		AddChild(_status);

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
		_status.Text =
			$"Speed: {Player.LinearVelocity.Length(),6:0.0} m/s\n" +
			$"Jetpack [X]: {(Player.JetpackOn ? "ON" : "OFF")}\n" +
			$"Dampeners [Z]: {(Player.DampenersOn ? "ON" : "OFF")}\n\n" +
			ToolbarText(Player.BuildTool.Selected);
	}

	private static string ToolbarText(BlockDefinition? selected)
	{
		var slots = BlockCatalog.Toolbar.Select((b, i) => b == selected ? $"> [{i + 1}] {b.DisplayName} <" : $"  [{i + 1}] {b.DisplayName}");
		string hand = selected is null ? "> [0] Empty hand <" : "  [0] Empty hand";
		string hint = selected is null ? "" : "\nLMB place   RMB remove";
		return string.Join("\n", slots.Append(hand)) + hint;
	}
}
