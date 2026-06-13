namespace VExplorer.Core.FileSystem;

public readonly record struct DirectoryPath(string Value)
{
    public static readonly DirectoryPath PC = new("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}");

    public override string ToString()
    {
        return Value;
    }
}
