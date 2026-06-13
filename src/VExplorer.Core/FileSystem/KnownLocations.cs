namespace VExplorer.Core.FileSystem;

/// <summary>
/// The fixed shell-namespace <see cref="Location"/>s that form the roots of
/// VExplorer's view: This PC (the visual root), the Recycle Bin and Network.
/// Parsing names are the Windows-native <c>::{CLSID}</c> identities; the
/// <c>KNOWNFOLDERID</c>s let the Shell layer fetch the real Explorer icons.
/// </summary>
public static class KnownLocations
{
    // ::{CLSID} parsing names recognized by SHParseDisplayName.
    private const string PcParsingName = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
    private const string RecycleBinParsingName = "::{645FF040-5081-101B-9F08-00AA002F954E}";
    private const string NetworkParsingName = "::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}";

    // KNOWNFOLDERIDs (for icon resolution via SHGetKnownFolderIDList).
    private static readonly Guid FolderIdComputer = new("0AC0837C-BBF8-452A-850D-79D08E667CA7");
    private static readonly Guid FolderIdRecycleBin = new("B7534046-3ECB-4C18-BE4E-64CD4CB7D6AC");
    private static readonly Guid FolderIdNetwork = new("D20BEEC4-5CA8-4905-AE3B-BF251EA09B53");

    /// <summary>The visual root and startup location.</summary>
    public static readonly Location Pc = Location.ForShell(PcParsingName, "PC", FolderIdComputer);

    public static readonly Location RecycleBin = Location.ForShell(
        RecycleBinParsingName,
        "Recycle Bin",
        FolderIdRecycleBin
    );

    public static readonly Location Network = Location.ForShell(
        NetworkParsingName,
        "Network",
        FolderIdNetwork
    );

    public static bool IsPc(Location location)
    {
        return location.Equals(Pc);
    }

    public static bool IsRecycleBin(Location location)
    {
        return location.Equals(RecycleBin);
    }

    public static bool IsNetwork(Location location)
    {
        return location.Equals(Network);
    }
}
