using GitHalls.Core.Diff;
using GitHalls.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GitHalls.App.Controls;

/// <summary>
/// Shows a diff either unified — one column, removals above additions — or side
/// by side, the old file on the left and the new one on the right.
///
/// Split mode is two of the same view rather than a second renderer: the diff is
/// cut into two columns padded to the same row count, so keeping them aligned is
/// only a matter of keeping their scroll positions equal.
/// </summary>
public sealed partial class DiffViewer : UserControl
{
    private FileDiff? _diff;
    private BinaryFileContents? _preview;
    private bool _sideBySide;
    private bool _selectionEnabled;

    public DiffViewer()
    {
        InitializeComponent();

        LeftView.VerticalOffsetChanged += (_, offset) => Mirror(RightView, offset);
        RightView.VerticalOffsetChanged += (_, offset) => Mirror(LeftView, offset);

        UnifiedView.SelectionChanged += (_, count) => SelectionChanged?.Invoke(this, count);
        UnifiedView.HunkActionInvoked += (_, hunk) => HunkActionInvoked?.Invoke(this, hunk);
    }

    // MARK: - Staging selection

    /// <summary>
    /// Lets rows be picked for staging. Only the unified view offers it: the
    /// split columns are padded copies, so a row there is not a row of the diff.
    ///
    /// Takes effect at the next <see cref="SetDiff"/>, which is what keeps a
    /// file change down to a single render.
    /// </summary>
    public bool SelectionEnabled
    {
        get => _selectionEnabled;
        set
        {
            _selectionEnabled = value;
            UnifiedView.SelectionEnabled = value;
        }
    }

    /// <summary>Wording of the per-hunk button — "Stage hunk" or "Unstage hunk".</summary>
    public string HunkActionLabel
    {
        get => UnifiedView.HunkActionLabel;
        set => UnifiedView.HunkActionLabel = value;
    }

    public int SelectedCount => UnifiedView.SelectedCount;
    public HashSet<int> SelectedLineIndices => UnifiedView.SelectedLineIndices;
    public void ClearSelection() => UnifiedView.ClearSelection();

    public event EventHandler<int>? SelectionChanged;
    public event EventHandler<DiffHunk>? HunkActionInvoked;

    public bool SideBySide
    {
        get => _sideBySide;
        set
        {
            if (_sideBySide == value) return;
            _sideBySide = value;
            Apply();
        }
    }

    /// <summary>Replaces what is shown. Pass null to clear.</summary>
    public void SetDiff(FileDiff? diff)
    {
        _diff = diff;

        // A text diff cannot have a preview. Clearing it here is what stops the
        // order these two setters are called in from leaving the previous
        // file's image on screen for a frame.
        if (diff is not { IsBinary: true }) _preview = null;

        Apply();
    }

    /// <summary>
    /// The file behind a binary diff. Set alongside the diff, and null for
    /// anything git had text for.
    /// </summary>
    public void SetPreview(BinaryFileContents? preview)
    {
        _preview = preview;
        Apply();
    }

    private void Apply()
    {
        // A file with no text diff is shown, not described. Everything below
        // this point is about text, and none of it applies.
        if (_preview is { HasSomethingToShow: true })
        {
            UnifiedView.SetDiff(null);
            UnifiedView.Visibility = Visibility.Collapsed;
            LeftView.SetDiff(null);
            RightView.SetDiff(null);
            SplitGrid.Visibility = Visibility.Collapsed;

            PreviewView.Visibility = Visibility.Visible;
            _ = PreviewView.ShowAsync(_preview);
            return;
        }

        PreviewView.Visibility = Visibility.Collapsed;
        _ = PreviewView.ShowAsync(null);

        // A binary file or a one-line notice has no second side to show.
        var split = _sideBySide && _diff != null && SideBySideDiff.HasTwoSides(_diff);

        if (split)
        {
            var (left, right) = SideBySideDiff.Split(_diff!);

            UnifiedView.SetDiff(null);
            UnifiedView.Visibility = Visibility.Collapsed;

            LeftView.SetDiff(left);
            RightView.SetDiff(right);
            SplitGrid.Visibility = Visibility.Visible;
            return;
        }

        // Dropped, not just hidden: a hidden view still holds its whole layout.
        LeftView.SetDiff(null);
        RightView.SetDiff(null);
        SplitGrid.Visibility = Visibility.Collapsed;

        UnifiedView.SetDiff(_diff);
        UnifiedView.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Keeps the other column at the same offset. The comparison is what stops
    /// the echo: the scroll this causes reports back an offset that already
    /// matches, and the exchange ends there.
    /// </summary>
    private static void Mirror(DiffTextView target, double offset)
    {
        if (Math.Abs(target.VerticalOffset - offset) < 0.5) return;
        target.SetVerticalOffset(offset);
    }
}
