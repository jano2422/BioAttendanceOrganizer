using System.Globalization;

namespace BioAttendanceOrganizer.Core.Models;

public sealed record CutoffPeriod(
    string Key,
    int Year,
    int Month,
    int CutoffNumber,
    DateOnly StartDate,
    DateOnly EndDate)
{
    public string DisplayName =>
        $"{CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(Month)} {StartDate.Day}-{EndDate.Day}, {Year}";

    public string ShortName => $"{Year:0000}-{Month:00} {(CutoffNumber == 1 ? "1st" : "2nd")} cutoff";

    public static CutoffPeriod Create(int year, int month, int cutoffNumber)
    {
        if (cutoffNumber is not 1 and not 2)
        {
            throw new ArgumentOutOfRangeException(nameof(cutoffNumber), "Cutoff number must be 1 or 2.");
        }

        var startDay = cutoffNumber == 1 ? 1 : 16;
        var endDay = cutoffNumber == 1 ? 15 : DateTime.DaysInMonth(year, month);
        var start = new DateOnly(year, month, startDay);
        var end = new DateOnly(year, month, endDay);
        var key = $"{year:0000}-{month:00}-{cutoffNumber}";

        return new CutoffPeriod(key, year, month, cutoffNumber, start, end);
    }

    public bool Contains(DateOnly date)
    {
        return date >= StartDate && date <= EndDate;
    }
}
