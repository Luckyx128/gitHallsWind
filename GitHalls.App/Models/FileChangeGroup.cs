using GitHalls.Core.Models;
using System.Collections.Generic;

namespace GitHalls.App.Models;

public class FileChangeGroup : List<FileChange>
{
    public string Name { get; }

    public FileChangeGroup(string name, IEnumerable<FileChange> items) : base(items)
    {
        Name = name;
    }
}
