using System.Security.Cryptography;
using System.Text;
using BioAttendanceOrganizer.Core.Models;

namespace BioAttendanceOrganizer.Core.Review;

public static class CorrectionKey
{
    public static string ForWorkbook(BiometricWorkbook workbook)
    {
        var builder = new StringBuilder();
        builder.Append(workbook.PeriodStart.ToString("yyyy-MM-dd"));
        builder.Append('|');
        builder.Append(workbook.PeriodEnd.ToString("yyyy-MM-dd"));
        foreach (var punch in workbook.RawPunches.OrderBy(x => x.EmployeeId).ThenBy(x => x.Timestamp))
        {
            builder.Append('|');
            builder.Append(punch.EmployeeId);
            builder.Append('@');
            builder.Append(punch.Timestamp.ToString("yyyy-MM-dd HH:mm"));
        }

        return Hash(builder.ToString());
    }

    public static string ForRecord(AttendanceRecord record)
    {
        return Hash(string.Join(
            "|",
            record.EmployeeId,
            record.WorkDate.ToString("yyyy-MM-dd"),
            record.RawPunches,
            record.Status.ToString()));
    }

    private static string Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
