using System;
using Godot;
using Rebirth.Life;

namespace Rebirth.UI;

/// <summary>
/// Visiting a village: what its people are asking, and short conversations with a few answers to
/// choose from. Opened from the Nexus or with F on an incubator.
/// </summary>
public partial class TalkPanel : CanvasLayer
{
	public event Action? Closed;
	public bool IsOpen => Visible;

	public People People { get; set; } = null!;

	private Settlement? _settlement;
	private Label _title = null!;
	private Label _about = null!;
	private Label _text = null!;
	private VBoxContainer _choices = null!;

	public override void _Ready()
	{
		Layer = 15;
		Visible = false;
		var root = new Control { Theme = UiTheme.Create(), MouseFilter = Control.MouseFilterEnum.Ignore };
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(root);
		var panel = new PanelContainer { CustomMinimumSize = new Vector2(560, 0) };
		root.AddChild(panel);
		panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center, Control.LayoutPresetMode.Minsize);
		panel.GrowHorizontal = Control.GrowDirection.Both;
		panel.GrowVertical = Control.GrowDirection.Both;
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 10);
		panel.AddChild(box);
		_title = new Label();
		_title.AddThemeColorOverride("font_color", UiTheme.Accent);
		_title.AddThemeFontSizeOverride("font_size", 22);
		box.AddChild(_title);
		_about = new Label();
		_about.AddThemeColorOverride("font_color", UiTheme.Dim);
		_about.AddThemeFontSizeOverride("font_size", 14);
		box.AddChild(_about);
		_text = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(536, 0) };
		_text.AddThemeFontSizeOverride("font_size", 18);
		box.AddChild(_text);
		_choices = new VBoxContainer();
		_choices.AddThemeConstantOverride("separation", 6);
		box.AddChild(_choices);
	}

	public void Open(Settlement settlement)
	{
		_settlement = settlement;
		_title.Text = settlement.Name;
		_about.Text = $"{settlement.Population} people on {settlement.Planet}   ·   Bond {Hearts(settlement.Bond)}";
		ClearChoices();
		switch (settlement.Request)
		{
			case { Kind: RequestKind.Talk } talk:
				var dialogue = RequestCatalog.Dialogues[talk.Dialogue];
				_text.Text = People.Fill(dialogue.Opening, settlement, talk.Person);
				for (int i = 0; i < dialogue.Answers.Count; i++)
				{
					int index = i;
					AddChoice(dialogue.Answers[i].Text, () => ShowReply(People.Answer(settlement, index)));
				}
				break;
			case { } request:
				_text.Text = $"{request.Person}: \"{request.Text}\"\n\n{People.Progress(settlement)}" +
					(request.GiftAmount > 0f ? "\nThey have a little something for you in return." : "");
				AddChoice("I'll see to it", Close);
				break;
			default:
				_text.Text = settlement.Population > 0
					? "Everyone is busy with their gardens today. They wave as you pass."
					: "The incubator hums softly. Nobody is awake yet.";
				AddChoice("Wave back", Close);
				break;
		}
		Visible = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	private void ShowReply(string reply)
	{
		ClearChoices();
		_text.Text = reply;
		if (_settlement is not null)
			_about.Text = $"{_settlement.Population} people on {_settlement.Planet}   ·   Bond {Hearts(_settlement.Bond)}";
		AddChoice("Goodbye for now", Close);
	}

	public void Close()
	{
		if (!Visible)
			return;
		Visible = false;
		_settlement = null;
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

	/// <summary>Bond as five hearts.</summary>
	public static string Hearts(float bond)
	{
		int full = Mathf.Clamp(Mathf.RoundToInt(bond / 20f), 0, 5);
		return new string('♥', full) + new string('♡', 5 - full);
	}

	private void AddChoice(string text, Action action)
	{
		var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 38), Alignment = HorizontalAlignment.Left };
		button.Pressed += action;
		_choices.AddChild(button);
	}

	private void ClearChoices()
	{
		foreach (var child in _choices.GetChildren())
			child.QueueFree();
	}
}
