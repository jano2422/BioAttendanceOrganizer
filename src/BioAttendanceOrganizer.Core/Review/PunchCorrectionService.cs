using System.Globalization;
using BioAttendanceOrganizer.Core.Models;

namespace BioAttendanceOrganizer.Core.Review;

public sealed class PunchCorrectionService
{
    public PunchCorrectionDraft CreateDraft(
        BiometricWorkbook workbook,
        AttendanceReport report,
        IReadOnlyDictionary<string, PunchCorrectionRecord>? savedCorrections = null)
    {
        var assignments = workbook.RawPunches.Select(CreateAssignment).ToList();
        ApplyRecognizedTypes(assignments, report);

        if (savedCorrections is not null)
        {
            ApplySavedCorrections(assignments, savedCorrections);
        }

        return new PunchCorrectionDraft(assignments
            .OrderBy(x => TryParseEmployeeId(x.EmployeeId))
            .ThenBy(x => x.Timestamp));
    }

    public PunchCorrectionAuditEntry MoveDate(PunchCorrectionDraft draft, string sourcePunchKey, DateOnly targetDate, string cutoffKey, string reason)
    {
        var punch = Find(draft, sourcePunchKey);
        var before = Snapshot(punch);
        punch.Timestamp = targetDate.ToDateTime(TimeOnly.FromDateTime(punch.Timestamp));
        return AddAudit(draft, cutoffKey, punch, PunchCorrectionAction.MoveDate, before, Snapshot(punch), reason);
    }

    public PunchCorrectionAuditEntry ReassignEmployee(PunchCorrectionDraft draft, string sourcePunchKey, EmployeeInfo employee, string cutoffKey, string reason)
    {
        var punch = Find(draft, sourcePunchKey);
        var before = Snapshot(punch);
        punch.EmployeeId = employee.Id;
        punch.EmployeeName = employee.Name;
        punch.Department = employee.Department;
        return AddAudit(draft, cutoffKey, punch, PunchCorrectionAction.ReassignEmployee, before, Snapshot(punch), reason);
    }

    public PunchCorrectionAuditEntry EditTime(PunchCorrectionDraft draft, string sourcePunchKey, DateTime timestamp, string cutoffKey, string reason)
    {
        var punch = Find(draft, sourcePunchKey);
        var before = Snapshot(punch);
        punch.Timestamp = timestamp;
        return AddAudit(draft, cutoffKey, punch, PunchCorrectionAction.EditTime, before, Snapshot(punch), reason);
    }

    public PunchCorrectionAuditEntry SetPunchType(PunchCorrectionDraft draft, string sourcePunchKey, PunchType punchType, string cutoffKey, string reason)
    {
        var punch = Find(draft, sourcePunchKey);
        var before = Snapshot(punch);
        punch.PunchType = punchType;
        return AddAudit(draft, cutoffKey, punch, PunchCorrectionAction.SetPunchType, before, Snapshot(punch), reason);
    }

    public IReadOnlyList<PunchCorrectionAuditEntry> PairPunches(PunchCorrectionDraft draft, string inPunchKey, string outPunchKey, string cutoffKey, string reason)
    {
        if (inPunchKey == outPunchKey)
        {
            throw new InvalidOperationException("Choose two different punches to pair.");
        }

        var inPunch = Find(draft, inPunchKey);
        var outPunch = Find(draft, outPunchKey);
        if (inPunch.IsDeleted || outPunch.IsDeleted)
        {
            throw new InvalidOperationException("Deleted punches cannot be paired.");
        }

        var pairKey = Guid.NewGuid().ToString("N");
        var audits = new List<PunchCorrectionAuditEntry>();
        var beforeIn = Snapshot(inPunch);
        inPunch.PunchType = PunchType.In;
        inPunch.PairKey = pairKey;
        audits.Add(AddAudit(draft, cutoffKey, inPunch, PunchCorrectionAction.PairPunches, beforeIn, Snapshot(inPunch), reason));

        var beforeOut = Snapshot(outPunch);
        outPunch.PunchType = PunchType.Out;
        outPunch.PairKey = pairKey;
        audits.Add(AddAudit(draft, cutoffKey, outPunch, PunchCorrectionAction.PairPunches, beforeOut, Snapshot(outPunch), reason));
        return audits;
    }

