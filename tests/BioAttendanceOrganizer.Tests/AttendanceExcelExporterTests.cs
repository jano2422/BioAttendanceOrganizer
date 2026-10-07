using BioAttendanceOrganizer.Core.Export;
using BioAttendanceOrganizer.Core.Models;
using BioAttendanceOrganizer.Core.Services;
using ClosedXML.Excel;

namespace BioAttendanceOrganizer.Tests;

public sealed class AttendanceExcelExporterTests
{
    [Fact]
    public void ExportCreatesSingleEmployeeAttendanceRecordSheet()
    {
        var report = Report(
            new AttendanceRecord
            {
                EmployeeId = "1",
                EmployeeName = "Sample Employee",
                Department = "Security",
                WorkDate = new DateOnly(2026, 5, 1),
                TimeIn = new DateTime(2026, 5, 1, 5, 13, 0),
                TimeOut = new DateTime(2026, 5, 1, 19, 13, 0),
                Status = AttendanceStatus.CleanDayShift
            },
            new AttendanceRecord
            {
                EmployeeId = "1",
                EmployeeName = "Sample Employee",
                Department = "Security",
                WorkDate = new DateOnly(2026, 5, 2),
                Status = AttendanceStatus.NoRecord
            });
        var path = Path.Combine(Path.GetTempPath(), $"dtr-export-{Guid.NewGuid():N}.xlsx");

        new AttendanceExcelExporter().Export(report, path, DtrImportSlot.Morning, "May 1-15, 2026");

        using var workbook = new XLWorkbook(path);
        Assert.Single(workbook.Worksheets);
        var sheet = workbook.Worksheet(AttendanceExcelExporter.SheetName);
        Assert.Equal("DTR Attendance Export", sheet.Name);
        Assert.Equal("Employee Attendance Record", sheet.Cell(2, 1).GetString());
        Assert.True(sheet.Protection.IsProtected);
        Assert.Equal("User ID: 1", sheet.Cell(7, 1).GetString());
        Assert.Equal("Name: Sample Employee", sheet.Cell(7, 4).GetString());
        Assert.Equal("Department: Security", sheet.Cell(7, 7).GetString());
        Assert.Equal(1, sheet.Cell(8, 1).GetValue<int>());
        Assert.Equal(2, sheet.Cell(8, 2).GetValue<int>());
    }

    [Fact]
    public void CompleteDayShowsInOutAndHours()
    {
        var report = Report(
            new AttendanceRecord
            {
                EmployeeId = "2",
                EmployeeName = "Day Employee",
                Department = "Office",
                WorkDate = new DateOnly(2026, 5, 1),
                TimeIn = new DateTime(2026, 5, 1, 5, 13, 0),
                TimeOut = new DateTime(2026, 5, 1, 19, 13, 0),
                Status = AttendanceStatus.CleanDayShift
            });
        var path = Path.Combine(Path.GetTempPath(), $"dtr-export-{Guid.NewGuid():N}.xlsx");

        new AttendanceExcelExporter().Export(report, path, DtrImportSlot.Morning, "May 1-15, 2026");

        using var workbook = new XLWorkbook(path);
        var cell = workbook.Worksheet(AttendanceExcelExporter.SheetName).Cell(9, 1).GetString();
        Assert.Contains("IN 05:13", cell);
        Assert.Contains("OUT 19:13", cell);
        Assert.Contains("H 14.00", cell);
        Assert.DoesNotContain("Day Shift", cell);
    }

