using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rebirth.Items;
using Rebirth.UI;

namespace Rebirth.Nexus;

/// <summary>
/// Production statistics, Factorio style: per item how much is made and used per minute over the last
/// minute, ten minutes or hour, with a small graph, the stock across all storage, what building sites
/// still need, and whether supply keeps up.
/// </summary>
public partial class StatsPanel : CanvasLayer
{
	public event Action? Closed;
	public bool IsOpen => Visible;

	public Colony Colony { get; set; } = null!;

	private static readonly (string Name, float Seconds)[] Windows = [("1 min", 60f), ("10 min", 600f), ("1 hour", 3600f)];
	private float _window = 600f;
	private VBoxContainer _rows = null!;
	private Label _note = null!;
	private float _timer;

	public override void _Ready()
	{
		Layer = 14;
		Visible = false;
		var root = new Control { Theme = UiTheme.Create() };
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(root);
		var dim = new ColorRect { Color = new Color(0.16f, 0.1f, 0.14f, 0.5f), MouseFilter = Control.MouseFilterEnum.Stop };
		dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		root.AddChild(dim);
		var panel = new PanelContainer { CustomMinimumSize = new Vector2(1180, 620) };
		root.AddChild(panel);
		panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center, Control.LayoutPresetMode.Minsize);
		panel.GrowHorizontal = Control.GrowDirection.Both;
		panel.GrowVertical = Control.GrowDirection.Both;
		var outer = new VBoxContainer();
		outer.AddThemeConstantOverride("separation", 8);
		panel.AddChild(outer);

		var top = new HBoxContainer();
		top.AddThemeConstantOverride("separation", 8);
		var title = new Label { Text = "PRODUCTION", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		title.AddThemeColorOverride("font_color", UiTheme.Accent);
		title.AddThemeFontSizeOverride("font_size", 22);
		top.AddChild(title);
		var group = new ButtonGroup();
		foreach (var (name, seconds) in Windows)
		{
			var button = new Button { Text = name, ToggleMode = true, ButtonGroup = group, ButtonPressed = seconds == _window };
			button.Pressed += () =>
			{
				_window = seconds;
				Refresh();
			};
			top.AddChild(button);
		}
		var close = new Button { Text = "Back" };
		close.Pressed += Close;
		top.AddChild(close);
		outer.AddChild(top);

		var header = new HBoxContainer();
		foreach (var (text, width) in new[] { ("", 52f), ("Item", 150f), ("Made /min", 100f), ("Used /min", 100f), ("Net", 90f), ("Stock", 90f), ("Wanted", 90f), ("Made vs used", 240f), ("", 200f) })
		{
			var label = new Label { Text = text, CustomMinimumSize = new Vector2(width, 0) };
			label.AddThemeColorOverride("font_color", UiTheme.Dim);
			label.AddThemeFontSizeOverride("font_size", 13);
			header.AddChild(label);
		}
		outer.AddChild(header);
		var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		outer.AddChild(scroll);
		_rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_rows.AddThemeConstantOverride("separation", 4);
		scroll.AddChild(_rows);
		_note = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_note.AddThemeColorOverride("font_color", UiTheme.Dim);
		_note.AddThemeFontSizeOverride("font_size", 13);
		outer.AddChild(_note);
	}

	public void Open()
	{
		Refresh();
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
		_timer -= (float)delta;
		if (_timer <= 0f)
			Refresh();
	}

	private void Refresh()
	{
		_timer = 1f;
		foreach (var child in _rows.GetChildren())
			child.QueueFree();
		var stock = Colony.Stock();
		var demand = Colony.Demand();
		var items = ProductionStats.Items.Union(stock.Keys).Union(demand.Keys)
			.Where(i => ItemCatalog.Get(i) is not null)
			.OrderBy(i => ItemCatalog.Get(i).Category == ItemCategory.Ingot ? 0 : 1).ThenBy(i => i).ToList();
		foreach (string item in items)
			_rows.AddChild(Row(item, stock.GetValueOrDefault(item), demand.GetValueOrDefault(item)));
		double recorded = ProductionStats.Elapsed;
		_note.Text = (recorded < _window ? $"Recorded so far: {recorded / 60.0:0.0} min (since this world was loaded). " : "") +
			"Made: drills, refineries, gifts. Used: refineries, building (bots and by hand), fabricators, Life machines, requests. Wanted: what building sites and fabricators still need.";
	}

