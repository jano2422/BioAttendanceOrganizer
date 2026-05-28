using BioAttendanceOrganizer.Core.Models;

namespace BioAttendanceOrganizer.Core.Analysis;

public sealed class AttendanceAnalyzer
{
    public AttendanceReport Analyze(BiometricWorkbook workbook, AttendanceRules? rules = null)
    {
        rules ??= new AttendanceRules();
        var records = new List<AttendanceRecord>();

        foreach (var employee in workbook.Employees)
        {
            var employeePunches = workbook.RawPunches
                .Where(x => x.EmployeeId == employee.Id)
                .OrderBy(x => x.Timestamp)
                .ToList();

            records.AddRange(AnalyzeEmployee(workbook, employee, employeePunches, rules));
        }

        var ordered = records
            .OrderBy(x => TryParseEmployeeId(x.EmployeeId))
            .ThenBy(x => x.WorkDate)
            .ThenBy(x => x.TimeIn ?? x.TimeOut ?? x.WorkDate.ToDateTime(TimeOnly.MinValue))
            .ToList();

        var summary = AttendanceSummary.From(
            workbook.PeriodStart,
            workbook.PeriodEnd,
            workbook.Employees.Count,
            workbook.RawPunches.Count,
            ordered);

        return new AttendanceReport(workbook, ordered, summary);
    }

    private static IReadOnlyList<AttendanceRecord> AnalyzeEmployee(
        BiometricWorkbook workbook,
        EmployeeInfo employee,
        IReadOnlyList<RawPunch> rawPunches,
        AttendanceRules rules)
    {
        var records = new List<AttendanceRecord>();
        var nodes = Deduplicate(rawPunches, rules).ToList();
        var coveredDates = new HashSet<DateOnly>(rawPunches.Select(x => DateOnly.FromDateTime(x.Timestamp)));
        var workDates = new HashSet<DateOnly>();

        var index = 0;
        while (index < nodes.Count)
        {
            if (rules.EnableCarryoverBoundaryDetection &&
                workbook.DetectedSlot != DtrImportSlot.Morning &&
                IsStartBoundaryCarryoverBeforeNight(nodes, index, workbook.PeriodStart, rules))
            {
                records.Add(CreateSinglePunchRecord(
                    employee,
                    nodes[index],
                    AttendanceStatus.CarryoverFromPreviousCutoff,
                    [IssueFlag.Carryover, IssueFlag.BoundaryPunch, IssueFlag.NeedsReview],
                    0.85,
                    rules.AutoApproveCleanNightShifts));
                index++;
                continue;
            }

            var candidate = FindBestPair(nodes, index, workbook.DetectedSlot, rules);
            if (candidate is not null)
            {
                var record = CreatePairedRecord(employee, nodes, index, candidate.Value.EndIndex, candidate.Value, rules);
                records.Add(record);
                workDates.Add(record.WorkDate);
                index = candidate.Value.EndIndex + 1;
                continue;
            }

            if (TryCreateTooShortRecord(employee, nodes, index, rules, out var tooShortRecord))
            {
                records.Add(tooShortRecord);
                index += 2;
                continue;
            }

            records.Add(CreateUnpairedRecord(employee, nodes[index], workbook, rules));
            index++;
        }

        foreach (var date in EnumerateDates(workbook.PeriodStart, workbook.PeriodEnd))
        {
            if (coveredDates.Contains(date) || workDates.Contains(date))
            {
                continue;
            }

            records.Add(new AttendanceRecord
            {
                EmployeeId = employee.Id,
                EmployeeName = employee.Name,
                Department = employee.Department,
                WorkDate = date,
                Status = AttendanceStatus.NoRecord,
                Confidence = 1,
                RawPunches = string.Empty,
                AutoApproveCleanNightShift = rules.AutoApproveCleanNightShifts
            });
        }

        return records;
    }

    private static IEnumerable<PunchNode> Deduplicate(IReadOnlyList<RawPunch> rawPunches, AttendanceRules rules)
    {
        PunchNode? last = null;
        foreach (var punch in rawPunches.OrderBy(x => x.Timestamp))
        {
            if (last is not null && punch.Timestamp - last.Primary.Timestamp <= rules.DuplicateTapWindow)
            {
                last.Duplicates.Add(punch);
                continue;
            }

            last = new PunchNode(punch);
            yield return last;
        }
    }

