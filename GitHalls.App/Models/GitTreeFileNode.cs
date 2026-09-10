using System.Collections.ObjectModel;
using GitHalls.Core.Models;
using Microsoft.UI.Xaml.Controls;

namespace GitHalls.App.Models;

public class GitTreeFileNode
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public ObservableCollection<GitTreeFileNode> Children { get; } = new();
    
    public int Depth { get; set; }
    public Microsoft.UI.Xaml.Thickness Margin => new Microsoft.UI.Xaml.Thickness(Depth * 20, 0, 0, 0);

    // Only for files
    public CommitFile? File { get; set; }
    
    public string IconGlyph => IsFolder ? "\uE838" : "\uE8A5"; // Folder / Document icon
    
    public string AdditionsText => File?.AdditionsText ?? "";
    public string DeletionsText => File?.DeletionsText ?? "";
    public bool HasChanges => File != null;
}
