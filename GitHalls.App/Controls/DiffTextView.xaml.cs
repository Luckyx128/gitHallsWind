using GitHalls.App.Services;
using GitHalls.App.Themes;
using GitHalls.Core.Diff;
using GitHalls.Core.Models;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI;

namespace GitHalls.App.Controls;

/// <summary>
/// Diff renderer with selectable text, a line-number gutter and syntax
/// highlighting. Port of the Swift Views/DiffTextView/.
///
/// The load-bearing property: line numbers and the +/- markers live in their
/// own layer, never inside the text. Selecting a range and copying it yields
/// the code exactly as it is on disk, which is what the whole control exists
/// for.
///
/// That is why the text never wraps (horizontal scrolling instead): with a
/// monospaced font, a fixed LineHeight and BlockLineHeight stacking, row N sits
/// at exactly N * LineHeight, so the gutter and the row tints can be positioned
/// arithmetically without ever consulting the text layout.
/// </summary>
public sealed partial class DiffTextView : UserControl
{
    /// <summary>
    /// RichTextBlock lays out every paragraph it is given, so a huge diff has
    /// to be cut off rather than merely virtualized.
    /// </summary>
    private const int MaxRenderedLines = 4000;

    private const double GutterPadding = 6;
    private const double MarkerWidth = 14;
    private const double ColumnGap = 6;
    private const double TextGap = 8;

    /// <summary>Width of the staging column at the left of the gutter.</summary>
    private const double SelectColumnWidth = 18;

    private const string CheckGlyph = "\uE73E";
    private const string PartialGlyph = "\uE739";

    /// <summary>
    /// Resolved once. A per-row resource lookup is the exact cost the theme is
    /// duplicated in code to avoid.
    /// </summary>
    private static readonly FontFamily SymbolFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private readonly IDiffHighlighter _highlighter;

    private HighlightedDiff? _diff;

    /// <summary>
    /// The diff the highlighted one was made from. Kept because a row index is
    /// the same in both, and staging needs the hunks and the raw lines that only
    /// this one carries.
    /// </summary>
    private FileDiff? _source;

    private DiffTextTheme _theme = DiffTextTheme.Light;

    private int _renderedLineCount;
    private double _gutterWidth;
    private double _selectColumnWidth;
    private double _numberColumnWidth;
    private double _contentWidth;

    /// <summary>
    /// One flag per line rather than a set of controls: the whole reason a
    /// 4000-line diff can be selected through without the scroll suffering.
    /// </summary>
    private bool[] _selected = Array.Empty<bool>();
    private int _selectedCount;

    private int _hoverRow = -1;
    private int _dragAnchor = -1;

    /// <summary>Whether the drag in progress is selecting or clearing.</summary>
    private bool _dragSelects;
    private bool _dragging;
    private int _lastDragRow = -1;

    /// <summary>
    /// The selection as it was when the drag began. Each move restores it and
    /// reapplies the range, so dragging back over rows undoes them instead of
    /// leaving a trail.
    /// </summary>
    private bool[] _selectionBeforeDrag = Array.Empty<bool>();

    /// <summary>Line indices currently matching the find query.</summary>
    private readonly List<int> _matches = new();
    private int _currentMatch = -1;

    private int _firstPaintedRow = -1;
    private int _lastPaintedRow = -1;

    /// <summary>
    /// Height cap when the control sizes itself to its content instead of
    /// filling the pane — the mode used for the per-file sections of a commit,
    /// which stack inside an outer scroll. NaN means "fill the pane".
    ///
    /// Past the cap the inner scroller takes over, so one enormous file in a
    /// commit can't push every section below it off the screen.
    /// </summary>
    public double MaxIntrinsicHeight { get; set; } = double.NaN;

    private bool IsIntrinsic => !double.IsNaN(MaxIntrinsicHeight);

    /// <summary>
    /// Draw one line-number column instead of old and new. Each side of a
    /// side-by-side view carries only its own numbers, so the second column
    /// would always be empty there.
    /// </summary>
    public bool SingleNumberColumn { get; set; }

