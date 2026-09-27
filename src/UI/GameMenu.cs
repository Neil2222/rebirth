using System;
using System.Collections.Generic;
using Godot;
using Rebirth.Core;

namespace Rebirth.UI;

/// <summary>
/// The title menu at start-up and the pause menu on Esc: everything the game does can be reached
/// from here with the mouse (Nexus, Forge, saving, new games with or without the tutorial).
/// </summary>
public partial class GameMenu : CanvasLayer
{
	public sealed record Entry(string Text, Action Action, bool Confirm = false, Func<bool>? Enabled = null);

	public event Action? Closed;
	public bool IsOpen => Visible;

	private VBoxContainer _buttons = null!;
	private Label _title = null!;
	private Label _subtitle = null!;
	private Button? _armed;   // a button waiting for its confirming second click

	public override void _Ready()
	{
		Layer = 20;
		Visible = false;
		ProcessMode = ProcessModeEnum.Always;

		var root = new Control { Theme = UiTheme.Create() };
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(root);
		// Soft dim over the world, warm rather than black.
		var dim = new ColorRect { Color = new Color(0.16f, 0.1f, 0.14f, 0.45f), MouseFilter = Control.MouseFilterEnum.Stop };
		dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		root.AddChild(dim);

		var panel = new PanelContainer { CustomMinimumSize = new Vector2(420, 0) };
		root.AddChild(panel);
		panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center, Control.LayoutPresetMode.Minsize);
		panel.GrowHorizontal = Control.GrowDirection.Both;
		panel.GrowVertical = Control.GrowDirection.Both;
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 10);
		panel.AddChild(box);
		_title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_title.AddThemeColorOverride("font_color", UiTheme.Accent);
		_title.AddThemeFontSizeOverride("font_size", 34);
		box.AddChild(_title);
		_subtitle = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_subtitle.AddThemeColorOverride("font_color", UiTheme.Dim);
		box.AddChild(_subtitle);
		_buttons = new VBoxContainer();
		_buttons.AddThemeConstantOverride("separation", 8);
		box.AddChild(_buttons);
	}

	public void Open(string title, string subtitle, IReadOnlyList<Entry> entries)
	{
		_title.Text = title;
		_subtitle.Text = subtitle;
		_armed = null;
		foreach (var child in _buttons.GetChildren())
			child.QueueFree();
		foreach (var entry in entries)
		{
			var button = new Button { Text = entry.Text, CustomMinimumSize = new Vector2(0, 40), Disabled = entry.Enabled?.Invoke() == false };
			button.Pressed += () => Press(button, entry);
			_buttons.AddChild(button);
		}
		GameState.WorldInputBlocked = true;
		Visible = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	private void Press(Button button, Entry entry)
	{
		if (entry.Confirm && _armed != button)
		{
			if (_armed is not null)
				_armed.Text = _armed.Text.Replace("  - click again", "");
			_armed = button;
			button.Text += "  - click again";
			return;
		}
		Close();
		entry.Action();
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (Visible && e.IsActionPressed("release_mouse"))
		{
			Close();
			GetViewport().SetInputAsHandled();
		}
	}

	public void Close()
	{
		if (!Visible)
			return;
		Visible = false;
		Closed?.Invoke();
	}
}
