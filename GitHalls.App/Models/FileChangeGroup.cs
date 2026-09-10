using GitHalls.Core.Models;
using System.Collections.ObjectModel;

namespace GitHalls.App.Models;

public class FileChangeGroup : ObservableCollection<FileChange>
{
    public string Name { get; }

    public FileChangeGroup(string name)
    {
        Name = name;
    }
}
