using System;
using System.Linq;
using Godot;
using Rebirth.Core;
using Rebirth.Persistence;

namespace Rebirth.UI;

public enum SlotMode { Save, Load, NewGame }

/// <summary>
/// The list of save slots, for saving into, loading from, or starting a new game in. Overwriting or
/// deleting a game that is in use needs a second click.
/// </summary>
public partial class SlotPanel : CanvasLayer
{
	/// <summary>Raised whenever the panel closes (chosen or backed out).</summary>
	public event Action? Closed;
	public bool IsOpen => Visible;

	private SlotMode _mode;
	private Action<int, string>? _choose;
	private Action? _back;
	private Label _title = null!;
	private LineEdit _name = null!;
	private HBoxContainer _nameRow = null!;
	private VBoxContainer _rows = null!;
	private Button? _armed;

	public override void _Ready()
	{
		Layer = 21;
		Visible = false;
		ProcessMode = ProcessModeEnum.Always;
		var root = new Control { Theme = UiTheme.Create() };
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(root);
		var dim = new ColorRect { Color = new Color(0.16f, 0.1f, 0.14f, 0.5f), MouseFilter = Control.MouseFilterEnum.Stop };
		dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		root.AddChild(dim);
		var panel = new PanelContainer { CustomMinimumSize = new Vector2(720, 0) };
		root.AddChild(panel);
		panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center, Control.LayoutPresetMode.Minsize);
		panel.GrowHorizontal = Control.GrowDirection.Both;
		panel.GrowVertical = Control.GrowDirection.Both;
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 10);
		panel.AddChild(box);
		_title = new Label();
		_title.AddThemeColorOverride("font_color", UiTheme.Accent);
		_title.AddThemeFontSizeOverride("font_size", 24);
		box.AddChild(_title);
		_nameRow = new HBoxContainer();
		_nameRow.AddChild(new Label { Text = "Name  " });
		_name = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MaxLength = 32 };
		_nameRow.AddChild(_name);
		box.AddChild(_nameRow);
		_rows = new VBoxContainer();
		_rows.AddThemeConstantOverride("separation", 6);
		box.AddChild(_rows);
		var back = new Button { Text = "Back", SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd };
		back.Pressed += Back;
		box.AddChild(back);
	}

	/// <param name="choose">Called with the chosen slot and the name typed (save / new game).</param>
	/// <param name="back">Called when the player backs out instead.</param>
	public void Open(SlotMode mode, Action<int, string> choose, Action back)
	{
		_mode = mode;
		_choose = choose;
		_back = back;
		_armed = null;
		_title.Text = mode switch
		{
			SlotMode.Save => "SAVE GAME",
			SlotMode.Load => "LOAD GAME",
			_ => "NEW GAME - CHOOSE A SLOT",
		};
		_nameRow.Visible = mode != SlotMode.Load;
		_name.Text = mode == SlotMode.Save ? Campaign.Active.SlotName : "";
		_name.PlaceholderText = mode == SlotMode.NewGame ? "Name (optional)" : "";
		Rebuild();
		GameState.WorldInputBlocked = true;
		Visible = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	private void Rebuild()
	{
		foreach (var child in _rows.GetChildren())
			child.QueueFree();
		for (int slot = 1; slot <= SaveSystem.SlotCount; slot++)
		{
			var campaign = Campaign.Peek(slot);
			bool active = slot == SaveSystem.ActiveSlot && campaign is not null;
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 8);
			var label = new Label
			{
				Text = campaign is null
					? $"{slot}.  Empty"
					: $"{slot}.  {campaign.SlotName}{(active ? "   (playing)" : "")}\n     {campaign.CurrentBox.Name} · {campaign.Boxes.Count} Box{(campaign.Boxes.Count > 1 ? "es" : "")} · ✦ {campaign.Starlight:0}{(campaign.SavedAt > System.DateTime.MinValue ? $" · {campaign.SavedAt:g}" : "")}",
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			};
			if (campaign is null)
				label.AddThemeColorOverride("font_color", UiTheme.Dim);
			row.AddChild(label);

			int index = slot;
			switch (_mode)
			{
				case SlotMode.Save:
					// Overwriting another game needs a second click; saving over the one you play doesn't.
					AddAction(row, "Save here", campaign is not null && !active, () => Choose(index));
					break;
				case SlotMode.Load:
					if (campaign is not null)
					{
						AddAction(row, "Load", false, () => Choose(index));
						var delete = AddAction(row, "Delete", true, () =>
						{
							SaveSystem.DeleteSlot(index);
							Rebuild();
						});
						delete.Disabled = active;
						delete.TooltipText = active ? "This is the game you are playing" : "";
					}
					break;
				default:
					AddAction(row, campaign is null ? "Start here" : "Overwrite", campaign is not null, () => Choose(index));
					break;
			}
			_rows.AddChild(row);
		}
	}

	private Button AddAction(HBoxContainer row, string text, bool confirm, Action action)
	{
		var button = new Button { Text = text, CustomMinimumSize = new Vector2(110, 38) };
		button.Pressed += () =>
		{
			if (confirm && _armed != button)
			{
				if (_armed is not null && IsInstanceValid(_armed))
					_armed.Text = _armed.Text.Replace("Sure?", "").Trim();
				_armed = button;
				button.Text = "Sure? " + text;
				return;
			}
			action();
		};
		row.AddChild(button);
		return button;
	}

	private void Choose(int slot)
	{
		string name = _name.Text.Trim();
		var choose = _choose;
		Hide();
		choose?.Invoke(slot, name);
	}

	private void Back()
	{
		var back = _back;
		Hide();
		back?.Invoke();
	}

	private new void Hide()
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
			Back();
			GetViewport().SetInputAsHandled();
		}
	}
}
