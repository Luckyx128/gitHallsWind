using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using GitHalls.Core.Models;
using System.Collections.ObjectModel;

namespace GitHalls.App.Views;

public sealed partial class DiffPage : Page
{
    public string FilePath
    {
        get => (string)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    public static readonly DependencyProperty FilePathProperty =
        DependencyProperty.Register(nameof(FilePath), typeof(string), typeof(DiffPage), new PropertyMetadata(""));

    public ObservableCollection<DiffLine> DiffLines { get; } = new();

    public DiffPage()
    {
        InitializeComponent();
    }

    public void UpdateDiff(FileDiff diff)
    {
        if (diff == null)
        {
            FilePath = "";
            DiffLines.Clear();
            return;
        }

        FilePath = diff.FilePath;
        DiffLines.Clear();

        if (diff.IsBinary)
        {
            DiffLines.Add(new DiffLine("Binary files differ.", DiffLineType.Context, null, null));
        }
        else
        {
            foreach (var line in diff.Lines)
            {
                DiffLines.Add(line);
            }
        }
    }
}
