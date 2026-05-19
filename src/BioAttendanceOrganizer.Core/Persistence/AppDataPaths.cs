namespace BioAttendanceOrganizer.Core.Persistence;

public static class AppDataPaths
{
    public static string DefaultDatabasePath()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BioAttendanceOrganizer");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "attendance-review.db");
    }

    public static string ImportCacheFolder()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BioAttendanceOrganizer",
            "imports");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
