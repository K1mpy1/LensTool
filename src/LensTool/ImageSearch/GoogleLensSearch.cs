using System.Windows.Media.Imaging;

namespace LensTool.ImageSearch;

/// <summary>
/// Google Lens. A direct upload from the app runs into Google's cookie consent in the EU, so instead
/// the image goes to the clipboard and Google opens with the Lens upload dialog; the user presses Ctrl+V.
/// </summary>
public sealed class GoogleLensSearch(Action<BitmapSource> copyToClipboard) : IImageSearchProvider
{
    // "olud" opens Google's "Search any image with Google Lens" dialog right away.
    public const string LensDialogUrl = "https://www.google.com/?olud";

    public string Name => "Google";

    public Task<ImageSearchLaunch> PrepareAsync(BitmapSource image, CancellationToken cancellationToken = default)
    {
        copyToClipboard(image);
        return Task.FromResult(new ImageSearchLaunch(LensDialogUrl, "Картинка скопирована — вставьте её в Google Lens: Ctrl+V."));
    }
}