    public PunchCorrectionAuditEntry UnpairPunch(PunchCorrectionDraft draft, string sourcePunchKey, string cutoffKey, string reason)
    {
        var punch = Find(draft, sourcePunchKey);
        var before = Snapshot(punch);
        punch.PunchType = PunchType.Unpaired;
        punch.PairKey = string.Empty;
        return AddAudit(draft, cutoffKey, punch, PunchCorrectionAction.UnpairPunches, before, Snapshot(punch), reason);
    }

    public PunchCorrectionAuditEntry DeletePunch(PunchCorrectionDraft draft, string sourcePunchKey, string cutoffKey, string reason)
    {
        var punch = Find(draft, sourcePunchKey);
        var before = Snapshot(punch);
        punch.IsDeleted = true;
        return AddAudit(draft, cutoffKey, punch, PunchCorrectionAction.DeletePunch, before, Snapshot(punch), reason);
    }

    public PunchCorrectionAuditEntry RestorePunch(PunchCorrectionDraft draft, string sourcePunchKey, string cutoffKey, string reason)
    {
        var punch = Find(draft, sourcePunchKey);
        var before = Snapshot(punch);
        punch.IsDeleted = false;
        return AddAudit(draft, cutoffKey, punch, PunchCorrectionAction.RestorePunch, before, Snapshot(punch), reason);
    }

    public IReadOnlyList<string> Validate(PunchCorrectionDraft draft, CutoffPeriod cutoff, string reason)
    {
        var errors = new List<string>();
        if (draft.HasPendingChanges && string.IsNullOrWhiteSpace(reason))
        {
            errors.Add("Correction reason is required before saving.");
        }

        var min = cutoff.StartDate.AddDays(-1).ToDateTime(TimeOnly.MinValue);
        var max = cutoff.EndDate.AddDays(1).ToDateTime(TimeOnly.MaxValue);
        foreach (var punch in draft.Assignments.Where(x => !x.IsDeleted))
        {
            if (punch.Timestamp < min || punch.Timestamp > max)
            {
                errors.Add($"{punch.SourcePunchKey} is outside the cutoff boundary allowance.");
            }
        }

        foreach (var pair in draft.Assignments.Where(x => !x.IsDeleted && !string.IsNullOrWhiteSpace(x.PairKey)).GroupBy(x => x.PairKey))
        {
            if (pair.Count(x => x.PunchType == PunchType.In) > 1 || pair.Count(x => x.PunchType == PunchType.Out) > 1)
            {
                errors.Add("A punch pair can only have one IN and one OUT.");
            }
        }

        return errors;
    }

    public IReadOnlyList<PunchCorrectionRecord> ToCorrectionRecords(PunchCorrectionDraft draft, string importKey, string reason)
    {
        var now = DateTime.Now;
        return draft.Assignments
            .Where(x => x.IsChanged)
            .Select(x => new PunchCorrectionRecord(
                importKey,
                x.SourcePunchKey,
                x.OriginalEmployeeId,
                x.OriginalTimestamp,
                x.EmployeeId,
                x.EmployeeName,
                x.Department,
                x.Timestamp,
                x.PunchType,
                x.PairKey,
                x.IsDeleted,
                reason.Trim(),
                now))
            .ToList();
    }

    public BiometricWorkbook ApplyDraftToWorkbook(BiometricWorkbook workbook, PunchCorrectionDraft draft)
    {
        var rawPunches = draft.Assignments
            .Where(x => !x.IsDeleted)
            .OrderBy(x => TryParseEmployeeId(x.EmployeeId))
            .ThenBy(x => x.Timestamp)
            .Select(x => new RawPunch(
                x.EmployeeId,
                x.EmployeeName,
                x.Department,
                x.Timestamp,
                x.Timestamp.Day,
                x.Timestamp.ToString("HH:mm", CultureInfo.InvariantCulture),
                x.SourceSheet,
                x.SourceRow,
                x.SourceColumn))
            .ToList();

        var employees = workbook.Employees
            .Concat(rawPunches.Select(x => new EmployeeInfo(x.EmployeeId, x.EmployeeName, x.Department)))
            .GroupBy(x => x.Id)
            .Select(x => x.Last())
            .OrderBy(x => TryParseEmployeeId(x.Id))
            .ToList();

        return workbook with { Employees = employees, RawPunches = rawPunches };
    }

