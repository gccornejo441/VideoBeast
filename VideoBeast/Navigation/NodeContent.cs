using Microsoft.UI.Xaml.Controls;

using Windows.Storage;

namespace VideoBeast.Navigation;

public sealed class NodeContent
{
    public string Name { get; }
    public Symbol Icon { get; }
    public StorageFolder? Folder { get; }
    public StorageFile? File { get; }

    public NodeContent(string name,Symbol icon,StorageFolder? folder,StorageFile? file)
    {
        Name = name;
        Icon = icon;
        Folder = folder;
        File = file;
    }

    public override string ToString() => Name;
}
