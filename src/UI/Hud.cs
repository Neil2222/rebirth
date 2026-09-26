using Godot;
using Driftworks.Characters;

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
			$"Dampeners [Z]: {(Player.DampenersOn ? "ON" : "OFF")}";
	}
}
