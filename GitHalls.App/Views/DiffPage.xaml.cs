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

    public static Microsoft.UI.Xaml.Visibility GetVisibility(bool isVisible) => isVisible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public string AdditionsText
    {
        get => (string)GetValue(AdditionsTextProperty);
        set => SetValue(AdditionsTextProperty, value);
    }
    public static readonly DependencyProperty AdditionsTextProperty = DependencyProperty.Register(nameof(AdditionsText), typeof(string), typeof(DiffPage), new PropertyMetadata(""));

    public string DeletionsText
    {
        get => (string)GetValue(DeletionsTextProperty);
        set => SetValue(DeletionsTextProperty, value);
    }
    public static readonly DependencyProperty DeletionsTextProperty = DependencyProperty.Register(nameof(DeletionsText), typeof(string), typeof(DiffPage), new PropertyMetadata(""));

    public Visibility HasStats
    {
        get => (Visibility)GetValue(HasStatsProperty);
        set => SetValue(HasStatsProperty, value);
    }
    public static readonly DependencyProperty HasStatsProperty = DependencyProperty.Register(nameof(HasStats), typeof(Visibility), typeof(DiffPage), new PropertyMetadata(Visibility.Collapsed));

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
            HasStats = Visibility.Collapsed;
            DiffLines.Clear();
            return;
        }

        FilePath = diff.FilePath;
        
        if (diff.IsBinary)
        {
            HasStats = Visibility.Collapsed;
        }
        else
        {
            AdditionsText = $"+{diff.Additions}";
            DeletionsText = $"-{diff.Deletions}";
            HasStats = Visibility.Visible;
        }

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
