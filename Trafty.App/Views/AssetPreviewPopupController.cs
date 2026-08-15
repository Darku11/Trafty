using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Trafty.App.Views;

/// <summary>
/// Drives the two-stage asset preview shared by the main archive list and the Client
/// Explorer: right-click on an asset opens a small popup near the cursor (a thumbnail if
/// the asset is previewable, otherwise a short explanation of why not); hovering over a
/// thumbnail swaps to a larger popup. Both close once the pointer leaves them, with a short
/// grace period so moving the cursor from the small popup onto the large one doesn't cause
/// a flicker.
/// </summary>
public sealed class AssetPreviewPopupController
{
    private static readonly TimeSpan CloseGrace = TimeSpan.FromMilliseconds(200);

    private readonly Popup _smallPopup;
    private readonly Image _smallImage;
    private readonly TextBlock? _smallMessage;
    private readonly Popup _largePopup;
    private readonly Image _largeImage;
    private readonly TextBlock? _largeInfo;
    private readonly DispatcherTimer _closeTimer;
    private Bitmap? _bitmap;

    public AssetPreviewPopupController(
        Popup smallPopup, Image smallImage,
        Popup largePopup, Image largeImage,
        TextBlock? largeInfo = null,
        TextBlock? smallMessage = null)
    {
        _smallPopup = smallPopup;
        _smallImage = smallImage;
        _smallMessage = smallMessage;
        _largePopup = largePopup;
        _largeImage = largeImage;
        _largeInfo = largeInfo;

        _closeTimer = new DispatcherTimer { Interval = CloseGrace };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            _smallPopup.IsOpen = false;
            _largePopup.IsOpen = false;
        };

        if (_smallPopup.Child is Control smallContent)
        {
            smallContent.AddHandler(InputElement.PointerEnteredEvent, (_, _) => _closeTimer.Stop(), RoutingStrategies.Bubble);
            smallContent.AddHandler(InputElement.PointerExitedEvent, (_, _) => ScheduleClose(), RoutingStrategies.Bubble);
        }

        _smallImage.PointerEntered += (_, _) => ShowLarge();

        if (_largePopup.Child is Control largeContent)
        {
            largeContent.AddHandler(InputElement.PointerEnteredEvent, (_, _) => _closeTimer.Stop(), RoutingStrategies.Bubble);
            largeContent.AddHandler(InputElement.PointerExitedEvent, (_, _) => ScheduleClose(), RoutingStrategies.Bubble);
        }
    }

    /// <summary>Opens the small popup at the current pointer position showing the given bitmap.</summary>
    public void ShowSmall(Bitmap bitmap, string? info = null)
    {
        _bitmap?.Dispose();
        _bitmap = bitmap;
        _smallImage.Source = bitmap;
        _largeImage.Source = bitmap;
        _smallImage.IsVisible = true;

        if (_smallMessage is not null)
        {
            _smallMessage.IsVisible = false;
        }

        if (_largeInfo is not null)
        {
            _largeInfo.Text = info;
        }

        _closeTimer.Stop();
        _largePopup.IsOpen = false;
        _smallPopup.IsOpen = true;
    }

    /// <summary>
    /// Opens the small popup showing a short text explanation instead of a thumbnail — used
    /// when the right-clicked asset has no format Trafty can render (e.g. .csv) or rendering
    /// it failed. No hover-to-large step applies here since there's no image to enlarge.
    /// </summary>
    public void ShowMessage(string message)
    {
        if (_smallMessage is null)
        {
            return;
        }

        _closeTimer.Stop();
        _largePopup.IsOpen = false;
        _smallImage.IsVisible = false;
        _smallMessage.Text = message;
        _smallMessage.IsVisible = true;
        _smallPopup.IsOpen = true;
    }

    /// <summary>Hides both popups immediately, e.g. when the selection changes or the list scrolls.</summary>
    public void HideAll()
    {
        _closeTimer.Stop();
        _smallPopup.IsOpen = false;
        _largePopup.IsOpen = false;
    }

    private void ShowLarge()
    {
        if (!_smallImage.IsVisible)
        {
            return;
        }

        _closeTimer.Stop();
        _smallPopup.IsOpen = false;
        _largePopup.IsOpen = true;
    }

    private void ScheduleClose()
    {
        _closeTimer.Stop();
        _closeTimer.Start();
    }
}
