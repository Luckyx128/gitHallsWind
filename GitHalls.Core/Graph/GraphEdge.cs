namespace GitHalls.Core.Graph;

public class GraphEdge
{
    public int StartTrack { get; }
    public int EndTrack { get; }
    public int ColorIndex { get; }

    public GraphEdge(int startTrack, int endTrack, int colorIndex)
    {
        StartTrack = startTrack;
        EndTrack = endTrack;
        ColorIndex = colorIndex;
    }
}

public class GraphTrack
{
    public int Index { get; }
    public int ColorIndex { get; }

    public GraphTrack(int index, int colorIndex)
    {
        Index = index;
        ColorIndex = colorIndex;
    }
}
