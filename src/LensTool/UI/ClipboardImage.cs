using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace LensTool.UI;

/// <summary>Puts an image on the clipboard in the formats other apps actually read.</summary>
public static class ClipboardImage
{
    /// <summary>
    /// Bitmap (Windows also derives CF_DIB from it) for classic apps like Paint, plus "PNG",
    /// which browsers, Office and messengers prefer. Throws <see cref="System.Runtime.InteropServices.ExternalException"/>
    /// when another app holds the clipboard.
    /// </summary>
    public static void Set(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        var png = new MemoryStream();
        encoder.Save(png);
        png.Position = 0;

        var data = new DataObject();
        data.SetImage(image);
        data.SetData("PNG", png, autoConvert: false);
        // copy: true keeps the data available after the app exits.
        Clipboard.SetDataObject(data, copy: true);
    }
}
