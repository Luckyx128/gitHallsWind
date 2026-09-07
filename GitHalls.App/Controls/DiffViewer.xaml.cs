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
    private bool _sideBySide;

    public DiffViewer()
    {
        InitializeComponent();

        LeftView.VerticalOffsetChanged += (_, offset) => Mirror(RightView, offset);
        RightView.VerticalOffsetChanged += (_, offset) => Mirror(LeftView, offset);
    }

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
        Apply();
    }

    private void Apply()
    {
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
