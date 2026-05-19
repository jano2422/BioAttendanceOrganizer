using System.Data;
using BioAttendanceOrganizer.Core.Import;
using BioAttendanceOrganizer.Core.Models;

namespace BioAttendanceOrganizer.Tests;

public sealed class BiometricWorkbookParserTests
{
    [Fact]
    public void ParsesMorningEmployeeAttendanceRecordFormat()
    {
        var dataSet = new DataSet();
        var sheet = CreateSheet("Employee Attendance Record", 26);
        dataSet.Tables.Add(sheet);

        AddRow(sheet, "Employee Attendance Record");
        AddRow(sheet);
        AddRow(sheet, "Attendance date:2026-05-01~2026-05-09");
        AddRow(sheet);
        AddRow(sheet, null, null, null, null, "User ID:", "1", null, null, null, null, "Name:", "ADMIN", null, null, null, null, null, null, null, null, null, null, "Department:", "AESIA");
        AddRow(sheet, null, 1, 2, 3);
        AddRow(sheet, null, "05:13 19:13", null, "05:24");

        var workbook = BiometricWorkbookParser.ParseDataSet("morning.xls", dataSet);

        Assert.Equal(new DateOnly(2026, 5, 1), workbook.PeriodStart);
        Assert.Equal(new DateOnly(2026, 5, 9), workbook.PeriodEnd);
        Assert.Equal(DtrImportSlot.Morning, workbook.DetectedSlot);
        var employee = Assert.Single(workbook.Employees);
        Assert.Equal("1", employee.Id);
        Assert.Equal("ADMIN", employee.Name);
        Assert.Equal("AESIA", employee.Department);
        Assert.Equal(3, workbook.RawPunches.Count);
        Assert.Contains(workbook.RawPunches, punch =>
            punch.EmployeeId == "1" &&
            punch.Timestamp == new DateTime(2026, 5, 1, 5, 13, 0) &&
            punch.RawCell == "05:13 19:13" &&
            punch.SourceSheet == "Employee Attendance Record" &&
            punch.SourceRow == 7 &&
            punch.SourceColumn == 2);
    }

    [Fact]
    public void MorningParserSkipsEmployeeBlocksWithoutIdOrName()
    {
        var dataSet = new DataSet();
        var sheet = CreateSheet("Employee Attendance Record", 16);
        dataSet.Tables.Add(sheet);

        AddRow(sheet, "Attendance date:2026-05-01~2026-05-09");
        AddRow(sheet, null, "User ID:", "2", null, "Name:", "Valid Employee", null, "Department:", "IT");
        AddRow(sheet, 1);
        AddRow(sheet, "08:00 17:00");
        AddRow(sheet, null, "User ID:", "3", null, "Name:", null, null, "Department:", "IT");
        AddRow(sheet, 1);
        AddRow(sheet, "08:00 17:00");

        var workbook = BiometricWorkbookParser.ParseDataSet("morning.xls", dataSet);

        var employee = Assert.Single(workbook.Employees);
        Assert.Equal("2", employee.Id);
        Assert.Equal("Valid Employee", employee.Name);
        Assert.Equal(2, workbook.RawPunches.Count);
        Assert.All(workbook.RawPunches, punch => Assert.Equal("2", punch.EmployeeId));
    }

    [Fact]
    public void ExistingLogsFormatStillParses()
    {
        var dataSet = new DataSet();
        var info = CreateSheet("Info", 1);
        var logs = CreateSheet("Logs", 12);
        var summary = CreateSheet("Summary", 3);
        dataSet.Tables.Add(info);
        dataSet.Tables.Add(logs);
        dataSet.Tables.Add(summary);

        AddRow(info, "2026-04-01~2026-04-02");
        AddRow(logs, null, 1, 2);
        AddRow(logs, "No :", null, "1", null, null, null, null, null, null, null, "Night Emp");
        AddRow(logs, null, "20:00", "06:00");
        AddRow(summary, "1", null, "Security");

        var workbook = BiometricWorkbookParser.ParseDataSet("night.xls", dataSet);

        Assert.Equal(DtrImportSlot.Night, workbook.DetectedSlot);
        var employee = Assert.Single(workbook.Employees);
        Assert.Equal("1", employee.Id);
        Assert.Equal("Night Emp", employee.Name);
        Assert.Equal("Security", employee.Department);
        Assert.Equal(2, workbook.RawPunches.Count);
        Assert.Contains(workbook.RawPunches, punch => punch.Timestamp == new DateTime(2026, 4, 1, 20, 0, 0));
        Assert.Contains(workbook.RawPunches, punch => punch.Timestamp == new DateTime(2026, 4, 2, 6, 0, 0));
    }

    private static DataTable CreateSheet(string name, int columnCount)
    {
        var table = new DataTable(name);
        for (var index = 0; index < columnCount; index++)
        {
            table.Columns.Add($"C{index}", typeof(object));
        }

        return table;
    }

    private static void AddRow(DataTable table, params object?[] values)
    {
        var row = table.NewRow();
        for (var index = 0; index < values.Length; index++)
        {
            row[index] = values[index] ?? DBNull.Value;
        }

        table.Rows.Add(row);
    }
}
