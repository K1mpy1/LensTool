using System.Windows.Media.Imaging;

namespace LensTool.ImageSearch;

/// <summary>
/// Google Lens through the user's own browser: the image goes to the clipboard, Google opens with the
/// Lens upload dialog and the app pastes the image there (see <see cref="BrowserPaste"/>).
/// The browser uploads it in the user's own Google session, so its cookie consent and account apply;
/// results links obtained by uploading from the app itself expire when opened in a signed-in browser.
/// </summary>
public sealed class GoogleLensSearch(Action<BitmapSource> copyToClipboard) : IImageSearchProvider
{
    public const string LensDialogUrl = "https://www.google.com/?olud"; // "olud" opens the Lens upload dialog

    public string Name => "Google";

    public Task<ImageSearchLaunch> PrepareAsync(BitmapSource image, CancellationToken cancellationToken = default)
    {
        copyToClipboard(image);
        AppLog.Info($"Картинка {image.PixelWidth}x{image.PixelHeight} скопирована в буфер обмена");
        return Task.FromResult(new ImageSearchLaunch(LensDialogUrl, PasteIntoBrowser: true));
    }
}
