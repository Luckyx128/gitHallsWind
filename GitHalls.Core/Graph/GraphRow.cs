using System.ComponentModel;
using System.Runtime.CompilerServices;
using GitHalls.Core.Models;

namespace GitHalls.Core.Graph;

public class GraphRow : INotifyPropertyChanged
{
    public Commit Commit { get; }
    public int TrackIndex { get; }
    public int ColorIndex { get; }
    public IReadOnlyList<GraphTrack> TopTracks { get; }
    public IReadOnlyList<GraphEdge> BottomEdges { get; }
    public int TrackCount { get; }

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();
            }
        }
    }
    
    // We can't reference GitTreeFileNode here because it's in App namespace, so we make it object.
    // Wait, GraphRow is in Core namespace. App models can't be referenced.
    // Instead of object, we can use IEnumerable.
    private object? _treeFiles;
    public object? TreeFiles
    {
        get => _treeFiles;
        set
        {
            if (_treeFiles != value)
            {
                _treeFiles = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public GraphRow(Commit commit, int trackIndex, int colorIndex, IReadOnlyList<GraphTrack> topTracks, IReadOnlyList<GraphEdge> bottomEdges, int trackCount)
    {
        Commit = commit;
        TrackIndex = trackIndex;
        ColorIndex = colorIndex;
        TopTracks = topTracks;
        BottomEdges = bottomEdges;
        TrackCount = trackCount;
    }
}
