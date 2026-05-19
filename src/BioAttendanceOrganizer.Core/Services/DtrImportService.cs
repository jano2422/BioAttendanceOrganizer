using BioAttendanceOrganizer.Core.Import;
using BioAttendanceOrganizer.Core.Models;

namespace BioAttendanceOrganizer.Core.Services;

public sealed class DtrImportService
{
    private readonly BiometricWorkbookParser _parser;

    public DtrImportService(BiometricWorkbookParser? parser = null)
    {
        _parser = parser ?? new BiometricWorkbookParser();
    }

    public BiometricWorkbook ImportWorkbook(string path)
    {
        return _parser.Parse(path);
    }
}