    private static PunchAssignment CreateAssignment(RawPunch punch)
    {
        return new PunchAssignment
        {
            SourcePunchKey = SourcePunchKey(punch),
            OriginalEmployeeId = punch.EmployeeId,
            OriginalEmployeeName = punch.EmployeeName,
            OriginalDepartment = punch.Department,
            OriginalTimestamp = punch.Timestamp,
            SourceSheet = punch.SourceSheet,
            SourceRow = punch.SourceRow,
            SourceColumn = punch.SourceColumn,
            RawCell = punch.RawCell,
            EmployeeId = punch.EmployeeId,
            EmployeeName = punch.EmployeeName,
            Department = punch.Department,
            Timestamp = punch.Timestamp,
            PunchType = PunchType.Unpaired
        };
    }

    public static string SourcePunchKey(RawPunch punch)
    {
        return $"{punch.SourceSheet}|{punch.SourceRow}|{punch.SourceColumn}|{punch.EmployeeId}|{punch.Timestamp:O}";
    }

    private static void ApplyRecognizedTypes(IReadOnlyList<PunchAssignment> assignments, AttendanceReport report)
    {
        foreach (var record in report.Records)
        {
            if (record.TimeIn is not null)
            {
                var punch = assignments.FirstOrDefault(x => x.EmployeeId == record.EmployeeId && x.OriginalTimestamp == record.TimeIn);
                if (punch is not null)
                {
                    punch.PunchType = PunchType.In;
                    punch.OriginalPunchType = PunchType.In;
                }
            }

            if (record.TimeOut is not null)
            {
                var punch = assignments.FirstOrDefault(x => x.EmployeeId == record.EmployeeId && x.OriginalTimestamp == record.TimeOut);
                if (punch is not null)
                {
                    punch.PunchType = PunchType.Out;
                    punch.OriginalPunchType = PunchType.Out;
                }
            }
        }
    }

    private static void ApplySavedCorrections(IReadOnlyList<PunchAssignment> assignments, IReadOnlyDictionary<string, PunchCorrectionRecord> savedCorrections)
    {
        foreach (var assignment in assignments)
        {
            if (!savedCorrections.TryGetValue(assignment.SourcePunchKey, out var saved))
            {
                continue;
            }

            assignment.EmployeeId = saved.EmployeeId;
            assignment.EmployeeName = saved.EmployeeName;
            assignment.Department = saved.Department;
            assignment.Timestamp = saved.Timestamp;
            assignment.PunchType = saved.PunchType;
            assignment.PairKey = saved.PairKey;
            assignment.IsDeleted = saved.IsDeleted;
        }
    }

    private static PunchAssignment Find(PunchCorrectionDraft draft, string sourcePunchKey)
    {
        return draft.Assignments.FirstOrDefault(x => x.SourcePunchKey == sourcePunchKey)
            ?? throw new InvalidOperationException("Punch was not found.");
    }

    private static PunchCorrectionAuditEntry AddAudit(
        PunchCorrectionDraft draft,
        string cutoffKey,
        PunchAssignment punch,
        PunchCorrectionAction action,
        string before,
        string after,
        string reason)
    {
        var entry = new PunchCorrectionAuditEntry(
            0,
            string.Empty,
            cutoffKey,
            punch.SourcePunchKey,
            punch.EmployeeId,
            punch.WorkDate,
            action,
            before,
            after,
            reason.Trim(),
            DateTime.Now);
        draft.PendingAuditEntries.Add(entry);
        return entry;
    }

    private static string Snapshot(PunchAssignment punch)
    {
        return $"{punch.EmployeeId}|{punch.Timestamp:yyyy-MM-dd HH:mm}|{punch.PunchType}|deleted={punch.IsDeleted}";
    }