	private Control Row(string item, float stock, float wanted)
	{
		var (made, used) = ProductionStats.PerMinute(item, _window);
		float net = made - used;
		var row = new HBoxContainer();
		row.AddChild(ItemSlot.Create(item, 0f, 44f));
		row.AddChild(new Label { Text = ItemCatalog.DisplayName(item), CustomMinimumSize = new Vector2(158, 0), VerticalAlignment = VerticalAlignment.Center });
		row.AddChild(Value($"{made:0}", 100f, new Color(0.25f, 0.55f, 0.3f)));
		row.AddChild(Value($"{used:0}", 100f, new Color(0.8f, 0.4f, 0.15f)));
		row.AddChild(Value(net >= 0 ? $"+{net:0}" : $"{net:0}", 90f, net < -0.5f ? UiTheme.Warning : UiTheme.Text));
		row.AddChild(Value(Icons.Short(stock), 90f, UiTheme.Text));
		row.AddChild(Value(wanted > 0.5f ? Icons.Short(wanted) : "-", 90f, UiTheme.Text));
		row.AddChild(new Sparkline
		{
			Made = ProductionStats.Series(item, true, _window, 40),
			Used = ProductionStats.Series(item, false, _window, 40),
			CustomMinimumSize = new Vector2(230, 40),
		});
		var (text, color) = Verdict(made, used, stock, wanted);
		var verdict = new Label { Text = text, CustomMinimumSize = new Vector2(210, 0), VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		verdict.AddThemeColorOverride("font_color", color);
		verdict.AddThemeFontSizeOverride("font_size", 13);
		row.AddChild(verdict);
		return row;
	}

	private static Label Value(string text, float width, Color color)
	{
		var label = new Label { Text = text, CustomMinimumSize = new Vector2(width, 0), VerticalAlignment = VerticalAlignment.Center };
		label.AddThemeColorOverride("font_color", color);
		return label;
	}

	/// <summary>Does supply keep up? In plain words, coloured by how worried to be.</summary>
	private static (string, Color) Verdict(float made, float used, float stock, float wanted)
	{
		var red = UiTheme.Warning;
		var orange = new Color(0.85f, 0.5f, 0.1f);
		var green = new Color(0.25f, 0.55f, 0.3f);
		float net = made - used;
		if (wanted > stock + 0.5f)
			return made < 0.5f
				? ("Short: nothing is making it", red)
				: ($"Short: ~{(wanted - stock) / made:0} min to make what's wanted", orange);
		if (net < -0.5f && stock > 0.5f)
			return ($"Running low: gone in ~{stock / -net:0} min", stock / -net < 5f ? red : orange);
		if (net > 0.5f)
			return ("Piling up", green);
		if (made < 0.5f && used < 0.5f)
			return ("Idle", UiTheme.Dim);
		return ("Balanced", UiTheme.Text);
	}

	/// <summary>Made (green) and used (orange) per minute across the window, on a shared scale.</summary>
	private partial class Sparkline : Control
	{
		public float[] Made { get; set; } = [];
		public float[] Used { get; set; } = [];

		public override void _Draw()
		{
			DrawStyleBox(UiTheme.Box(new Color(1f, 0.98f, 0.94f), UiTheme.PanelEdge, 1, 6), new Rect2(Vector2.Zero, Size));
			float top = Mathf.Max(1f, Mathf.Max(Made.DefaultIfEmpty(0f).Max(), Used.DefaultIfEmpty(0f).Max()));
			Line(Used, top, new Color(0.9f, 0.5f, 0.2f));
			Line(Made, top, new Color(0.3f, 0.65f, 0.35f));
		}

		private void Line(float[] values, float top, Color color)
		{
			if (values.Length < 2)
				return;
			var points = new Vector2[values.Length];
			for (int i = 0; i < values.Length; i++)
				points[i] = new Vector2(4f + (Size.X - 8f) * i / (values.Length - 1), Size.Y - 4f - (Size.Y - 8f) * values[i] / top);
			DrawPolyline(points, color, 2f, true);
		}
	}
}
