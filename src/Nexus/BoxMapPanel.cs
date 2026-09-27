using System;
using System.Linq;
using Godot;
using Rebirth.Core;
using Rebirth.UI;

namespace Rebirth.Nexus;

/// <summary>
/// The Boxes you have reached and the Starlight the freed ones give off: travel between them, and
/// spend Starlight on upgrades that last for good (bot bay, cargo hold, faster bots, cheaper bot builds).
/// </summary>
public partial class BoxMapPanel : CanvasLayer
{
	public event Action? Closed;
	/// <summary>The player asked to travel to the Box with this index.</summary>
	public event Action<int>? TravelRequested;
	public bool IsOpen => Visible;

	private Label _starlight = null!;
	private VBoxContainer _boxes = null!;
	private VBoxContainer _upgrades = null!;
	private Label _carry = null!;
	private Label _message = null!;
	private int _armedBox = -1;

	public override void _Ready()
	{
		Layer = 14;
		Visible = false;
		var root = new Control { Theme = UiTheme.Create(), MouseFilter = Control.MouseFilterEnum.Ignore };
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(root);
		var dim = new ColorRect { Color = new Color(0.16f, 0.1f, 0.14f, 0.5f), MouseFilter = Control.MouseFilterEnum.Stop };
		dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		root.AddChild(dim);
		var panel = new PanelContainer { CustomMinimumSize = new Vector2(980, 560) };
		root.AddChild(panel);
		panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center, Control.LayoutPresetMode.Minsize);
		panel.GrowHorizontal = Control.GrowDirection.Both;
		panel.GrowVertical = Control.GrowDirection.Both;
		var outer = new VBoxContainer();
		outer.AddThemeConstantOverride("separation", 10);
		panel.AddChild(outer);

		var top = new HBoxContainer();
		var title = new Label { Text = "BOXES & STARLIGHT", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		title.AddThemeColorOverride("font_color", UiTheme.Accent);
		title.AddThemeFontSizeOverride("font_size", 22);
		top.AddChild(title);
		_starlight = new Label();
		_starlight.AddThemeFontSizeOverride("font_size", 18);
		top.AddChild(_starlight);
		outer.AddChild(top);

		var columns = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		columns.AddThemeConstantOverride("separation", 24);
		outer.AddChild(columns);

		var left = new VBoxContainer { CustomMinimumSize = new Vector2(430, 0) };
		left.AddChild(UiTheme.Heading("BOXES YOU HAVE REACHED"));
		_boxes = new VBoxContainer();
		_boxes.AddThemeConstantOverride("separation", 6);
		left.AddChild(_boxes);
		left.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });
		left.AddChild(UiTheme.Heading("TRAVELLING TAKES ALONG"));
		_carry = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_carry.AddThemeFontSizeOverride("font_size", 14);
		left.AddChild(_carry);
		columns.AddChild(left);

		var right = new VBoxContainer { CustomMinimumSize = new Vector2(480, 0) };
		right.AddChild(UiTheme.Heading("UPGRADES  (for good, in every Box)"));
		_upgrades = new VBoxContainer();
		_upgrades.AddThemeConstantOverride("separation", 6);
		right.AddChild(_upgrades);
		columns.AddChild(right);

		var bottom = new HBoxContainer();
		_message = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_message.AddThemeColorOverride("font_color", UiTheme.Accent);
		bottom.AddChild(_message);
		var close = new Button { Text = "Back" };
		close.Pressed += Close;
		bottom.AddChild(close);
		outer.AddChild(bottom);
	}

	public void Open()
	{
		_message.Text = "";
		_armedBox = -1;
		Rebuild();
		Visible = true;
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
		if (Visible && e.IsActionPressed("release_mouse"))
		{
			Close();
			GetViewport().SetInputAsHandled();
		}
	}

	public override void _Process(double delta)
	{
		if (!Visible)
			return;
		var campaign = Campaign.Active;
		_starlight.Text = $"✦ {campaign.Starlight:0} Starlight" + (campaign.StarlightPerMinute > 0.01f ? $"   (+{campaign.StarlightPerMinute:0.0}/min)" : "");
	}

	private void Rebuild()
	{
		var campaign = Campaign.Active;
		foreach (var child in _boxes.GetChildren())
			child.QueueFree();
		foreach (var box in campaign.Boxes.OrderBy(b => b.Index))
		{
			var row = new HBoxContainer();
			string state = box.Index == campaign.Current ? "you are here" : box.Freed ? $"freed, shines {box.Glow * Campaign.GlowShare:0.0}/min" : "not freed yet";
			var label = new Label { Text = $"{box.Name}\n{KindText(box.Kind)} · {state}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			label.AddThemeFontSizeOverride("font_size", 14);
			row.AddChild(label);
			var travel = new Button { Text = box.Index == campaign.Current ? "Here" : "Travel", Disabled = box.Index == campaign.Current };
			int index = box.Index;
			travel.Pressed += () =>
			{
				// A second click confirms: travelling packs up and reloads the world.
				if (_armedBox != index)
				{
					_armedBox = index;
					travel.Text = "Sure? Click again";
					return;
				}
				Close();
				TravelRequested?.Invoke(index);
			};
			row.AddChild(travel);
			_boxes.AddChild(row);
		}
		_carry.Text = $"Blueprints: all of them.\nIngots: up to {campaign.CargoCarried / 1000f:0.#} t from Home (cargo hold).\n" +
			$"Bots: up to {campaign.BotsCarried} of yours (bot bay).\nA new Box gives you a fresh Home with a drill" +
			(campaign.WelcomeBots > 0 ? $" and {campaign.WelcomeBots} waiting bot{(campaign.WelcomeBots > 1 ? "s" : "")}." : ".");

		foreach (var child in _upgrades.GetChildren())
			child.QueueFree();
		foreach (var upgrade in Campaign.Upgrades)
		{
			int level = campaign.Level(upgrade.Id);
			var row = new HBoxContainer();
			var label = new Label { Text = $"{upgrade.Name}  {new string('●', level)}{new string('○', upgrade.MaxLevel - level)}\n{upgrade.Effect}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
			label.AddThemeFontSizeOverride("font_size", 13);
			row.AddChild(label);
			bool maxed = level >= upgrade.MaxLevel;
			var buy = new Button { Text = maxed ? "Max" : $"✦ {campaign.CostOf(upgrade)}", Disabled = maxed || campaign.Starlight < campaign.CostOf(upgrade), CustomMinimumSize = new Vector2(90, 0) };
			buy.Pressed += () =>
			{
				_message.Text = campaign.Buy(upgrade) ? $"{upgrade.Name} upgraded." : "Not enough Starlight yet.";
				Rebuild();
			};
			row.AddChild(buy);
			_upgrades.AddChild(row);
		}
	}

	private static string KindText(BoxKind kind) => kind switch
	{
		BoxKind.First => "where it all began",
		BoxKind.Tide => "seas and reefs",
		BoxKind.Dim => "a faint star: solar power is weak",
		BoxKind.Frost => "ice everywhere, air is hard to come by",
		_ => "dry and warm: ice is rare",
	};
}
