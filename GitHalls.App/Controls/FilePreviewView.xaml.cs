using GitHalls.Core.Diff;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;

namespace GitHalls.App.Controls;

/// <summary>
/// What stands in for the diff of a file git has no text for.
///
/// An image is shown as an image, before beside after, because "Binary file not
/// shown" is the one thing about a picture nobody needs to be told. Anything
/// else states what it is and how big it got.
/// </summary>
public sealed partial class FilePreviewView : UserControl
{
    public FilePreviewView()
    {
        InitializeComponent();
    }

    /// <summary>Replaces what is shown. Pass null to clear.</summary>
    public async Task ShowAsync(BinaryFileContents? contents)
    {
        ImagePanel.Visibility = Visibility.Collapsed;
        OtherPanel.Visibility = Visibility.Collapsed;

        BeforeImage.Source = null;
        AfterImage.Source = null;

        if (contents == null || !contents.HasSomethingToShow) return;

        if (contents.Kind == FilePreviewKind.Image)
        {
            await ShowImagesAsync(contents);
            return;
        }

        ShowOther(contents);
    }

    private async Task ShowImagesAsync(BinaryFileContents contents)
    {
        // "Deleted"/"Added" rather than "Before"/"After" when there is only one
        // side: a lone image labelled "Before" reads as a missing half.
        var beforeShown = await SetImageAsync(BeforeImage, contents.Before);
        var afterShown = await SetImageAsync(AfterImage, contents.After);

        BeforePanel.Visibility = beforeShown ? Visibility.Visible : Visibility.Collapsed;
        AfterPanel.Visibility = afterShown ? Visibility.Visible : Visibility.Collapsed;

        if (beforeShown)
        {
            BeforeCaption.Text = $"{(afterShown ? "Before" : "Deleted")} · {FilePreview.FormattedSize(contents.Before!.Length)}";
        }

        if (afterShown)
        {
            AfterCaption.Text = $"{(beforeShown ? "After" : "Added")} · {FilePreview.FormattedSize(contents.After!.Length)}";
        }

        // The extension said image and the decoder disagreed; the card at least
        // says what the file is rather than leaving an empty frame.
        if (!beforeShown && !afterShown)
        {
            ShowOther(contents);
            return;
        }

        ImagePanel.Visibility = Visibility.Visible;
    }

    private static async Task<bool> SetImageAsync(Image target, byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return false;

        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            target.Source = bitmap;
            return true;
        }
        catch (Exception)
        {
            // A format this machine cannot decode is not an error worth raising
            // over a preview; the file still gets described below.
            return false;
        }
    }

    private void ShowOther(BinaryFileContents contents)
    {
        var extension = Path.GetExtension(contents.FilePath).TrimStart('.').ToUpperInvariant();
        OtherTypeText.Text = extension.Length == 0 ? "Binary file" : $"{extension} file";
        OtherSizeText.Text = SizeLabel(contents);

        OtherPanel.Visibility = Visibility.Visible;
    }

    private static string SizeLabel(BinaryFileContents contents)
    {
        if (contents.Before != null && contents.After != null)
        {
            return contents.Before.Length == contents.After.Length
                ? FilePreview.FormattedSize(contents.After.Length)
                : $"{FilePreview.FormattedSize(contents.Before.Length)} → {FilePreview.FormattedSize(contents.After.Length)}";
        }

        if (contents.After != null) return $"Added · {FilePreview.FormattedSize(contents.After.Length)}";
        if (contents.Before != null) return $"Deleted · {FilePreview.FormattedSize(contents.Before.Length)}";

        return string.Empty;
    }
}
