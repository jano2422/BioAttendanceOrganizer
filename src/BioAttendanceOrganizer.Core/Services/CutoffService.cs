using BioAttendanceOrganizer.Core.Models;

namespace BioAttendanceOrganizer.Core.Services;

public static class CutoffService
{
    public static IReadOnlyList<CutoffPeriod> GenerateMonth(int year, int month)
    {
        return
        [
            CutoffPeriod.Create(year, month, 1),
            CutoffPeriod.Create(year, month, 2)
        ];
    }

    public static CutoffPeriod ForDate(DateOnly date)
    {
        return CutoffPeriod.Create(date.Year, date.Month, date.Day <= 15 ? 1 : 2);
    }
}
