using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Trafty.App.Guides;

namespace Trafty.App.Views;

public partial class MainWindow
{
    private bool _guideBindingsAttached;

    private void OnGuideSystemLoaded(object? sender, RoutedEventArgs e)
    {
        if (!_guideBindingsAttached)
        {
            BindGuideButton("ScanClientButton", TraftyGuides.Archive);
            BindGuideButton("NewArchiveButton", TraftyGuides.Archive);
            BindGuideButton("OpenArchiveButton", TraftyGuides.Archive);
            BindGuideButton("OpenWorldPropsButton", TraftyGuides.WorldProps);
            BindGuideButton("OpenZoneButton", TraftyGuides.Zone);
            BindGuideButton("OpenUiButton", TraftyGuides.Ui);
            BindGuideButton("OpenTextureButton", TraftyGuides.Texture);
            BindGuideButton("OpenColorTableButton", TraftyGuides.Atmosphere);
            BindGuideButton("OpenSoundButton", TraftyGuides.Audio);
            _guideBindingsAttached = true;
        }

        SetGuide(TraftyGuides.Archive);
    }

    private void BindGuideButton(string controlName, GuideProfile profile)
    {
        Button? button = this.FindControl<Button>(controlName);

        if (button is not null)
        {
            button.Click += (_, _) => SetGuide(profile);
        }
    }

    private void OnWorkspaceTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_guideBindingsAttached || sender is not TabControl tabs)
        {
            return;
        }

        GuideProfile profile = tabs.SelectedIndex switch
        {
            1 => TraftyGuides.WorldProps,
            2 => TraftyGuides.Zone,
            3 => TraftyGuides.Ui,
            _ => TraftyGuides.Archive,
        };

        SetGuide(profile);
    }

    private void SetGuide(GuideProfile profile)
    {
        Avalonia.Controls.Shapes.Path? sigil = this.FindControl<Avalonia.Controls.Shapes.Path>("GuideSigil");
        TextBlock? name = this.FindControl<TextBlock>("GuideName");
        TextBlock? role = this.FindControl<TextBlock>("GuideRole");
        TextBlock? message = this.FindControl<TextBlock>("GuideMessage");
        TextBlock? quip = this.FindControl<TextBlock>("GuideQuip");
        Border? portraitRing = this.FindControl<Border>("GuidePortraitRing");
        Border? guideCard = this.FindControl<Border>("GuideCard");

        var accent = new SolidColorBrush(Color.Parse(profile.AccentHex));

        if (sigil is not null)
        {
            sigil.Data = Geometry.Parse(profile.SigilGeometry);
            sigil.Fill = accent;
        }

        if (name is not null)
        {
            name.Text = profile.Name;
        }

        if (role is not null)
        {
            role.Text = profile.Role;
        }

        if (message is not null)
        {
            message.Text = profile.Message;
        }

        if (quip is not null)
        {
            quip.Text = profile.Quip;
        }

        if (portraitRing is not null)
        {
            portraitRing.BorderBrush = accent;
        }

        if (guideCard is not null)
        {
            guideCard.BorderBrush = accent;
        }
    }
}