    /// <summary>Current vertical scroll position, in pixels.</summary>
    public double VerticalOffset => Scroller.VerticalOffset;

    /// <summary>Raised whenever this view scrolls, so another can follow it.</summary>
    public event EventHandler<double>? VerticalOffsetChanged;

    /// <summary>Scrolls to <paramref name="offset"/> without animating.</summary>
    public void SetVerticalOffset(double offset) => Scroller.ChangeView(null, offset, null, disableAnimation: true);

    // MARK: - Staging selection

    /// <summary>
    /// Lets rows be picked for staging. Off by default: the commit detail pane
    /// shows history, which nothing can be staged from.
    ///
    /// Read when the diff is rendered rather than acted on here, so setting it
    /// and then handing over a diff costs one render instead of two. Set it
    /// before <see cref="SetDiff"/>, never after.
    /// </summary>
    public bool SelectionEnabled { get; set; }

    /// <summary>How many changed lines are picked.</summary>
    public int SelectedCount => _selectedCount;

    /// <summary>The picked rows, as indices into the diff's lines.</summary>
    public HashSet<int> SelectedLineIndices
    {
        get
        {
            var set = new HashSet<int>();
            for (int i = 0; i < _selected.Length; i++)
            {
                if (_selected[i]) set.Add(i);
            }

            return set;
        }
    }

    /// <summary>Raised whenever the picked rows change, with the new count.</summary>
    public event EventHandler<int>? SelectionChanged;

    /// <summary>Raised by the button on a hunk header — stage or unstage that block outright.</summary>
    public event EventHandler<DiffHunk>? HunkActionInvoked;

    /// <summary>Text for that button. The pane sets it to "Stage" or "Unstage".</summary>
    public string HunkActionLabel { get; set; } = "Stage hunk";

    public void ClearSelection()
    {
        if (_selectedCount == 0) return;

        ClearSelectionState();
        RepaintLayers(force: true);
        SelectionChanged?.Invoke(this, 0);
    }

    private void ClearSelectionState()
    {
        Array.Clear(_selected);

        // Same length as the live selection, always: a drag restores from it by
        // a straight copy.
        _selectionBeforeDrag = new bool[_selected.Length];

        _selectedCount = 0;
        _hoverRow = -1;
        _dragAnchor = -1;
        _dragging = false;
        _lastDragRow = -1;
    }

    public DiffTextView() : this(ColorCodeDiffHighlighter.Instance) { }

    public DiffTextView(IDiffHighlighter highlighter)
    {
        _highlighter = highlighter;
        InitializeComponent();

        // Left, not the default Stretch: a stretched text block reports the
        // width it was given rather than the width its content needs.
        TextLayer.HorizontalAlignment = HorizontalAlignment.Left;
        TextLayer.FontFamily = new FontFamily(DiffTextTheme.FontFamily);
        TextLayer.FontSize = DiffTextTheme.FontSize;
        TextLayer.LineHeight = DiffTextTheme.LineHeight;

        BuildContextMenu();

        ActualThemeChanged += (_, _) => ApplyTheme();
        Loaded += (_, _) => ApplyTheme();

        var findAccelerator = new KeyboardAccelerator { Key = VirtualKey.F, Modifiers = VirtualKeyModifiers.Control };
        findAccelerator.Invoked += (_, args) => { args.Handled = true; OpenFind(); };
        KeyboardAccelerators.Add(findAccelerator);

        var copyAccelerator = new KeyboardAccelerator { Key = VirtualKey.C, Modifiers = VirtualKeyModifiers.Control };
        copyAccelerator.Invoked += (_, args) => { args.Handled = CopySelection(); };
        KeyboardAccelerators.Add(copyAccelerator);
    }

