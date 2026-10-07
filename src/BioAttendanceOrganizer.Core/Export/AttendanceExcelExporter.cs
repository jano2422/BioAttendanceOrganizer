using System.Globalization;
using BioAttendanceOrganizer.Core.Models;
using ClosedXML.Excel;

namespace BioAttendanceOrganizer.Core.Export;

public sealed class AttendanceExcelExporter
{
    public const string SheetName = "DTR Attendance Export";
    private const string ExportProtectionPassword = "BioAttendanceOrganizer";
    private const int FirstDateColumn = 1;
    private const int FirstEmployeeRow = 7;

    public void Export(AttendanceReport report, string outputPath, DtrImportSlot slot, string cutoffLabel)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Choose an export file path.", nameof(outputPath));
        }

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var workbook = new XLWorkbook();
        AddAttendanceRecordSheet(workbook, report, slot);
        workbook.SaveAs(outputPath);
    }

    private static void AddAttendanceRecordSheet(XLWorkbook workbook, AttendanceReport report, DtrImportSlot slot)
    {
        var sheet = workbook.Worksheets.Add(SheetName);
        var dates = EnumerateDates(report.Summary.PeriodStart, report.Summary.PeriodEnd).ToList();
        var lastDateColumn = FirstDateColumn + Math.Max(dates.Count, 9) - 1;

        sheet.Style.Font.SetFontName("Calibri");
        sheet.Style.Font.SetFontSize(10);
        sheet.Cell(2, FirstDateColumn).Value = "Employee Attendance Record";
        sheet.Range(2, FirstDateColumn, 2, lastDateColumn).Merge().Style
            .Font.SetBold()
            .Font.SetFontSize(18)
            .Font.SetFontColor(XLColor.Blue)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        sheet.Cell(3, FirstDateColumn).Value = $"Attendance date:{report.Summary.PeriodStart:yyyy-MM-dd}~{report.Summary.PeriodEnd:yyyy-MM-dd}";
        sheet.Cell(4, FirstDateColumn).Value = $"Tabling date:{DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        sheet.Cell(5, FirstDateColumn).Value = $"Shift:{FormatSlot(slot)}";
        sheet.Range(3, FirstDateColumn, 5, lastDateColumn).Style
            .Font.SetFontColor(XLColor.Blue)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        var rowIndex = FirstEmployeeRow;
        foreach (var employeeGroup in report.Records
                     .GroupBy(x => new { x.EmployeeId, x.EmployeeName, x.Department })
                     .OrderBy(x => TryParseEmployeeId(x.Key.EmployeeId))
                     .ThenBy(x => x.Key.EmployeeName))
        {
            WriteEmployeeBlock(
                sheet,
                rowIndex,
                lastDateColumn,
                dates,
                employeeGroup.Key.EmployeeId,
                employeeGroup.Key.EmployeeName,
                employeeGroup.Key.Department,
                employeeGroup);
            rowIndex += 3;
        }

        sheet.Columns(FirstDateColumn, lastDateColumn).Width = 14;
        sheet.Rows(FirstEmployeeRow, Math.Max(FirstEmployeeRow, rowIndex - 1)).Height = 24;
        for (var row = FirstEmployeeRow + 2; row < rowIndex; row += 3)
        {
            sheet.Row(row).Height = 56;
        }

        var usedRange = sheet.Range(FirstEmployeeRow, FirstDateColumn, Math.Max(FirstEmployeeRow, rowIndex - 1), lastDateColumn);
        usedRange.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
        usedRange.Style.Border.SetInsideBorderColor(XLColor.Blue);
        usedRange.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        usedRange.Style.Border.SetOutsideBorderColor(XLColor.Blue);
        usedRange.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        usedRange.Style.Alignment.SetWrapText();

        sheet.SheetView.FreezeRows(6);
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.CenterHorizontally = true;
        sheet.Protect(ExportProtectionPassword);
    }

    private static void WriteEmployeeBlock(
        IXLWorksheet sheet,
        int row,
        int lastDateColumn,
        IReadOnlyList<DateOnly> dates,
        string employeeId,
        string employeeName,
        string department,
        IEnumerable<AttendanceRecord> records)
    {
        var idEnd = Math.Max(FirstDateColumn, lastDateColumn / 3);
        var nameStart = Math.Min(lastDateColumn, idEnd + 1);
        var nameEnd = Math.Max(nameStart, lastDateColumn * 2 / 3);
        var departmentStart = Math.Min(lastDateColumn, nameEnd + 1);

        SetMergedValue(sheet, row, FirstDateColumn, idEnd, $"User ID: {employeeId}");
        SetMergedValue(sheet, row, nameStart, nameEnd, $"Name: {employeeName}");
        SetMergedValue(sheet, row, departmentStart, lastDateColumn, $"Department: {department}");
        sheet.Range(row, FirstDateColumn, row, lastDateColumn).Style
            .Font.SetBold()
            .Font.SetFontColor(XLColor.Blue)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        for (var index = 0; index < dates.Count; index++)
        {
            var column = FirstDateColumn + index;
            sheet.Cell(row + 1, column).Value = dates[index].Day;
            sheet.Cell(row + 1, column).Style
                .Font.SetBold()
                .Font.SetFontColor(XLColor.Blue)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        }

        var recordsByDate = records
            .GroupBy(x => x.WorkDate)
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(record => record.FinalTimeIn ?? record.FinalTimeOut ?? record.WorkDate.ToDateTime(TimeOnly.MinValue)).ToList());

        for (var index = 0; index < dates.Count; index++)
        {
            if (!recordsByDate.TryGetValue(dates[index], out var dayRecords))
            {
                continue;
            }

            var completeRecords = dayRecords
                .Where(record => record.FinalTimeIn.HasValue && record.FinalTimeOut.HasValue && record.DurationHours.HasValue)
                .ToList();
            if (completeRecords.Count == 0)
            {
                continue;
            }

            var cell = sheet.Cell(row + 2, FirstDateColumn + index);
            var richText = cell.CreateRichText();
            for (var recordIndex = 0; recordIndex < completeRecords.Count; recordIndex++)
            {
                if (recordIndex > 0)
                {
                    richText.AddText(Environment.NewLine + Environment.NewLine);
                }

                AppendDayRecord(richText, completeRecords[recordIndex]);
            }
            cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            cell.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            cell.Style.Alignment.SetWrapText();
        }
    }

    private static void SetMergedValue(IXLWorksheet sheet, int row, int firstColumn, int lastColumn, string value)
    {
        if (firstColumn > lastColumn)
        {
            return;
        }

        if (firstColumn == lastColumn)
        {
            sheet.Cell(row, firstColumn).Value = value;
            return;
        }

        var range = sheet.Range(row, firstColumn, row, lastColumn);
        range.Merge();
        range.FirstCell().Value = value;
    }

    private static void AppendDayRecord(IXLRichText richText, AttendanceRecord record)
    {
        var timeIn = record.FinalTimeIn!.Value;
        var timeOut = record.FinalTimeOut!.Value;
        var hours = record.DurationHours!.Value.ToString("0.00", CultureInfo.InvariantCulture);
        var crossesDate = timeIn.Date != timeOut.Date;
        richText.AddText(string.Join(
            Environment.NewLine,
            "IN " + FormatTime(timeIn, crossesDate),
            "OUT " + FormatTime(timeOut, crossesDate)) + Environment.NewLine);
        var hoursText = richText.AddText("H " + hours);
        if (record.DurationHours.Value < 12)
        {
            hoursText.SetFontColor(XLColor.Red);
        }
    }

    private static string FormatTime(DateTime value, bool includeDate)
    {
        return value.ToString(includeDate ? "MM-dd HH:mm" : "HH:mm", CultureInfo.InvariantCulture);
    }

    private static IEnumerable<DateOnly> EnumerateDates(DateOnly start, DateOnly end)
    {
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            yield return date;
        }
    }

    private static int TryParseEmployeeId(string employeeId)
    {
        return int.TryParse(employeeId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : int.MaxValue;
    }

    private static string FormatSlot(DtrImportSlot slot)
    {
        return slot == DtrImportSlot.Morning ? "Morning Shift" : "Night Shift";
    }
}
