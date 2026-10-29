using System.Windows.Media.Imaging;

namespace LensTool.ImageSearch;

/// <summary>Reverse image search. Implementations are swappable.</summary>
public interface IImageSearchProvider
{
    /// <summary>Provider name for logs and settings.</summary>
    string Name { get; }

    /// <summary>Prepares the search and returns what to open in the browser.</summary>
    Task<ImageSearchLaunch> PrepareAsync(BitmapSource image, CancellationToken cancellationToken = default);
}

/// <param name="Target">URL to open in the default browser.</param>
/// <param name="Hint">What the user still has to do in the browser, if anything.</param>
public sealed record ImageSearchLaunch(string Target, string? Hint = null);

/// <summary>The search could not be prepared; <see cref="Exception.Message"/> is shown to the user.</summary>
public sealed class ImageSearchException(string message, Exception? inner = null) : Exception(message, inner);