    /// <summary>Replaces what is shown. Pass null to clear.</summary>
    public void SetDiff(FileDiff? diff)
    {
        CloseFind();

        _selected = diff == null ? Array.Empty<bool>() : new bool[diff.Lines.Count];
        ClearSelectionState();

        if (diff == null || diff.Lines.Count == 0)
        {
            _diff = null;
            _source = null;
            TextLayer.Blocks.Clear();
            TintLayer.Children.Clear();
            GutterLayer.Children.Clear();
            OverlayLayer.Children.Clear();
            ShowRemainingButton.Visibility = Visibility.Collapsed;
            _renderedLineCount = 0;
            ApplyIntrinsicHeight();
            SelectionChanged?.Invoke(this, 0);
            return;
        }

        _source = diff;
        _diff = DiffHighlightMapper.Make(diff, _highlighter);
        Render(Math.Min(_diff.Lines.Count, MaxRenderedLines));
        SelectionChanged?.Invoke(this, 0);
    }

    private void Render(int lineCount)
    {
        if (_diff == null) return;

        _renderedLineCount = lineCount;
        MeasureGutter();

        // The gutter is painted (Background="Transparent") so it can receive the
        // pointer, which also means it swallows one. Off where nothing can be
        // picked, so dragging a text selection from the margin still works in
        // the commit pane.
        GutterLayer.IsHitTestVisible = CanSelect;

        BuildText();
        UpdateContentSize();
        RepaintLayers(force: true);

        ApplyIntrinsicHeight();

        var remaining = _diff.Lines.Count - _renderedLineCount;
        ShowRemainingButton.Content = $"Show remaining {remaining:N0} lines";
        ShowRemainingButton.Visibility = remaining > 0 ? Visibility.Visible : Visibility.Collapsed;

        Scroller.ChangeView(0, 0, null, disableAnimation: true);
    }

    /// <summary>
    /// In intrinsic mode the control asks for exactly the height its rows need,
    /// so an outer ScrollViewer can stack several of these. Filling the pane is
    /// the default and needs no explicit height.
    /// </summary>
    private void ApplyIntrinsicHeight()
    {
        if (!IsIntrinsic)
        {
            Height = double.NaN;
            return;
        }

        var content = _renderedLineCount * DiffTextTheme.LineHeight + 2;
        Height = Math.Max(DiffTextTheme.LineHeight, Math.Min(content, MaxIntrinsicHeight));
    }

    private void ShowRemaining_Click(object sender, RoutedEventArgs e)
    {
        if (_diff == null) return;
        Render(_diff.Lines.Count);
    }

    // MARK: - Text

    private void BuildText()
    {
        if (_diff == null) return;

        TextLayer.Blocks.Clear();
        TextLayer.Margin = new Thickness(_gutterWidth + TextGap, 0, TextGap, 0);

        for (int i = 0; i < _renderedLineCount; i++)
        {
            var line = _diff.Lines[i];
            var paragraph = new Paragraph { Margin = new Thickness(0) };

            if (line.Type == DiffLineType.HunkHeader)
            {
                paragraph.Inlines.Add(new Run
                {
                    Text = line.RawText,
                    Foreground = new SolidColorBrush(_theme.HunkHeaderText),
                    FontStyle = global::Windows.UI.Text.FontStyle.Italic
                });
            }
            else if (line.Runs.Count == 0)
            {
                paragraph.Inlines.Add(new Run { Text = string.Empty });
            }
            else
            {
                foreach (var run in line.Runs)
                {
                    paragraph.Inlines.Add(new Run
                    {
                        Text = run.Text,
                        Foreground = new SolidColorBrush(_theme.TokenColor(run.Kind))
                    });
                }
            }

            TextLayer.Blocks.Add(paragraph);
        }
    }

    // MARK: - Gutter metrics