    private static PairCandidate? FindBestPair(IReadOnlyList<PunchNode> nodes, int startIndex, DtrImportSlot slot, AttendanceRules rules)
    {
        var start = nodes[startIndex].Primary.Timestamp;
        PairCandidate? best = null;

        for (var endIndex = startIndex + 1; endIndex < nodes.Count; endIndex++)
        {
            var end = nodes[endIndex].Primary.Timestamp;
            var duration = end - start;
            if (duration <= TimeSpan.Zero)
            {
                continue;
            }

            if (duration > rules.MaximumWorkDuration)
            {
                break;
            }

            if (duration < rules.MinimumWorkDuration)
            {
                continue;
            }

            var candidate = ScorePair(start, end, endIndex, slot, rules);
            if (candidate is null)
            {
                continue;
            }

            if (best is null ||
                candidate.Value.Score > best.Value.Score ||
                (Math.Abs(candidate.Value.Score - best.Value.Score) < 0.001 && duration > best.Value.Duration))
            {
                best = candidate;
            }
        }

        return best;
    }

    private static PairCandidate? ScorePair(DateTime start, DateTime end, int endIndex, DtrImportSlot slot, AttendanceRules rules)
    {
        var duration = end - start;
        var startTime = TimeOnly.FromDateTime(start);
        var endTime = TimeOnly.FromDateTime(end);
        var sameDay = start.Date == end.Date;
        var nextDay = end.Date == start.Date.AddDays(1);

        if (slot == DtrImportSlot.Morning &&
            IsDayStart(startTime, rules) &&
            ((sameDay && IsDayEnd(endTime, rules)) ||
             (nextDay && IsCrossMidnightDayEnd(endTime, rules))))
        {
            return new PairCandidate(endIndex, AttendanceStatus.CleanDayShift, duration, 0.96);
        }

        if (slot == DtrImportSlot.Night && nextDay && IsNightStartForPair(startTime, rules) && IsNightEnd(endTime, rules))
        {
            return new PairCandidate(endIndex, AttendanceStatus.LikelyNightShift, duration, 0.92);
        }

        if ((sameDay || nextDay) && IsPlausibleEndpoint(startTime, endTime, sameDay, slot, rules))
        {
            return new PairCandidate(endIndex, AttendanceStatus.NeedsReview, duration, 0.62);
        }

        return null;
    }

    private static bool TryCreateTooShortRecord(
        EmployeeInfo employee,
        IReadOnlyList<PunchNode> nodes,
        int startIndex,
        AttendanceRules rules,
        out AttendanceRecord record)
    {
        record = new AttendanceRecord();
        if (startIndex + 1 >= nodes.Count)
        {
            return false;
        }

        var start = nodes[startIndex].Primary.Timestamp;
        var end = nodes[startIndex + 1].Primary.Timestamp;
        var duration = end - start;
        if (duration <= TimeSpan.Zero || duration >= rules.MinimumWorkDuration || duration > rules.DuplicateTapWindow)
        {
            return false;
        }

        var flags = new HashSet<IssueFlag> { IssueFlag.TooShort, IssueFlag.DuplicateTap, IssueFlag.NeedsReview };
        flags.UnionWith(DuplicateFlags(nodes, startIndex, startIndex + 1));

        record = new AttendanceRecord
        {
            EmployeeId = employee.Id,
            EmployeeName = employee.Name,
            Department = employee.Department,
            WorkDate = DateOnly.FromDateTime(start),
            TimeIn = start,
            TimeOut = end,
            Status = AttendanceStatus.TooShort,
            Flags = flags.ToList(),
            Confidence = 0.2,
            RawPunches = FormatRawPunches(AllRaw(nodes, startIndex, startIndex + 1)),
            AutoApproveCleanNightShift = rules.AutoApproveCleanNightShifts
        };

        return true;
    }

