using System;
using Godot;
using Rebirth.Core;

namespace Rebirth.UI;

/// <summary>Every action and its key; click one, press the new key. A key already in use swaps over.</summary>
public partial class ControlsPanel : CanvasLayer
{
	public event Action? Closed;
	public bool IsOpen => Visible;

	private VBoxContainer _rows = null!;
	private Label _message = null!;
	private string? _listening;
	private Action? _back;

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
		var panel = new PanelContainer { CustomMinimumSize = new Vector2(620, 640) };
		root.AddChild(panel);
		panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center, Control.LayoutPresetMode.Minsize);
		panel.GrowHorizontal = Control.GrowDirection.Both;
		panel.GrowVertical = Control.GrowDirection.Both;
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 8);
		panel.AddChild(box);
		var title = new Label { Text = "CONTROLS" };
		title.AddThemeColorOverride("font_color", UiTheme.Accent);
		title.AddThemeFontSizeOverride("font_size", 24);
		box.AddChild(title);
		var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		box.AddChild(scroll);
		_rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_rows.AddThemeConstantOverride("separation", 3);
		scroll.AddChild(_rows);
		_message = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_message.AddThemeColorOverride("font_color", UiTheme.Accent);
		box.AddChild(_message);
		var bottom = new HBoxContainer();
		var reset = new Button { Text = "Reset to defaults" };
		reset.Pressed += () =>
		{
			Keybinds.ResetAll();
			_message.Text = "All keys are back to their defaults.";
			Rebuild();
		};
		bottom.AddChild(reset);
		bottom.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
		var back = new Button { Text = "Back" };
		back.Pressed += Back;
		bottom.AddChild(back);
		box.AddChild(bottom);
	}

	public void Open(Action back)
	{
		_back = back;
		_listening = null;
		_message.Text = "Click an action, then press the key or mouse button you want.";
		Rebuild();
		GameState.WorldInputBlocked = true;
		Visible = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	private void Rebuild()
	{
		foreach (var child in _rows.GetChildren())
			child.QueueFree();
		foreach (var (action, name) in Keybinds.Rebindable)
		{
			var row = new HBoxContainer();
			row.AddChild(new Label { Text = name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
			var key = new Button { Text = _listening == action ? "press a key..." : Keybinds.Label(action), CustomMinimumSize = new Vector2(170, 32) };
			key.Pressed += () =>
			{
				_listening = action;
				Rebuild();
			};
			row.AddChild(key);
			_rows.AddChild(row);
		}
	}

	public override void _Input(InputEvent e)
	{
		if (!Visible || _listening is null)
			return;
		InputEvent? chosen = e switch
		{
			InputEventKey { Pressed: true, Echo: false } key => new InputEventKey { PhysicalKeycode = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode },
			InputEventMouseButton { Pressed: true } mouse => new InputEventMouseButton { ButtonIndex = mouse.ButtonIndex },
			_ => null,
		};
		if (chosen is null)
			return;
		GetViewport().SetInputAsHandled();
		string action = _listening;
		_listening = null;
		string? swapped = Keybinds.Rebind(action, chosen);
		_message.Text = swapped is null
			? $"{NameOf(action)}: {Keybinds.Label(action)}"
			: $"{NameOf(action)}: {Keybinds.Label(action)}. {NameOf(swapped)} moved to {Keybinds.Label(swapped)}.";
		// Let the click that picked the key finish before the buttons are rebuilt.
		CallDeferred(MethodName.Rebuild);
	}

	private static string NameOf(string action)
	{
		foreach (var (a, name) in Keybinds.Rebindable)
			if (a == action)
				return name;
		return action;
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (Visible && _listening is null && e.IsActionPressed("release_mouse"))
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
