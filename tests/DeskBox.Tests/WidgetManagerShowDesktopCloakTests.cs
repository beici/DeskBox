using DeskBox.Services;

namespace DeskBox.Tests;

/// <summary>
/// FWIN-01: the Show-Desktop self-heal must treat a widget as cloaked
/// whenever DWMWA_CLOAK carries any cloak bit. The mask semantics: 0
/// visible, 1 cloaked by the owner app, 2 cloaked by the shell, 4 cloak
/// inherited from the owner, -1 the dwmapi-unavailable failure sentinel.
/// </summary>
public sealed class WidgetManagerShowDesktopCloakTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(7, true)]
    [InlineData(-1, false)]
    public void IsDwmCloakStateCloaked_HonoursTheFullDwmMask(int cloakState, bool expected)
    {
        Assert.Equal(expected, WidgetManager.IsDwmCloakStateCloaked(cloakState));
    }
}