    private static AttendanceRecord CreatePairedRecord(
        EmployeeInfo employee,
        IReadOnlyList<PunchNode> nodes,
        int startIndex,
        int endIndex,
        PairCandidate candidate,
        AttendanceRules rules)
    {
        var raw = AllRaw(nodes, startIndex, endIndex).ToList();
        var flags = new HashSet<IssueFlag>();
        var confidence = candidate.Score;

        if (candidate.Status == AttendanceStatus.LikelyNightShift)
        {
            flags.Add(IssueFlag.LikelyNightShift);
        }

        if (endIndex - startIndex > 1)
        {
            flags.Add(IssueFlag.ExtraPunches);
            confidence -= 0.12;
        }

        foreach (var duplicateFlag in DuplicateFlags(nodes, startIndex, endIndex))
        {
            flags.Add(duplicateFlag);
            confidence -= 0.08;
        }

        if (candidate.Status == AttendanceStatus.NeedsReview)
        {
            flags.Add(IssueFlag.Ambiguous);
            flags.Add(IssueFlag.NeedsReview);
            confidence -= 0.1;
        }

        var start = nodes[startIndex].Primary.Timestamp;
        var end = nodes[endIndex].Primary.Timestamp;

        return new AttendanceRecord
        {
            EmployeeId = employee.Id,
            EmployeeName = employee.Name,
            Department = employee.Department,
            WorkDate = DateOnly.FromDateTime(start),
            TimeIn = start,
            TimeOut = end,
            Status = candidate.Status,
            Flags = flags.ToList(),
            Confidence = Math.Clamp(confidence, 0.05, 1),
            RawPunches = FormatRawPunches(raw),
            AutoApproveCleanNightShift = rules.AutoApproveCleanNightShifts
        };
    }

    private static AttendanceRecord CreateUnpairedRecord(
        EmployeeInfo employee,
        PunchNode node,
        BiometricWorkbook workbook,
        AttendanceRules rules)
    {
        var timestamp = node.Primary.Timestamp;
        var date = DateOnly.FromDateTime(timestamp);
        var time = TimeOnly.FromDateTime(timestamp);
        var flags = new HashSet<IssueFlag>(DuplicateFlags([node], 0, 0));
        var confidence = flags.Contains(IssueFlag.DuplicateTap) ? 0.45 : 0.55;

        AttendanceStatus status;
        if (workbook.DetectedSlot == DtrImportSlot.Night &&
            rules.EnableCarryoverBoundaryDetection &&
            date == workbook.PeriodStart &&
            IsNightEnd(time, rules))
        {
            status = AttendanceStatus.CarryoverFromPreviousCutoff;
            flags.Add(IssueFlag.Carryover);
            flags.Add(IssueFlag.BoundaryPunch);
            confidence = 0.82;
        }
        else if (workbook.DetectedSlot == DtrImportSlot.Night &&
                 rules.EnableCarryoverBoundaryDetection &&
                 date == workbook.PeriodEnd &&
                 IsStrongNightStart(time, rules))
        {
            status = AttendanceStatus.CarryoverToNextCutoff;
            flags.Add(IssueFlag.Carryover);
            flags.Add(IssueFlag.BoundaryPunch);
            confidence = 0.82;
        }
        else if (workbook.DetectedSlot == DtrImportSlot.Night && IsStrongNightStart(time, rules))
        {
            status = AttendanceStatus.MissingOut;
            flags.Add(IssueFlag.LikelyNightShift);
            flags.Add(IssueFlag.MissingOut);
        }
        else if (workbook.DetectedSlot == DtrImportSlot.Night && IsNightEnd(time, rules))
        {
            status = AttendanceStatus.MissingIn;
            flags.Add(IssueFlag.MissingIn);
        }
        else if (workbook.DetectedSlot == DtrImportSlot.Morning && IsDayEnd(time, rules))
        {
            status = AttendanceStatus.MissingIn;
            flags.Add(IssueFlag.MissingIn);
        }
        else if (workbook.DetectedSlot == DtrImportSlot.Morning && IsDayStart(time, rules))
        {
            status = AttendanceStatus.MissingOut;
            flags.Add(IssueFlag.MissingOut);
        }
        else
        {
            status = AttendanceStatus.NeedsReview;
            flags.Add(IssueFlag.Ambiguous);
        }

        flags.Add(IssueFlag.NeedsReview);

        return CreateSinglePunchRecord(employee, node, status, flags, confidence, rules.AutoApproveCleanNightShifts);
    }

    private static AttendanceRecord CreateSinglePunchRecord(
        EmployeeInfo employee,
        PunchNode node,
        AttendanceStatus status,
        IEnumerable<IssueFlag> flags,
        double confidence,
        bool autoApproveCleanNightShift)
    {
        var timestamp = node.Primary.Timestamp;
        var raw = AllRaw([node], 0, 0);
        DateTime? timeIn = status is AttendanceStatus.MissingIn or AttendanceStatus.CarryoverFromPreviousCutoff
            ? null
            : timestamp;
        DateTime? timeOut = status is AttendanceStatus.MissingIn or AttendanceStatus.CarryoverFromPreviousCutoff
            ? timestamp
            : null;

        return new AttendanceRecord
        {
            EmployeeId = employee.Id,
            EmployeeName = employee.Name,
            Department = employee.Department,
            WorkDate = DateOnly.FromDateTime(timestamp),
            TimeIn = timeIn,
            TimeOut = timeOut,
            Status = status,
            Flags = flags.Distinct().ToList(),
            Confidence = Math.Clamp(confidence, 0.05, 1),
            RawPunches = FormatRawPunches(raw),
            AutoApproveCleanNightShift = autoApproveCleanNightShift
        };
    }

