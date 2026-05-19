using BioAttendanceOrganizer.Core.Analysis;
using BioAttendanceOrganizer.Core.Import;

namespace BioAttendanceOrganizer.Tests;

public sealed class WorkbookSmokeTests
{
    [Fact]
    public void ImportsProvidedLegacyBiometricWorkbookWhenAvailable()
    {
        const string samplePath = @"C:\Users\johnc\Downloads\01-15 APRIL NS BIO ATTN (3).XLS";
        if (!File.Exists(samplePath))
        {
            return;
        }

        var workbook = new BiometricWorkbookParser().Parse(samplePath);
        var report = new AttendanceAnalyzer().Analyze(workbook);

        Assert.Equal(new DateOnly(2026, 4, 1), workbook.PeriodStart);
        Assert.Equal(new DateOnly(2026, 4, 16), workbook.PeriodEnd);
        Assert.True(workbook.Employees.Count >= 200);
        Assert.True(workbook.RawPunches.Count >= 2_000);
        Assert.True(report.Summary.IssueRows > 0);
    }

    [Fact]
    public void ImportsProvidedMorningEmployeeAttendanceWorkbookWhenAvailable()
    {
        const string samplePath = @"C:\Users\johnc\Downloads\1_(May)Employee Attendance Record.xls";
        if (!File.Exists(samplePath))
        {
            return;
        }

        var workbook = new BiometricWorkbookParser().Parse(samplePath);
        var report = new AttendanceAnalyzer().Analyze(workbook);

        Assert.Equal(new DateOnly(2026, 5, 1), workbook.PeriodStart);
        Assert.Equal(new DateOnly(2026, 5, 9), workbook.PeriodEnd);
        Assert.True(workbook.Employees.Count > 0);
        Assert.True(workbook.RawPunches.Count > 0);
        Assert.True(report.Records.Count > 0);
    }
}
