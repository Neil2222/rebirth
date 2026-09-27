using System;
using Godot;
using Rebirth.Core;

namespace Rebirth.UI;

/// <summary>Graphics options: a preset for quick choices, and each setting on its own. Changes apply at once.</summary>
public partial class GraphicsPanel : CanvasLayer
{
	public event Action? Closed;
	/// <summary>Something changed: re-apply to the world.</summary>
	public event Action? Changed;
	public bool IsOpen => Visible;

	private Action? _back;
	private OptionButton _preset = null!;
	private HSlider _scale = null!;
	private Label _scaleLabel = null!;
	private CheckButton _shadows = null!, _ssao = null!, _glow = null!, _vsync = null!, _fullscreen = null!;
	private OptionButton _aa = null!, _cap = null!;
	private bool _updating;
	private static readonly int[] Caps = [0, 30, 60, 120, 144];

	public override void _Ready()
	{
		Layer = 22;
		Visible = false;
		ProcessMode = ProcessModeEnum.Always;
		var root = new Control { Theme = UiTheme.Create() };
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(root);
		var dim = new ColorRect { Color = new Color(0.16f, 0.1f, 0.14f, 0.5f), MouseFilter = Control.MouseFilterEnum.Stop };
		dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		root.AddChild(dim);
		var panel = new PanelContainer { CustomMinimumSize = new Vector2(560, 0) };
		root.AddChild(panel);
		panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center, Control.LayoutPresetMode.Minsize);
		panel.GrowHorizontal = Control.GrowDirection.Both;
		panel.GrowVertical = Control.GrowDirection.Both;
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 8);
		panel.AddChild(box);
		var title = new Label { Text = "GRAPHICS" };
		title.AddThemeColorOverride("font_color", UiTheme.Accent);
		title.AddThemeFontSizeOverride("font_size", 24);
		box.AddChild(title);

		_preset = new OptionButton();
		foreach (var name in new[] { "Low  (laptops, older PCs)", "Medium", "High", "Custom" })
			_preset.AddItem(name);
		_preset.ItemSelected += index =>
		{
			if (index == (int)GraphicsPreset.Custom)
				return;
			GraphicsSettings.Current.UsePreset((GraphicsPreset)(int)index);
			Commit();
		};
		Row(box, "Quality", _preset);

		_scale = new HSlider { MinValue = 0.5, MaxValue = 1.0, Step = 0.05, CustomMinimumSize = new Vector2(200, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
		_scaleLabel = new Label { CustomMinimumSize = new Vector2(50, 0) };
		_scale.ValueChanged += value => Custom(s => s.RenderScale = (float)value);
		var scaleRow = new HBoxContainer();
		scaleRow.AddChild(_scale);
		scaleRow.AddChild(_scaleLabel);
		Row(box, "Render scale", scaleRow);

		_shadows = Toggle(box, "Shadows", on => Custom(s => s.Shadows = on));
		_ssao = Toggle(box, "Soft contact shadows (SSAO)", on => Custom(s => s.Ssao = on));
		_glow = Toggle(box, "Glow on lamps and flames", on => Custom(s => s.Glow = on));
		_aa = new OptionButton();
		foreach (var name in new[] { "Off", "2×", "4×" })
			_aa.AddItem(name);
		_aa.ItemSelected += index => Custom(s => s.AntiAliasing = (int)index);
		Row(box, "Smooth edges (MSAA)", _aa);

		box.AddChild(new HSeparator());
		_vsync = Toggle(box, "VSync", on => Commit(s => s.VSync = on));
		_cap = new OptionButton();
		foreach (int cap in Caps)
			_cap.AddItem(cap == 0 ? "No limit" : $"{cap} fps");
		_cap.ItemSelected += index => Commit(s => s.FpsCap = Caps[index]);
		Row(box, "Frame limit", _cap);
		_fullscreen = Toggle(box, "Fullscreen", on => Commit(s => s.Fullscreen = on));

		var hint = new Label { Text = "Slow computer? Choose Low, or lower the render scale.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
		hint.AddThemeColorOverride("font_color", UiTheme.Dim);
		box.AddChild(hint);
		var back = new Button { Text = "Back", SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd };
		back.Pressed += Back;
		box.AddChild(back);
	}

	private static void Row(VBoxContainer box, string name, Control control)
	{
		var row = new HBoxContainer();
		row.AddChild(new Label { Text = name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
		row.AddChild(control);
		box.AddChild(row);
	}

	private CheckButton Toggle(VBoxContainer box, string name, Action<bool> changed)
	{
		var toggle = new CheckButton { Text = name };
		toggle.Toggled += on => changed(on);
		box.AddChild(toggle);
		return toggle;
	}

	/// <summary>A quality setting changed by hand: the preset becomes Custom.</summary>
	private void Custom(Action<GraphicsSettings> change)
	{
		if (_updating)
			return;
		change(GraphicsSettings.Current);
		GraphicsSettings.Current.Preset = GraphicsPreset.Custom;
		Commit();
	}

	private void Commit(Action<GraphicsSettings>? change = null)
	{
		if (_updating)
			return;
		change?.Invoke(GraphicsSettings.Current);
		GraphicsSettings.Current.Save();
		Changed?.Invoke();
		Refresh();
	}

	private void Refresh()
	{
		var s = GraphicsSettings.Current;
		_updating = true;
		_preset.Select((int)s.Preset);
		_scale.Value = s.RenderScale;
		_scaleLabel.Text = $"{s.RenderScale:P0}";
		_shadows.ButtonPressed = s.Shadows;
		_ssao.ButtonPressed = s.Ssao;
		_glow.ButtonPressed = s.Glow;
		_aa.Select(s.AntiAliasing);
		_vsync.ButtonPressed = s.VSync;
		_cap.Select(Mathf.Max(0, Array.IndexOf(Caps, s.FpsCap)));
		_fullscreen.ButtonPressed = s.Fullscreen;
		_updating = false;
	}

	public void Open(Action back)
	{
		_back = back;
		Refresh();
		GameState.WorldInputBlocked = true;
		Visible = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (Visible && e.IsActionPressed("release_mouse"))
		{
			Back();
			GetViewport().SetInputAsHandled();
		}
	}

	private void Back()
	{
		if (!Visible)
			return;
		Visible = false;
		Closed?.Invoke();
		_back?.Invoke();
	}
}