    private static bool IsStartBoundaryCarryoverBeforeNight(
        IReadOnlyList<PunchNode> nodes,
        int index,
        DateOnly periodStart,
        AttendanceRules rules)
    {
        if (index + 2 >= nodes.Count)
        {
            return false;
        }

        var first = nodes[index].Primary.Timestamp;
        var second = nodes[index + 1].Primary.Timestamp;
        var third = nodes[index + 2].Primary.Timestamp;

        if (DateOnly.FromDateTime(first) != periodStart || !IsNightEnd(TimeOnly.FromDateTime(first), rules))
        {
            return false;
        }

        var duration = third - second;
        return second.Date == first.Date &&
               third.Date == second.Date.AddDays(1) &&
               IsNightStartForPair(TimeOnly.FromDateTime(second), rules) &&
               IsNightEnd(TimeOnly.FromDateTime(third), rules) &&
               duration >= rules.MinimumWorkDuration &&
               duration <= rules.MaximumWorkDuration;
    }

    private static IEnumerable<IssueFlag> DuplicateFlags(IReadOnlyList<PunchNode> nodes, int startIndex, int endIndex)
    {
        for (var index = startIndex; index <= endIndex; index++)
        {
            if (nodes[index].Duplicates.Count > 0)
            {
                yield return IssueFlag.DuplicateTap;
                yield break;
            }
        }
    }

    private static IEnumerable<RawPunch> AllRaw(IReadOnlyList<PunchNode> nodes, int startIndex, int endIndex)
    {
        for (var index = startIndex; index <= endIndex; index++)
        {
            yield return nodes[index].Primary;
            foreach (var duplicate in nodes[index].Duplicates)
            {
                yield return duplicate;
            }
        }
    }

    private static string FormatRawPunches(IEnumerable<RawPunch> punches)
    {
        return string.Join(" | ", punches
            .OrderBy(x => x.Timestamp)
            .Select(x => x.Timestamp.ToString("yyyy-MM-dd HH:mm")));
    }

    private static bool IsDayStart(TimeOnly time, AttendanceRules rules)
    {
        return time.ToTimeSpan() < rules.DayStartEnd;
    }

    private static bool IsDayEnd(TimeOnly time, AttendanceRules rules)
    {
        var value = time.ToTimeSpan();
        if (rules.DayEndEnd < rules.DayEndStart)
        {
            return value >= rules.DayEndStart || value <= rules.DayEndEnd;
        }

        return value >= rules.DayEndStart && value <= rules.DayEndEnd;
    }

    private static bool IsCrossMidnightDayEnd(TimeOnly time, AttendanceRules rules)
    {
        return rules.DayEndEnd < rules.DayEndStart &&
               time.ToTimeSpan() <= rules.DayEndEnd;
    }

    private static bool IsNightStartForPair(TimeOnly time, AttendanceRules rules)
    {
        return time.ToTimeSpan() >= rules.NightPairStart;
    }

    private static bool IsStrongNightStart(TimeOnly time, AttendanceRules rules)
    {
        return time.ToTimeSpan() >= rules.StrongNightStart;
    }

    private static bool IsNightEnd(TimeOnly time, AttendanceRules rules)
    {
        return time.ToTimeSpan() <= rules.NightEndEnd;
    }

    private static bool IsPlausibleEndpoint(TimeOnly start, TimeOnly end, bool sameDay, DtrImportSlot slot, AttendanceRules rules)
    {
        if (slot == DtrImportSlot.Morning)
        {
            return IsDayStart(start, rules) &&
                   (sameDay || IsCrossMidnightDayEnd(end, rules));
        }

        return slot == DtrImportSlot.Night &&
               !sameDay &&
               IsNightStartForPair(start, rules) &&
               IsNightEnd(end, rules);
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
        return int.TryParse(employeeId, out var value) ? value : int.MaxValue;
    }

    private sealed class PunchNode(RawPunch primary)
    {
        public RawPunch Primary { get; } = primary;
        public List<RawPunch> Duplicates { get; } = new();
    }

    private readonly record struct PairCandidate(
        int EndIndex,
        AttendanceStatus Status,
        TimeSpan Duration,
        double Score);
}
