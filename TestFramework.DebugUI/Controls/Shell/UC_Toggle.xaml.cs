using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TestFramework.DebugUI.Controls.Shell;

/// <summary>
/// One boolean setting: a label, an explanation, and a switch.
/// </summary>
/// <remarks>
/// The explanation is not optional decoration. A settings panel of bare labels makes the reader guess
/// what each one does and, worse, guess what the default was for — so every row here has room to say
/// why it exists, which is the same standard the code comments are held to.
/// </remarks>
public partial class UC_Toggle : UserControl
{
    private bool isOn;
    private bool raising;

    public UC_Toggle()
    {
        InitializeComponent();
        Paint();
    }

    /// <summary>Raised when the user changes the switch, not when it is set in code.</summary>
    /// <remarks>
    /// The distinction matters: the panel sets these while it is being populated, and a control that
    /// announced those would have the panel writing settings the moment it opened.
    /// </remarks>
    public event Action<bool>? Toggled;

    /// <summary>The setting's name.</summary>
    public string Label
    {
        get => tbLabel.Text;
        set => tbLabel.Text = value;
    }

    /// <summary>What the setting does, and why it is set the way it is.</summary>
    public string Note
    {
        get => tbNote.Text;
        set
        {
            tbNote.Text = value;
            tbNote.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>Whether the switch is on.</summary>
    public bool IsOn
    {
        get => isOn;
        set
        {
            if (isOn == value)
                return;

            isOn = value;
            Paint();

            if (!raising)
                return;

            Toggled?.Invoke(isOn);
        }
    }

    /// <summary>Sets the switch without announcing it, for populating the panel.</summary>
    public void SetQuietly(bool value)
    {
        isOn = value;
        Paint();
    }

    private void Row_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        raising = true;

        try
        {
            IsOn = !IsOn;
        }
        finally
        {
            raising = false;
        }

        e.Handled = true;
    }

    private void Paint()
    {
        bTrack.Background = (Brush)FindResource(isOn ? "AccentSelection" : "SurfaceRaised");
        eKnob.Background = (Brush)FindResource(isOn ? "TextPrimary" : "TextFaint");
        eKnob.HorizontalAlignment = isOn ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        eKnob.Margin = isOn ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0);
    }
}