    [Fact]
    public void NightShiftOutStaysInWorkDateColumnWithActualDates()
    {
        var report = Report(
            new AttendanceRecord
            {
                EmployeeId = "3",
                EmployeeName = "Night Employee",
                Department = "Security",
                WorkDate = new DateOnly(2026, 4, 1),
                TimeIn = new DateTime(2026, 4, 1, 20, 0, 0),
                TimeOut = new DateTime(2026, 4, 2, 6, 0, 0),
                Status = AttendanceStatus.LikelyNightShift,
                Flags = [IssueFlag.LikelyNightShift]
            },
            new AttendanceRecord
            {
                EmployeeId = "3",
                EmployeeName = "Night Employee",
                Department = "Security",
                WorkDate = new DateOnly(2026, 4, 2),
                Status = AttendanceStatus.NoRecord
            });
        var path = Path.Combine(Path.GetTempPath(), $"dtr-export-{Guid.NewGuid():N}.xlsx");

        new AttendanceExcelExporter().Export(report, path, DtrImportSlot.Night, "April 1-15, 2026");

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet(AttendanceExcelExporter.SheetName);
        var workDateCell = sheet.Cell(9, 1).GetString();
        var nextDateCell = sheet.Cell(9, 2).GetString();
        Assert.Contains("IN 04-01 20:00", workDateCell);
        Assert.Contains("OUT 04-02 06:00", workDateCell);
        Assert.Contains("H 10.00", workDateCell);
        Assert.DoesNotContain("Night Shift", workDateCell);
        Assert.DoesNotContain("+1", workDateCell);
        Assert.Equal(string.Empty, nextDateCell);
    }

    [Fact]
    public void IncompleteRecordsExportAsBlankWithoutReviewText()
    {
        var report = Report(
            new AttendanceRecord
            {
                EmployeeId = "4",
                EmployeeName = "Incomplete Employee",
                Department = "Office",
                WorkDate = new DateOnly(2026, 5, 1),
                TimeIn = new DateTime(2026, 5, 1, 8, 0, 0),
                Status = AttendanceStatus.MissingOut,
                Flags = [IssueFlag.MissingOut]
            });
        var path = Path.Combine(Path.GetTempPath(), $"dtr-export-{Guid.NewGuid():N}.xlsx");

        new AttendanceExcelExporter().Export(report, path, DtrImportSlot.Morning, "May 1-15, 2026");

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet(AttendanceExcelExporter.SheetName);
        Assert.Equal(string.Empty, sheet.Cell(9, 1).GetString());

        var exportedText = string.Join(" ", sheet.CellsUsed().Select(cell => cell.GetString()));
        Assert.DoesNotContain("Missing", exportedText);
        Assert.DoesNotContain("Needs Review", exportedText);
        Assert.DoesNotContain("Needs Checking", exportedText);
        Assert.DoesNotContain("Flags", exportedText);
        Assert.DoesNotContain("Status", exportedText);
        Assert.DoesNotContain("Summary", exportedText);
        Assert.DoesNotContain("DTR Rows", exportedText);
        Assert.DoesNotContain("+1", exportedText);
    }

