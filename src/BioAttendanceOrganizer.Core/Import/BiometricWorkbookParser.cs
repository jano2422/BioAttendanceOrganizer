using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BioAttendanceOrganizer.Core.Models;
using ExcelDataReader;

namespace BioAttendanceOrganizer.Core.Import;

public sealed class BiometricWorkbookParser
{
    private static readonly Regex TimeRegex = new(@"\b([01]?\d|2[0-3]):[0-5]\d\b", RegexOptions.Compiled);
    private static readonly Regex PeriodRegex = new(
        @"(?<sy>\d{4})[/-](?<sm>\d{1,2})[/-](?<sd>\d{1,2})\s*~\s*(?:(?<ey>\d{4})[/-])?(?<em>\d{1,2})[/-](?<ed>\d{1,2})",
        RegexOptions.Compiled);

    static BiometricWorkbookParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public BiometricWorkbook Parse(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Biometric workbook was not found.", path);
        }

        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataTableConfiguration
            {
                UseHeaderRow = false
            }
        });

        return ParseDataSet(path, dataSet);
    }

    internal static BiometricWorkbook ParseDataSet(string path, DataSet dataSet)
    {
        var period = TryReadPeriod(dataSet) ??
                     throw new InvalidOperationException("The workbook period could not be read from the biometric file.");

        var logs = FindSheet(dataSet, "Logs");
        if (logs is not null)
        {
            var departments = TryReadDepartments(dataSet);
            var employees = ReadEmployees(logs, departments);
            var punches = ReadPunches(logs, departments, period.Start, period.End);

            return new BiometricWorkbook(path, period.Start, period.End, employees, punches, DtrImportSlot.Night);
        }

        var morning = FindSheet(dataSet, "Employee Attendance Record");
        if (morning is not null)
        {
            var employees = ReadMorningEmployees(morning);
            var punches = ReadMorningPunches(morning, period.Start, period.End);

            return new BiometricWorkbook(path, period.Start, period.End, employees, punches, DtrImportSlot.Morning);
        }

        throw new InvalidOperationException("The workbook format is not supported. Expected a 'Logs' sheet or an 'Employee Attendance Record' sheet.");
    }

    public static (DateOnly Start, DateOnly End)? TryParsePeriodText(string text)
    {
        var match = PeriodRegex.Match(text);
        if (!match.Success)
        {
            return null;
        }

        var startYear = int.Parse(match.Groups["sy"].Value, CultureInfo.InvariantCulture);
        var startMonth = int.Parse(match.Groups["sm"].Value, CultureInfo.InvariantCulture);
        var startDay = int.Parse(match.Groups["sd"].Value, CultureInfo.InvariantCulture);
        var endYear = match.Groups["ey"].Success
            ? int.Parse(match.Groups["ey"].Value, CultureInfo.InvariantCulture)
            : startYear;
        var endMonth = int.Parse(match.Groups["em"].Value, CultureInfo.InvariantCulture);
        var endDay = int.Parse(match.Groups["ed"].Value, CultureInfo.InvariantCulture);

        var start = new DateOnly(startYear, startMonth, startDay);
        var end = new DateOnly(endYear, endMonth, endDay);
        if (end < start)
        {
            end = end.AddYears(1);
        }

        return (start, end);
    }

    private static DataTable? FindSheet(DataSet dataSet, string name)
    {
        return dataSet.Tables
            .Cast<DataTable>()
            .FirstOrDefault(x => string.Equals(x.TableName, name, StringComparison.OrdinalIgnoreCase));
    }

    private static (DateOnly Start, DateOnly End)? TryReadPeriod(DataSet dataSet)
    {
        foreach (DataTable table in dataSet.Tables)
        {
            foreach (DataRow row in table.Rows)
            {
                foreach (var value in row.ItemArray)
                {
                    var text = CellText(value);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    var period = TryParsePeriodText(text);
                    if (period is not null)
                    {
                        return period;
                    }
                }
            }
        }

        return null;
    }

    private static Dictionary<string, string> TryReadDepartments(DataSet dataSet)
    {
        var summary = dataSet.Tables
            .Cast<DataTable>()
            .FirstOrDefault(x => string.Equals(x.TableName, "Summary", StringComparison.OrdinalIgnoreCase));
        var departments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (summary is null)
        {
            return departments;
        }

        foreach (DataRow row in summary.Rows)
        {
            if (row.ItemArray.Length < 3)
            {
                continue;
            }

            var id = NormalizeId(CellText(row[0]));
            var department = CellText(row[2]);
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(department))
            {
                departments[id] = department;
            }
        }

        return departments;
    }

    private static IReadOnlyList<EmployeeInfo> ReadEmployees(DataTable logs, IReadOnlyDictionary<string, string> departments)
    {
        var employees = new Dictionary<string, EmployeeInfo>(StringComparer.OrdinalIgnoreCase);

        for (var rowIndex = 0; rowIndex < logs.Rows.Count; rowIndex++)
        {
            var row = logs.Rows[rowIndex];
            if (!IsEmployeeHeader(row))
            {
                continue;
            }

            var id = NormalizeId(GetCell(row, 2));
            var name = GetCell(row, 10);
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            departments.TryGetValue(id, out var department);
            employees[id] = new EmployeeInfo(id, name.Trim(), department ?? string.Empty);
        }

        return employees.Values
            .OrderBy(x => int.TryParse(x.Id, out var id) ? id : int.MaxValue)
            .ThenBy(x => x.Name)
            .ToList();
    }

    private static IReadOnlyList<RawPunch> ReadPunches(
        DataTable logs,
        IReadOnlyDictionary<string, string> departments,
        DateOnly start,
        DateOnly end)
    {
        var punches = new List<RawPunch>();

        for (var rowIndex = 0; rowIndex < logs.Rows.Count; rowIndex++)
        {
            var employeeRow = logs.Rows[rowIndex];
            if (!IsEmployeeHeader(employeeRow))
            {
                continue;
            }

            var id = NormalizeId(GetCell(employeeRow, 2));
            var name = GetCell(employeeRow, 10).Trim();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            departments.TryGetValue(id, out var department);
            var punchRowIndex = rowIndex + 1;
            if (punchRowIndex >= logs.Rows.Count)
            {
                continue;
            }

            var dateHeaders = ReadDateHeaders(rowIndex > 0 ? logs.Rows[rowIndex - 1] : null, start, end);
            var punchRow = logs.Rows[punchRowIndex];
            foreach (var header in dateHeaders)
            {
                if (header.ColumnIndex >= punchRow.ItemArray.Length)
                {
                    continue;
                }

                var rawCell = GetCell(punchRow, header.ColumnIndex);
                foreach (var time in ParseTimes(rawCell))
                {
                    punches.Add(new RawPunch(
                        id,
                        name,
                        department ?? string.Empty,
                        header.Date.ToDateTime(time),
                        header.Date.Day,
                        NormalizeRawCell(rawCell),
                        logs.TableName,
                        punchRowIndex + 1,
                        header.ColumnIndex + 1));
                }
            }
        }

        return punches
            .OrderBy(x => int.TryParse(x.EmployeeId, out var id) ? id : int.MaxValue)
            .ThenBy(x => x.Timestamp)
            .ToList();
    }

    private static IReadOnlyList<EmployeeInfo> ReadMorningEmployees(DataTable sheet)
    {
        var employees = new Dictionary<string, EmployeeInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (DataRow row in sheet.Rows)
        {
            if (TryReadMorningEmployee(row, out var employee))
            {
                employees[employee.Id] = employee;
            }
        }

        return employees.Values
            .OrderBy(x => int.TryParse(x.Id, out var id) ? id : int.MaxValue)
            .ThenBy(x => x.Name)
            .ToList();
    }

    private static IReadOnlyList<RawPunch> ReadMorningPunches(DataTable sheet, DateOnly start, DateOnly end)
    {
        var punches = new List<RawPunch>();

        for (var rowIndex = 0; rowIndex < sheet.Rows.Count; rowIndex++)
        {
            var employeeRow = sheet.Rows[rowIndex];
            if (!TryReadMorningEmployee(employeeRow, out var employee))
            {
                continue;
            }

            var dateHeaderRowIndex = rowIndex + 1;
            var punchRowIndex = rowIndex + 2;
            if (punchRowIndex >= sheet.Rows.Count)
            {
                continue;
            }

            var dateHeaders = ReadDateHeaders(sheet.Rows[dateHeaderRowIndex], start, end);
            var punchRow = sheet.Rows[punchRowIndex];
            foreach (var header in dateHeaders)
            {
                if (header.ColumnIndex >= punchRow.ItemArray.Length)
                {
                    continue;
                }

                var rawCell = GetCell(punchRow, header.ColumnIndex);
                foreach (var time in ParseTimes(rawCell))
                {
                    punches.Add(new RawPunch(
                        employee.Id,
                        employee.Name,
                        employee.Department,
                        header.Date.ToDateTime(time),
                        header.Date.Day,
                        NormalizeRawCell(rawCell),
                        sheet.TableName,
                        punchRowIndex + 1,
                        header.ColumnIndex + 1));
                }
            }
        }

        return punches
            .OrderBy(x => int.TryParse(x.EmployeeId, out var id) ? id : int.MaxValue)
            .ThenBy(x => x.Timestamp)
            .ToList();
    }

    private static bool TryReadMorningEmployee(DataRow row, out EmployeeInfo employee)
    {
        var id = NormalizeId(ReadLabelValue(row, "User ID"));
        var name = ReadLabelValue(row, "Name").Trim();
        var department = ReadLabelValue(row, "Department").Trim();
        employee = new EmployeeInfo(id, name, department);

        return !string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name);
    }

    private static string ReadLabelValue(DataRow row, string label)
    {
        for (var index = 0; index < row.ItemArray.Length; index++)
        {
            var text = GetCell(row, index).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (string.Equals(NormalizeLabel(text), label, StringComparison.OrdinalIgnoreCase))
            {
                return GetCell(row, index + 1).Trim();
            }

            var prefix = label + ":";
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var inlineValue = text[prefix.Length..].Trim();
                return !string.IsNullOrWhiteSpace(inlineValue)
                    ? inlineValue
                    : GetCell(row, index + 1).Trim();
            }
        }

        return string.Empty;
    }

    private static string NormalizeLabel(string value)
    {
        return value.Trim().TrimEnd(':').Trim();
    }

    private static IReadOnlyList<DateHeader> ReadDateHeaders(DataRow? headerRow, DateOnly start, DateOnly end)
    {
        if (headerRow is null)
        {
            return Array.Empty<DateHeader>();
        }

        var dates = EnumerateDates(start, end).ToList();
        var datesByDay = dates
            .GroupBy(x => x.Day)
            .ToDictionary(x => x.Key, x => new Queue<DateOnly>(x));
        var headers = new List<DateHeader>();

        for (var column = 0; column < headerRow.ItemArray.Length; column++)
        {
            if (!TryReadDayNumber(headerRow[column], out var dayNumber))
            {
                continue;
            }

            if (!datesByDay.TryGetValue(dayNumber, out var candidates) || candidates.Count == 0)
            {
                continue;
            }

            headers.Add(new DateHeader(column, candidates.Dequeue()));
        }

        return headers;
    }

    private static IEnumerable<DateOnly> EnumerateDates(DateOnly start, DateOnly end)
    {
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            yield return date;
        }
    }

    private static IEnumerable<TimeOnly> ParseTimes(string rawCell)
    {
        foreach (Match match in TimeRegex.Matches(rawCell))
        {
            if (TimeOnly.TryParseExact(match.Value, "H:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ||
                TimeOnly.TryParseExact(match.Value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
            {
                yield return time;
            }
        }
    }

    private static bool IsEmployeeHeader(DataRow row)
    {
        return string.Equals(GetCell(row, 0).Trim(), "No :", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadDayNumber(object? value, out int dayNumber)
    {
        dayNumber = 0;

        switch (value)
        {
            case double number when number >= 1 && number <= 31 && Math.Abs(number - Math.Round(number)) < 0.001:
                dayNumber = (int)Math.Round(number);
                return true;
            case int number when number is >= 1 and <= 31:
                dayNumber = number;
                return true;
        }

        var text = CellText(value).Trim();
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        if (parsed is < 1 or > 31)
        {
            return false;
        }

        dayNumber = parsed;
        return true;
    }

    private static string GetCell(DataRow row, int index)
    {
        return index >= row.ItemArray.Length ? string.Empty : CellText(row[index]);
    }

    private static string CellText(object? value)
    {
        return value switch
        {
            null => string.Empty,
            DBNull => string.Empty,
            double number => Math.Abs(number - Math.Round(number)) < 0.001
                ? ((int)Math.Round(number)).ToString(CultureInfo.InvariantCulture)
                : number.ToString(CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static string NormalizeId(string id)
    {
        var text = id.Trim();
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number.ToString(CultureInfo.InvariantCulture)
            : text;
    }

    private static string NormalizeRawCell(string rawCell)
    {
        return rawCell.Replace("\r", string.Empty).Replace("\n", " | ").Trim();
    }

    private sealed record DateHeader(int ColumnIndex, DateOnly Date);
}