    private void MeasureGutter()
    {
        if (_diff == null) return;

        var digits = Math.Max(
            Math.Max(_diff.MaxOldLineNumber, 1).ToString().Length,
            Math.Max(_diff.MaxNewLineNumber, 1).ToString().Length);

        // Monospaced digits at this size are close enough to 0.62em that
        // measuring a text block per render would be wasted work.
        var digitWidth = DiffTextTheme.GutterFontSize * 0.62;
        _numberColumnWidth = digits * digitWidth + 4;

        _selectColumnWidth = CanSelect ? SelectColumnWidth : 0;

        var numberColumns = SingleNumberColumn ? 1 : 2;
        _gutterWidth = Math.Ceiling(
            GutterPadding + _selectColumnWidth + MarkerWidth
            + numberColumns * (ColumnGap + _numberColumnWidth) + GutterPadding);
    }

    /// <summary>
    /// Whether this diff can be staged from at all. A binary file, a notice or
    /// an untracked file's synthesized diff carries no patch to build.
    /// </summary>
    private bool CanSelect => SelectionEnabled && _source is { CanBuildPatch: true };

    private void UpdateContentSize()
    {
        var height = _renderedLineCount * DiffTextTheme.LineHeight;

        // Release the width fixed by the previous diff first: the text block
        // stretches to fill it, so measuring against it would report the old
        // width and the content could only ever grow.
        ContentHost.Width = double.NaN;
        ContentHost.UpdateLayout();

        _contentWidth = Math.Max(
            _gutterWidth + TextGap + TextLayer.ActualWidth + TextGap,
            Scroller.ViewportWidth);

        ContentHost.Width = _contentWidth;
        ContentHost.Height = height;
        TintLayer.Width = _contentWidth;
        TintLayer.Height = height;
        GutterLayer.Width = _gutterWidth;
        GutterLayer.Height = height;

        // Zero-sized on purpose: a Canvas does not clip, so the one button it
        // may hold still draws, and nothing else in it can swallow a click.
        OverlayLayer.Width = 0;
        OverlayLayer.Height = 0;
    }

    // MARK: - Tint + gutter painting

