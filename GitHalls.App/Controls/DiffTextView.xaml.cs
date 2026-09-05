using GitHalls.App.Services;
using GitHalls.App.Themes;
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

    private readonly IDiffHighlighter _highlighter;

    private HighlightedDiff? _diff;
    private DiffTextTheme _theme = DiffTextTheme.Light;

    private int _renderedLineCount;
    private double _gutterWidth;
    private double _numberColumnWidth;
    private double _contentWidth;

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

        if (diff == null || diff.Lines.Count == 0)
        {
            _diff = null;
            TextLayer.Blocks.Clear();
            TintLayer.Children.Clear();
            GutterLayer.Children.Clear();
            ShowRemainingButton.Visibility = Visibility.Collapsed;
            _renderedLineCount = 0;
            ApplyIntrinsicHeight();
            return;
        }

        _diff = DiffHighlightMapper.Make(diff, _highlighter);
        Render(Math.Min(_diff.Lines.Count, MaxRenderedLines));
    }

    private void Render(int lineCount)
    {
        if (_diff == null) return;

        _renderedLineCount = lineCount;
        MeasureGutter();
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
        _gutterWidth = Math.Ceiling(GutterPadding + MarkerWidth + ColumnGap + _numberColumnWidth + ColumnGap + _numberColumnWidth + GutterPadding);
    }

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
    }

    // MARK: - Tint + gutter painting

    private void Scroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        // The gutter lives inside the scrolled content, so it is pushed back by
        // exactly the horizontal offset to stay pinned at the left edge.
        GutterTransform.X = Scroller.HorizontalOffset;
        RepaintLayers(force: false);
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
            PaintGutterRow(line, y);
        }
    }

    private void PaintTint(HighlightedDiffLine line, int index, double y)
    {
        var isMatch = _currentMatch >= 0 && _currentMatch < _matches.Count && _matches[_currentMatch] == index;
        var color = isMatch ? _theme.SearchHighlight : _theme.LineBackground(line.Type);
        if (color == null) return;

        // A hunk header reads as a full-width band; +/- tints start after the
        // gutter so the numbers keep their own backdrop.
        var left = line.Type == DiffLineType.HunkHeader ? 0 : _gutterWidth;

        var rectangle = new Rectangle
        {
            Width = Math.Max(_contentWidth - left, 0),
            Height = DiffTextTheme.LineHeight,
            Fill = new SolidColorBrush(color.Value)
        };
        Canvas.SetLeft(rectangle, left);
        Canvas.SetTop(rectangle, y);
        TintLayer.Children.Add(rectangle);
    }

    private void PaintGutterRow(HighlightedDiffLine line, double y)
    {
        if (line.Type == DiffLineType.HunkHeader) return;

        var markerX = GutterPadding;
        var oldColumnX = markerX + MarkerWidth + ColumnGap;
        var newColumnX = oldColumnX + _numberColumnWidth + ColumnGap;

        AddNumber(line.OldLineNumber, oldColumnX, y);
        AddNumber(line.NewLineNumber, newColumnX, y);

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