    private static int TryParseEmployeeId(string employeeId)
    {
        return int.TryParse(employeeId, out var value) ? value : int.MaxValue;
    }

    public (bool IsValid, string Reason) ValidateMoveTarget(PunchCorrectionDraft draft, string sourcePunchKey, AttendanceRecord targetRecord, PunchType targetType, AttendanceRules rules, CutoffPeriod cutoff)
    {
        var punch = Find(draft, sourcePunchKey);
        
        var minCutoff = cutoff.StartDate.AddDays(-1).ToDateTime(TimeOnly.MinValue);
        var maxCutoff = cutoff.EndDate.AddDays(1).ToDateTime(TimeOnly.MaxValue);
        
        // Wait, where is the punch being moved to? It's being moved to a specific date/time, or just testing if it can be assigned to targetRecord.
        // If it's smart move, the target is usually "Before Known Out" or "After Known In".
        // Let's assume the timestamp will be modified to match the targetRecord's WorkDate.
        // Wait, the prompt says "A punch can fill MissingIn only if the resulting IN is before the OUT and the duration is within AttendanceRules."
        // We just need to simulate the move. But what is the new timestamp? 
        // For a smart move, if we want to move `punch` to fill `MissingIn`, we would change its timestamp to the target date. 
        // But what time? The prompt doesn't specify changing the time, only the date? Or it keeps the time and moves the date.
        // "Allows moving a punch to fill MissingIn when it becomes before the known OUT" - implies we change its Date to the target date, and keep its Time.
        
        var targetDate = targetRecord.WorkDate;
        var newTimestamp = targetDate.ToDateTime(TimeOnly.FromDateTime(punch.Timestamp));
        
        if (newTimestamp < minCutoff || newTimestamp > maxCutoff)
        {
            return (false, "Outside cutoff boundary");
        }
        
        if (targetType == PunchType.In)
        {
            if (targetRecord.FinalTimeOut.HasValue)
            {
                if (newTimestamp >= targetRecord.FinalTimeOut.Value)
                    return (false, "IN must be before OUT");
                
                var duration = targetRecord.FinalTimeOut.Value - newTimestamp;
                if (duration < rules.MinimumWorkDuration || duration > rules.MaximumWorkDuration)
                    return (false, $"Duration {duration.TotalHours:0.##}h is outside allowed {rules.MinimumWorkDuration.TotalHours}-{rules.MaximumWorkDuration.TotalHours}h");

                if (newTimestamp.Date < targetRecord.FinalTimeOut.Value.Date)
                {
                    var inTime = TimeOnly.FromDateTime(newTimestamp);
                    if (inTime.ToTimeSpan() < rules.NightPairStart)
                        return (false, "Previous evening IN is too early for night shift");
                }
            }
        }
        else if (targetType == PunchType.Out)
        {
            if (targetRecord.FinalTimeIn.HasValue)
            {
                if (newTimestamp <= targetRecord.FinalTimeIn.Value)
                    return (false, "OUT must be after IN");
                
                var duration = newTimestamp - targetRecord.FinalTimeIn.Value;
                if (duration < rules.MinimumWorkDuration || duration > rules.MaximumWorkDuration)
                    return (false, $"Duration {duration.TotalHours:0.##}h is outside allowed {rules.MinimumWorkDuration.TotalHours}-{rules.MaximumWorkDuration.TotalHours}h");
                    
                if (newTimestamp.Date > targetRecord.FinalTimeIn.Value.Date)
                {
                    // It's a night shift OUT.
                    var outTime = TimeOnly.FromDateTime(newTimestamp);
                    if (outTime.ToTimeSpan() > rules.NightEndEnd)
                        return (false, "Next day OUT is too late for night shift");
                }
            }
        }
        
        if (!string.IsNullOrWhiteSpace(punch.PairKey))
        {
            var pairPunches = draft.Assignments.Where(x => !x.IsDeleted && x.PairKey == punch.PairKey && x.SourcePunchKey != punch.SourcePunchKey).ToList();
            if (pairPunches.Any(x => x.PunchType == targetType))
            {
                return (false, $"Duplicate {targetType} in pair");
            }
        }
        
        return (true, "Valid move");
    }
}