    private void Scroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        // The gutter lives inside the scrolled content, so it is pushed back by
        // exactly the horizontal offset to stay pinned at the left edge.
        GutterTransform.X = Scroller.HorizontalOffset;
        OverlayTransform.X = Scroller.HorizontalOffset;
        RepaintLayers(force: false);
        VerticalOffsetChanged?.Invoke(this, Scroller.VerticalOffset);
    }

    private void Scroller_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateContentSize();
        RepaintLayers(force: true);
    }

    /// <summary>
    /// Rebuilds tints and gutter labels for the visible rows only — the reason
    /// a 4000-line diff stays responsive despite the text itself being fully
    /// laid out.
    /// </summary>
    private void RepaintLayers(bool force)
    {
        if (_diff == null || _renderedLineCount == 0) return;

        const int overscan = 10;
        var first = Math.Max(0, (int)(Scroller.VerticalOffset / DiffTextTheme.LineHeight) - overscan);
        var visible = (int)Math.Ceiling(Math.Max(Scroller.ViewportHeight, 1) / DiffTextTheme.LineHeight);
        var last = Math.Min(_renderedLineCount - 1, first + visible + overscan * 2);

        if (!force && first == _firstPaintedRow && last == _lastPaintedRow) return;
        _firstPaintedRow = first;
        _lastPaintedRow = last;

        TintLayer.Children.Clear();
        GutterLayer.Children.Clear();

        var gutterBackground = new Rectangle
        {
            Width = _gutterWidth,
            Height = GutterLayer.Height,
            Fill = new SolidColorBrush(_theme.GutterBackground)
        };
        Canvas.SetLeft(gutterBackground, 0);
        Canvas.SetTop(gutterBackground, 0);
        GutterLayer.Children.Add(gutterBackground);

        var separator = new Rectangle
        {
            Width = 1,
            Height = GutterLayer.Height,
            Fill = new SolidColorBrush(_theme.GutterSeparator)
        };
        Canvas.SetLeft(separator, _gutterWidth - 1);
        GutterLayer.Children.Add(separator);

        for (int i = first; i <= last; i++)
        {
            var line = _diff.Lines[i];
            var y = i * DiffTextTheme.LineHeight;

            PaintTint(line, i, y);
            PaintGutterRow(line, i, y);
        }

        PaintHover();
    }

    /// <summary>
    /// Everything that answers the pointer, on a layer of its own: at most a
    /// tint, a glyph and a button. Following the pointer would otherwise mean
    /// rebuilding every visible row for a change to one of them, which is what
    /// a hover down a long diff would have cost.
    /// </summary>
    private void PaintHover()
    {
        OverlayLayer.Children.Clear();

        if (!CanSelect || _hoverRow < 0 || _hoverRow >= _renderedLineCount) return;

        var line = _source!.Lines[_hoverRow];
        var y = _hoverRow * DiffTextTheme.LineHeight;
        var isHeader = line.Type == DiffLineType.HunkHeader;

        if (!isHeader && !PatchBuilder.IsSelectable(line)) return;

        var hunk = isHeader ? _source.HunkAt(_hoverRow) : null;
        if (isHeader && hunk is not { HasContent: true }) return;

        // Hit testing stays off for all of it except the button: the text
        // underneath has to keep its own selection.
        var tint = new Rectangle
        {
            Width = Math.Max(_contentWidth, 0),
            Height = DiffTextTheme.LineHeight,
            Fill = new SolidColorBrush(_theme.HoverBackground),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(tint, 0);
        Canvas.SetTop(tint, y);
        OverlayLayer.Children.Add(tint);

        if (isHeader)
        {
            var whole = IsWholeHunkSelected(hunk!);
            OverlayLayer.Children.Add(Glyph(whole ? CheckGlyph : PartialGlyph, GutterPadding, y,
                whole ? _theme.SelectionMark : _theme.SelectionMarkIdle));
            AddHunkButton(hunk!, y);
            return;
        }

        // Nothing is drawn under the pointer on a row already picked — its own
        // check is there already, in the gutter.
        if (_hoverRow >= _selected.Length || !_selected[_hoverRow])
        {
            OverlayLayer.Children.Add(Glyph(CheckGlyph, GutterPadding, y, _theme.SelectionMarkIdle));
        }
    }

    /// <summary>The one interactive element in the whole control.</summary>
    private void AddHunkButton(DiffHunk hunk, double y)
    {
        var button = new Button
        {
            Content = HunkActionLabel,
            FontSize = 11,
            Padding = new Thickness(8, 0, 8, 0),
            MinHeight = 0,
            Height = DiffTextTheme.LineHeight - 2,
            CornerRadius = new CornerRadius(3),
            Tag = hunk
        };
        button.Click += HunkButton_Click;

        Canvas.SetLeft(button, _gutterWidth + TextGap);
        Canvas.SetTop(button, y + 1);
        OverlayLayer.Children.Add(button);
    }

    private void HunkButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DiffHunk hunk }) HunkActionInvoked?.Invoke(this, hunk);
    }

    private void PaintTint(HighlightedDiffLine line, int index, double y)
    {
        var isMatch = _currentMatch >= 0 && _currentMatch < _matches.Count && _matches[_currentMatch] == index;
        var color = isMatch ? _theme.SearchHighlight : _theme.LineBackground(line.Type);

        // A hunk header reads as a full-width band; +/- tints start after the
        // gutter so the numbers keep their own backdrop.
        var left = line.Type == DiffLineType.HunkHeader ? 0 : _gutterWidth;

        if (color != null) AddTint(color.Value, left, y);

        // Selection sits over the +/- tint rather than replacing it: which side
        // a picked line is on still has to be readable.
        if (CanSelect && index < _selected.Length && _selected[index])
        {
            AddTint(_theme.SelectionBackground, left, y);
        }
    }

    private void AddTint(Color color, double left, double y)
    {
        var rectangle = new Rectangle
        {
            Width = Math.Max(_contentWidth - left, 0),
            Height = DiffTextTheme.LineHeight,
            Fill = new SolidColorBrush(color),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(rectangle, left);
        Canvas.SetTop(rectangle, y);
        TintLayer.Children.Add(rectangle);
    }

    private void PaintGutterRow(HighlightedDiffLine line, int index, double y)
    {
        if (line.Type == DiffLineType.HunkHeader)
        {
            PaintHunkHeaderMark(index, y);
            return;
        }

        PaintSelectionMark(index, y);

        var markerX = GutterPadding + _selectColumnWidth;
        var oldColumnX = markerX + MarkerWidth + ColumnGap;

        if (SingleNumberColumn)
        {
            // Only one of the two is ever set on a split side.
            AddNumber(line.OldLineNumber ?? line.NewLineNumber, oldColumnX, y);
        }
        else
        {
            AddNumber(line.OldLineNumber, oldColumnX, y);
            AddNumber(line.NewLineNumber, oldColumnX + _numberColumnWidth + ColumnGap, y);
        }

        var (symbol, markerColor) = line.Type switch
        {
            DiffLineType.Addition => ("+", _theme.AdditionMarker),
            DiffLineType.Deletion => ("−", _theme.DeletionMarker),
            _ => (null, default(Color))
        };

        if (symbol == null) return;

        var marker = new TextBlock
        {
            Text = symbol,
            FontFamily = new FontFamily(DiffTextTheme.FontFamily),
            FontSize = DiffTextTheme.GutterFontSize,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(markerColor),
            Width = MarkerWidth,
            Height = DiffTextTheme.LineHeight,
            TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(marker, markerX);
        Canvas.SetTop(marker, y);
        GutterLayer.Children.Add(marker);
    }

    /// <summary>
    /// The check in the staging column of a picked row. The faint one under the
    /// pointer belongs to <see cref="PaintHover"/>, so following the pointer
    /// never touches this layer.
    /// </summary>
    private void PaintSelectionMark(int index, double y)
    {
        if (!CanSelect) return;
        if (index >= _selected.Length || !_selected[index]) return;

        GutterLayer.Children.Add(Glyph(CheckGlyph, GutterPadding, y, _theme.SelectionMark));
    }

    private void PaintHunkHeaderMark(int index, double y)
    {
        if (!CanSelect) return;

        var hunk = _source?.HunkAt(index);
        if (hunk == null || !hunk.HasContent) return;

        var hasSelected = false;
        var hasUnselected = false;

        for (int i = hunk.FirstLineIndex; i <= hunk.LastLineIndex; i++)
        {
            if (!PatchBuilder.IsSelectable(_source!.Lines[i])) continue;
            if (_selected[i]) hasSelected = true;
            else hasUnselected = true;
        }


        var glyph = hasUnselected ? PartialGlyph : CheckGlyph;
        GutterLayer.Children.Add(Glyph(glyph, GutterPadding, y, _theme.SelectionMark));
    }

    private static TextBlock Glyph(string glyph, double x, double y, Color color)
    {
        var text = new TextBlock
        {
            Text = glyph,
            FontFamily = SymbolFont,
            FontSize = 11,
            Foreground = new SolidColorBrush(color),
            Width = SelectColumnWidth,
            Height = DiffTextTheme.LineHeight,
            TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(text, x);
        Canvas.SetTop(text, y);
        return text;
    }

    private void AddNumber(int? number, double x, double y)
    {
        if (number == null) return;

        var text = new TextBlock
        {
            Text = number.Value.ToString(),
            FontFamily = new FontFamily(DiffTextTheme.FontFamily),
            FontSize = DiffTextTheme.GutterFontSize,
            Foreground = new SolidColorBrush(_theme.GutterText),
            Width = _numberColumnWidth,
            Height = DiffTextTheme.LineHeight,
            TextAlignment = TextAlignment.Right,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(text, x);
        Canvas.SetTop(text, y);
        GutterLayer.Children.Add(text);
    }

    // MARK: - Pointer

    /// <summary>
    /// The row under the pointer. The gutter spans the whole content, so its own
    /// coordinates are already content space — no scroll offset to add and no
    /// text layout to ask, which is what makes this cheap enough to run on every
    /// move.
    /// </summary>
    private int RowAt(PointerRoutedEventArgs e)
    {
        var y = e.GetCurrentPoint(GutterLayer).Position.Y;
        if (y < 0) return -1;

        var row = (int)(y / DiffTextTheme.LineHeight);
        return row < _renderedLineCount ? row : -1;
    }

    private void GutterLayer_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!CanSelect) return;

        var row = RowAt(e);

        if (_dragging)
        {
            // The throttle that keeps a drag from thrashing: within one row,
            // there is nothing new to paint.
            if (row < 0 || row == _lastDragRow) return;

            _lastDragRow = row;
            ApplyDragRange(row);
            return;
        }

        if (row == _hoverRow) return;

        _hoverRow = row;
        PaintHover();
    }

    private void GutterLayer_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!CanSelect) return;

        var row = RowAt(e);
        if (row < 0) return;

        e.Handled = true;

        var line = _source!.Lines[row];
        if (line.Type == DiffLineType.HunkHeader)
        {
            var hunk = _source.HunkAt(row);
            if (hunk is { HasContent: true }) ToggleHunk(hunk);
            return;
        }

        if (!PatchBuilder.IsSelectable(line)) return;

        // Shift keeps the previous anchor, so a click and a shift-click bracket
        // a range the way a list does.
        var extending = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift) && _dragAnchor >= 0;
        if (!extending)
        {
            _dragAnchor = row;
            _dragSelects = !_selected[row];
            _selectionBeforeDrag = (bool[])_selected.Clone();
        }

        _dragging = true;
        _lastDragRow = row;
        ApplyDragRange(row);

        GutterLayer.CapturePointer(e.Pointer);
    }

    private void GutterLayer_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;

        _dragging = false;
        GutterLayer.ReleasePointerCapture(e.Pointer);
    }

    private void GutterLayer_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging || _hoverRow < 0) return;

        _hoverRow = -1;
        PaintHover();
    }

    /// <summary>
    /// Rewrites the selection as "what it was when the drag started, plus this
    /// range". Restoring first is what lets a drag be taken back by dragging
    /// the other way.
    /// </summary>
    private void ApplyDragRange(int row)
    {
        Array.Copy(_selectionBeforeDrag, _selected, _selected.Length);

        var from = Math.Min(_dragAnchor, row);
        var to = Math.Max(_dragAnchor, row);

        for (int i = from; i <= to; i++)
        {
            if (PatchBuilder.IsSelectable(_source!.Lines[i])) _selected[i] = _dragSelects;
        }

        PublishSelection();
    }

    private void ToggleHunk(DiffHunk hunk)
    {
        var select = !IsWholeHunkSelected(hunk);

        for (int i = hunk.FirstLineIndex; i <= hunk.LastLineIndex; i++)
        {
            if (PatchBuilder.IsSelectable(_source!.Lines[i])) _selected[i] = select;
        }

        // A hunk click is also an anchor: shift-clicking a line after it extends
        // from the block rather than from wherever the pointer last was.
        _dragAnchor = hunk.FirstLineIndex;
        _selectionBeforeDrag = (bool[])_selected.Clone();

        PublishSelection();
    }

    private bool IsWholeHunkSelected(DiffHunk hunk)
    {
        var sawOne = false;

        for (int i = hunk.FirstLineIndex; i <= hunk.LastLineIndex; i++)
        {
            if (!PatchBuilder.IsSelectable(_source!.Lines[i])) continue;
            if (!_selected[i]) return false;
            sawOne = true;
        }

        return sawOne;
    }

    private void PublishSelection()
    {
        var count = 0;
        foreach (var picked in _selected)
        {
            if (picked) count++;
        }

        _selectedCount = count;
        RepaintLayers(force: true);
        SelectionChanged?.Invoke(this, count);
    }

    // MARK: - Theme

    private void ApplyTheme()
    {
        _theme = DiffTextTheme.For(ActualTheme);
        RootGrid.Background = new SolidColorBrush(_theme.ViewBackground);
        TextLayer.Foreground = new SolidColorBrush(_theme.BaseText);

        if (_diff == null) return;

        // Token brushes are baked into the runs, so a theme change means a rebuild.
        BuildText();
        RepaintLayers(force: true);
    }

    // MARK: - Copy

    private void BuildContextMenu()
    {
        var menu = new MenuFlyout();

        var copySelection = new MenuFlyoutItem { Text = "Copy" };
        copySelection.Click += (_, _) => CopySelection();
        menu.Items.Add(copySelection);

        menu.Items.Add(new MenuFlyoutSeparator());

        var copyDiff = new MenuFlyoutItem { Text = "Copy Entire Diff" };
        copyDiff.Click += (_, _) => CopyToClipboard(_diff?.PlainText);
        menu.Items.Add(copyDiff);

        var copyPath = new MenuFlyoutItem { Text = "Copy File Path" };
        copyPath.Click += (_, _) => CopyToClipboard(_diff?.Path);
        menu.Items.Add(copyPath);

        menu.Opening += (_, _) =>
        {
            copySelection.IsEnabled = !string.IsNullOrEmpty(TextLayer.SelectedText);
            copyDiff.IsEnabled = _diff != null;
            copyPath.IsEnabled = !string.IsNullOrEmpty(_diff?.Path);
        };

        ContextFlyout = menu;
    }

    private bool CopySelection()
    {
        var selected = TextLayer.SelectedText;
        if (string.IsNullOrEmpty(selected)) return false;
        CopyToClipboard(selected);
        return true;
    }

    private static void CopyToClipboard(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    // MARK: - Find

    private void OpenFind()
    {
        FindBar.Visibility = Visibility.Visible;
        FindTextBox.Focus(FocusState.Programmatic);
        FindTextBox.SelectAll();
    }

    private void CloseFind_Click(object sender, RoutedEventArgs e) => CloseFind();

    private void CloseFind()
    {
        FindBar.Visibility = Visibility.Collapsed;
        _matches.Clear();
        _currentMatch = -1;
        FindStatusText.Text = string.Empty;
        RepaintLayers(force: true);
    }

    private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e) => RunSearch();

    private void FindTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            StepMatch(+1);
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            CloseFind();
        }
    }

    private void FindNext_Click(object sender, RoutedEventArgs e) => StepMatch(+1);
    private void FindPrevious_Click(object sender, RoutedEventArgs e) => StepMatch(-1);

    private void RunSearch()
    {
        _matches.Clear();
        _currentMatch = -1;

        var query = FindTextBox.Text;
        if (_diff != null && !string.IsNullOrEmpty(query))
        {
            for (int i = 0; i < _renderedLineCount; i++)
            {
                if (_diff.Lines[i].RawText.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    _matches.Add(i);
                }
            }
        }

        if (_matches.Count > 0) _currentMatch = 0;
        UpdateFindStatus();
        ScrollToCurrentMatch();
    }

    private void StepMatch(int delta)
    {
        if (_matches.Count == 0) return;
        _currentMatch = (_currentMatch + delta + _matches.Count) % _matches.Count;
        UpdateFindStatus();
        ScrollToCurrentMatch();
    }

    private void UpdateFindStatus()
    {
        FindStatusText.Text = _matches.Count == 0
            ? (string.IsNullOrEmpty(FindTextBox.Text) ? string.Empty : "No results")
            : $"{_currentMatch + 1} of {_matches.Count}";
    }

    private void ScrollToCurrentMatch()
    {
        if (_currentMatch < 0 || _currentMatch >= _matches.Count)
        {
            RepaintLayers(force: true);
            return;
        }

        var y = _matches[_currentMatch] * DiffTextTheme.LineHeight;
        var target = Math.Max(0, y - Scroller.ViewportHeight / 3);
        Scroller.ChangeView(null, target, null, disableAnimation: true);
        RepaintLayers(force: true);
    }
}
