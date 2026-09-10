using GitHalls.Core.Models;

namespace GitHalls.Core.Graph;

public static class GraphBuilder
{
    public static IReadOnlyList<GraphRow> Build(IReadOnlyList<Commit> commits)
    {
        var rows = new List<GraphRow>(commits.Count);
        
        var activeTracks = new List<string?>(); // Hash expected on this track
        var trackColors = new List<int>();
        int nextColorIndex = 0;

        foreach (var commit in commits)
        {
            var topTracks = new List<GraphTrack>();
            for (int i = 0; i < activeTracks.Count; i++)
            {
                if (activeTracks[i] != null)
                {
                    topTracks.Add(new GraphTrack(i, trackColors[i]));
                }
            }

            int trackIndex = activeTracks.IndexOf(commit.Hash);
            const int MaxTracks = 8;
            
            if (trackIndex == -1)
            {
                // New track for this commit
                trackIndex = activeTracks.IndexOf(null);
                if (trackIndex == -1)
                {
                    if (activeTracks.Count < MaxTracks)
                    {
                        trackIndex = activeTracks.Count;
                        activeTracks.Add(commit.Hash);
                        trackColors.Add(nextColorIndex++);
                    }
                    else
                    {
                        // Overflow: replace the last track or just reuse it
                        trackIndex = MaxTracks - 1;
                        activeTracks[trackIndex] = commit.Hash;
                    }
                }
                else
                {
                    activeTracks[trackIndex] = commit.Hash;
                    trackColors[trackIndex] = nextColorIndex++;
                }
            }

            int commitColor = trackColors[trackIndex];
            var bottomEdges = new List<GraphEdge>();

            // Edges for passing tracks
            for (int i = 0; i < activeTracks.Count; i++)
            {
                if (i != trackIndex && activeTracks[i] != null)
                {
                    bottomEdges.Add(new GraphEdge(i, i, trackColors[i]));
                }
            }

            // Edges for parents
            if (commit.Parents.Count > 0)
            {
                var firstParent = commit.Parents[0];
                int existingTrack = activeTracks.IndexOf(firstParent);

                if (existingTrack == -1 || existingTrack == trackIndex)
                {
                    activeTracks[trackIndex] = firstParent;
                    bottomEdges.Add(new GraphEdge(trackIndex, trackIndex, commitColor));
                }
                else
                {
                    // Merge into existing track and free this one
                    bottomEdges.Add(new GraphEdge(trackIndex, existingTrack, trackColors[existingTrack]));
                    activeTracks[trackIndex] = null;
                }

                for (int i = 1; i < commit.Parents.Count; i++)
                {
                    var parentHash = commit.Parents[i];
                    int parentTrack = activeTracks.IndexOf(parentHash);
                    if (parentTrack == -1)
                    {
                        parentTrack = activeTracks.IndexOf(null);
                        if (parentTrack == -1)
                        {
                            if (activeTracks.Count < MaxTracks)
                            {
                                parentTrack = activeTracks.Count;
                                activeTracks.Add(parentHash);
                                trackColors.Add(nextColorIndex++);
                            }
                            else
                            {
                                parentTrack = MaxTracks - 1;
                                activeTracks[parentTrack] = parentHash;
                            }
                        }
                        else
                        {
                            activeTracks[parentTrack] = parentHash;
                            trackColors[parentTrack] = nextColorIndex++;
                        }
                    }
                    bottomEdges.Add(new GraphEdge(trackIndex, parentTrack, trackColors[parentTrack]));
                }
            }
            else
            {
                activeTracks[trackIndex] = null;
            }

            rows.Add(new GraphRow(commit, trackIndex, commitColor, topTracks, bottomEdges, activeTracks.Count));
        }

        return rows;
    }
}