    [Fact]
    public void ExportedWorkbookCannotBeImportedAsDtrSource()
    {
        var report = Report(
            new AttendanceRecord
            {
                EmployeeId = "5",
                EmployeeName = "Export Employee",
                Department = "Office",
                WorkDate = new DateOnly(2026, 5, 1),
                TimeIn = new DateTime(2026, 5, 1, 8, 0, 0),
                TimeOut = new DateTime(2026, 5, 1, 17, 0, 0),
                Status = AttendanceStatus.CleanDayShift
            });
        var path = Path.Combine(Path.GetTempPath(), $"dtr-export-{Guid.NewGuid():N}.xlsx");

        new AttendanceExcelExporter().Export(report, path, DtrImportSlot.Morning, "May 1-15, 2026");

        var ex = Assert.Throws<InvalidOperationException>(() => new DtrImportService().ImportWorkbook(path));
        Assert.Contains("workbook format is not supported", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(719, false, true)]
    [InlineData(720, false, false)]
    [InlineData(840, false, false)]
    [InlineData(719, true, true)]
    [InlineData(720, true, false)]
    public void ExportColorsOnlyHoursBelowTwelveRed(int minutes, bool corrected, bool expectedRed)
    {
        var timeIn = new DateTime(2026, 5, 1, 20, 0, 0);
        var record = new AttendanceRecord
        {
            EmployeeId = "6",
            EmployeeName = "Hours Employee",
            WorkDate = DateOnly.FromDateTime(timeIn),
            TimeIn = timeIn,
            TimeOut = timeIn.AddMinutes(corrected ? 840 : minutes),
            CorrectionAction = corrected ? CorrectionAction.EditTimes : CorrectionAction.None,
            CorrectedTimeIn = corrected ? timeIn : null,
            CorrectedTimeOut = corrected ? timeIn.AddMinutes(minutes) : null,
            Status = AttendanceStatus.LikelyNightShift
        };
        var path = Path.Combine(Path.GetTempPath(), $"dtr-export-{Guid.NewGuid():N}.xlsx");

        try
        {
            new AttendanceExcelExporter().Export(Report(record), path, DtrImportSlot.Night, "May 1-15, 2026");

            using var workbook = new XLWorkbook(path);
            var cell = workbook.Worksheet(AttendanceExcelExporter.SheetName).Cell(9, 1);
            var runs = cell.GetRichText().ToList();
            var hoursRun = Assert.Single(runs, run => run.Text.StartsWith("H "));
            Assert.Equal("H " + record.DurationText, hoursRun.Text);
            Assert.Equal(expectedRed, hoursRun.FontColor.Equals(XLColor.Red));
            Assert.All(runs.Where(run => !run.Text.StartsWith("H ")),
                run => Assert.NotEqual(XLColor.Red, run.FontColor));
            Assert.Contains("IN 05-01 20:00", cell.GetString());
            Assert.Contains("OUT 05-02", cell.GetString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MultipleRecordsInOneCellKeepIndependentHoursColors()
    {
        var timeIn = new DateTime(2026, 5, 1, 0, 0, 0);
        var report = Report(new[] { 6, 12 }.Select(hours => new AttendanceRecord
        {
            EmployeeId = "7",
            EmployeeName = "Multiple Records Employee",
            WorkDate = DateOnly.FromDateTime(timeIn),
            TimeIn = timeIn,
            TimeOut = timeIn.AddHours(hours),
            Status = AttendanceStatus.CleanDayShift
        }).ToArray());
        var path = Path.Combine(Path.GetTempPath(), $"dtr-export-{Guid.NewGuid():N}.xlsx");

        try
        {
            new AttendanceExcelExporter().Export(report, path, DtrImportSlot.Morning, "May 1-15, 2026");

            using var workbook = new XLWorkbook(path);
            var cell = workbook.Worksheet(AttendanceExcelExporter.SheetName).Cell(9, 1);
            var hoursRuns = cell.GetRichText().Where(run => run.Text.StartsWith("H ")).ToList();
            Assert.Equal(2, hoursRuns.Count);
            Assert.Equal("H 6.00", hoursRuns[0].Text);
            Assert.Equal(XLColor.Red, hoursRuns[0].FontColor);
            Assert.Equal("H 12.00", hoursRuns[1].Text);
            Assert.NotEqual(XLColor.Red, hoursRuns[1].FontColor);
            Assert.Contains("H 6.00" + Environment.NewLine + Environment.NewLine + "IN", cell.GetString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static AttendanceReport Report(params AttendanceRecord[] records)
    {
        var employees = records
            .GroupBy(x => new { x.EmployeeId, x.EmployeeName, x.Department })
            .Select(x => new EmployeeInfo(x.Key.EmployeeId, x.Key.EmployeeName, x.Key.Department))
            .ToList();
        var start = records.Min(x => x.WorkDate);
        var end = records.Max(x => x.WorkDate);
        var workbook = new BiometricWorkbook("export-test.xls", start, end, employees, []);
        var summary = AttendanceSummary.From(start, end, employees.Count, 0, records);
        return new AttendanceReport(workbook, records, summary);
    }
}
