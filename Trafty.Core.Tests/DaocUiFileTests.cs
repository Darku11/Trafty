using System.Linq;
using Trafty.Core.UI;
using Xunit;

namespace Trafty.Core.Tests;

public sealed class DaocUiFileTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, name);

    [Fact]
    public void Parse_ChatWindow_MatchesKnownFields()
    {
        DaocUiFile ui = DaocUiFile.Load(Fixture("chat_window.xml"));

        Assert.Single(ui.Windows);
        Assert.Single(ui.Textures);
        Assert.Equal(2, ui.ImageAreaTemplates.Count);

        DaocTextureDef texture = ui.Textures[0];
        Assert.Equal("emoticons", texture.Name);
        Assert.Equal("atlantis/emoticons.tga", texture.File);

        DaocWindowTemplate window = ui.Windows[0];
        Assert.Equal("chat", window.Name);
        Assert.Equal(350, window.Width);
        Assert.Equal(200, window.Height);
        Assert.False(window.CloseButton);
        Assert.False(window.MoveButton);
        Assert.Equal("Chat", window.WindowId);
        Assert.Equal(new[] { "Main", "Broad", "Guild", "Group", "Chat" }, window.TabNames);
        Assert.Equal(8, window.Controls.Count);
    }

    [Fact]
    public void Parse_ChatWindow_HandlesLowercaseWidthHeightElements()
    {
        // chat_window.xml mixes <Width>/<Height> and lowercase <width>/<height> for the
        // same logical field across different controls in the same file — this is the
        // real-world quirk the case-insensitive lookup exists for.
        DaocUiFile ui = DaocUiFile.Load(Fixture("chat_window.xml"));
        DaocWindowTemplate window = ui.Windows[0];

        DaocControlDef chatEntry = window.Controls.Single(c => c.ControlId == "1002");
        Assert.Equal(343, chatEntry.Width);
        Assert.Equal(16, chatEntry.Height);

        DaocControlDef invisibleButton = window.Controls.Single(c => c.ControlId == "1005");
        Assert.Equal(340, invisibleButton.Width);
        Assert.Equal(98, invisibleButton.Height);
    }

    [Fact]
    public void Parse_ChatWindow_ImageAreaTemplatesResolveToTexture()
    {
        DaocUiFile ui = DaocUiFile.Load(Fixture("chat_window.xml"));

        DaocImageAreaTemplate emoteButton = ui.ImageAreaTemplates.Single(t => t.Name == "emote_button");
        Assert.Equal("emoticons", emoteButton.TextureName);
        Assert.Equal(20, emoteButton.SizeX);
        Assert.Equal(20, emoteButton.SizeY);
        Assert.Equal(80, emoteButton.TopLeftX);
        Assert.Equal(100, emoteButton.TopLeftY);
    }

    [Fact]
    public void Parse_CommandWindow_MatchesKnownFields()
    {
        DaocUiFile ui = DaocUiFile.Load(Fixture("command_window.xml"));

        DaocWindowTemplate window = Assert.Single(ui.Windows);
        Assert.Equal("command", window.Name);
        Assert.Equal(135, window.Width);
        Assert.Equal(216, window.Height);
        Assert.True(window.CloseButton);
        Assert.True(window.MoveButton);
        Assert.Empty(window.TabNames);

        // 1 FullResizeImageDef background + 22 ButtonDefs.
        Assert.Equal(23, window.Controls.Count);
        Assert.Equal(22, window.Controls.Count(c => c.Kind == "ButtonDef"));
    }

    [Fact]
    public void Parse_CommandWindow_ButtonsCarryLabelAndTemplateButNoExplicitSize()
    {
        DaocUiFile ui = DaocUiFile.Load(Fixture("command_window.xml"));
        DaocWindowTemplate window = ui.Windows[0];

        DaocControlDef attackButton = window.Controls.Single(c => c.OnClickEvent == "ToggleAttackMode");
        Assert.Equal("ATTACK", attackButton.Label);
        Assert.Equal("button_large", attackButton.TemplateName);
        Assert.Equal(6, attackButton.X);
        Assert.Equal(15, attackButton.Y);

        // Real quirk verified against the file: button size comes from TemplateName, not
        // an explicit Width/Height on the ButtonDef itself.
        Assert.Null(attackButton.Width);
        Assert.Null(attackButton.Height);
    }

    [Fact]
    public void Parse_ButtonWithoutExplicitControlId_LeavesControlIdNull()
    {
        // The "CLOCK" button in command_window.xml has no <ControlId> at all — a real gap
        // in the source data, not something to paper over with a fabricated id.
        DaocUiFile ui = DaocUiFile.Load(Fixture("command_window.xml"));
        DaocWindowTemplate window = ui.Windows[0];

        DaocControlDef clockButton = window.Controls.Single(c => c.Label == "CLOCK");
        Assert.Null(clockButton.ControlId);
    }

    [Fact]
    public void Parse_InvalidXml_Throws()
    {
        Assert.Throws<DaocUiFormatException>(() => DaocUiFile.Parse("not xml at all <<<"));
    }

    [Fact]
    public void Parse_MissingRootElement_Throws()
    {
        Assert.Throws<DaocUiFormatException>(() => DaocUiFile.Parse("<SomethingElse></SomethingElse>"));
    }

    [Fact]
    public void Render_RealWindow_ProducesNonEmptyImage()
    {
        DaocUiFile ui = DaocUiFile.Load(Fixture("command_window.xml"));

        using var stream = new MemoryStream();
        DaocWindowRenderer.SaveAsPng(ui.Windows[0], stream);

        Assert.True(stream.Length > 0);
    }
}
