using BioAttendanceOrganizer.Core.Analysis;
using BioAttendanceOrganizer.Core.Models;
using BioAttendanceOrganizer.Core.Persistence;
using BioAttendanceOrganizer.Core.Review;

namespace BioAttendanceOrganizer.Tests;

public sealed class PunchCorrectionServiceTests
{
    private readonly AttendanceAnalyzer _analyzer = new();
    private readonly PunchCorrectionService _service = new();

    [Fact]
    public void MovesPunchToCorrectDateAndRecomputesTimePair()
    {
        var workbook = Workbook(
            new DateTime(2026, 4, 1, 6, 0, 0),
            new DateTime(2026, 4, 3, 17, 0, 0));
        var draft = Draft(workbook);
        var outPunch = draft.Assignments.Single(x => x.Timestamp.Day == 3);

        _service.MoveDate(draft, outPunch.SourcePunchKey, new DateOnly(2026, 4, 1), "2026-04-1", "Wrong date");
        var corrected = _service.ApplyDraftToWorkbook(workbook, draft);
        var report = _analyzer.Analyze(corrected);
        var row = report.Records.Single(x => x.Status != AttendanceStatus.NoRecord);

        Assert.Equal(new DateTime(2026, 4, 1, 17, 0, 0), row.TimeOut);
    }

    [Fact]
    public void ReassignsPunchToAnotherEmployee()
    {
        var workbook = Workbook(new DateTime(2026, 4, 1, 6, 0, 0));
        var draft = Draft(workbook);
        var punch = draft.Assignments.Single();
        var target = new EmployeeInfo("2", "Second Employee", "SECURITY");

        _service.ReassignEmployee(draft, punch.SourcePunchKey, target, "2026-04-1", "Wrong employee");
        var corrected = _service.ApplyDraftToWorkbook(workbook, draft);

        Assert.Contains(corrected.RawPunches, x => x.EmployeeId == "2" && x.EmployeeName == "Second Employee");
        Assert.Contains(corrected.Employees, x => x.Id == "2");
    }

    [Fact]
    public void EditsPunchTime()
    {
        var workbook = Workbook(new DateTime(2026, 4, 1, 6, 0, 0));
        var draft = Draft(workbook);
        var punch = draft.Assignments.Single();

        _service.EditTime(draft, punch.SourcePunchKey, new DateTime(2026, 4, 1, 7, 30, 0), "2026-04-1", "Wrong time");
        var corrected = _service.ApplyDraftToWorkbook(workbook, draft);

        Assert.Equal(new DateTime(2026, 4, 1, 7, 30, 0), corrected.RawPunches.Single().Timestamp);
    }

    [Fact]
    public void PairsAndUnpairsPunches()
    {
        var workbook = Workbook(
            new DateTime(2026, 4, 1, 6, 0, 0),
            new DateTime(2026, 4, 1, 17, 0, 0));
        var draft = Draft(workbook);
        var punches = draft.Assignments.OrderBy(x => x.Timestamp).ToList();

        _service.PairPunches(draft, punches[0].SourcePunchKey, punches[1].SourcePunchKey, "2026-04-1", "Pair");
        Assert.Equal(PunchType.In, punches[0].PunchType);
        Assert.Equal(PunchType.Out, punches[1].PunchType);
        Assert.Equal(punches[0].PairKey, punches[1].PairKey);

        _service.UnpairPunch(draft, punches[0].SourcePunchKey, "2026-04-1", "Unpair");
        Assert.Equal(PunchType.Unpaired, punches[0].PunchType);
        Assert.Equal(string.Empty, punches[0].PairKey);
    }

    [Fact]
    public void DeletesAndRestoresPunchWithoutLosingSourceMetadata()
    {
        var workbook = Workbook(new DateTime(2026, 4, 1, 6, 0, 0));
        var draft = Draft(workbook);
        var punch = draft.Assignments.Single();
        var sourceRow = punch.SourceRow;

        _service.DeletePunch(draft, punch.SourcePunchKey, "2026-04-1", "Bad punch");
        Assert.Empty(_service.ApplyDraftToWorkbook(workbook, draft).RawPunches);

        _service.RestorePunch(draft, punch.SourcePunchKey, "2026-04-1", "Restore");
        var restored = _service.ApplyDraftToWorkbook(workbook, draft).RawPunches.Single();
        Assert.Equal(sourceRow, restored.SourceRow);
        Assert.Equal(punch.SourceSheet, restored.SourceSheet);
    }

    [Fact]
    public void RequiresReasonBeforeSavingChangedDraft()
    {
        var workbook = Workbook(new DateTime(2026, 4, 1, 6, 0, 0));
        var draft = Draft(workbook);
        var punch = draft.Assignments.Single();
        _service.DeletePunch(draft, punch.SourcePunchKey, "2026-04-1", "");

        var errors = _service.Validate(draft, CutoffPeriod.Create(2026, 4, 1), "");

        Assert.Contains(errors, x => x.Contains("reason", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PersistsCorrectionsAndAuditThenReloads()
    {
        var database = TempDatabase();
        var workbook = Workbook(new DateTime(2026, 4, 1, 6, 0, 0));
        var importKey = CorrectionKey.ForWorkbook(workbook);
        var draft = Draft(workbook);
        var punch = draft.Assignments.Single();
        _service.EditTime(draft, punch.SourcePunchKey, new DateTime(2026, 4, 1, 7, 0, 0), "2026-04-1", "Wrong time");

        database.SavePunchCorrections(
            importKey,
            "2026-04-1",
            _service.ToCorrectionRecords(draft, importKey, "Wrong time"),
            draft.PendingAuditEntries.Select(x => x with { ImportKey = importKey, Reason = "Wrong time" }).ToList());

        var saved = database.LoadPunchCorrections(importKey);
        var audit = database.LoadPunchCorrectionAudit(importKey);
        var reloadedDraft = _service.CreateDraft(workbook, _analyzer.Analyze(workbook), saved);

        Assert.Equal(new DateTime(2026, 4, 1, 7, 0, 0), reloadedDraft.Assignments.Single().Timestamp);
        Assert.Single(audit);
        Assert.Equal(PunchCorrectionAction.EditTime, audit[0].Action);
        Assert.Contains("07:00", audit[0].AfterValue);
    }

    [Fact]
    public void ValidateMoveTarget_AllowsMovingToFillMissingIn()
    {
        var workbook = Workbook(new DateTime(2026, 4, 1, 12, 0, 0), new DateTime(2026, 4, 2, 8, 0, 0)); // Second punch is missing IN
        var draft = Draft(workbook);
        var targetRecord = new AttendanceRecord
        {
            EmployeeId = "1",
            WorkDate = new DateOnly(2026, 4, 2),
            TimeIn = null,
            TimeOut = new DateTime(2026, 4, 2, 17, 0, 0),
            Status = AttendanceStatus.MissingIn,
            RawPunches = "raw"
        };
        var punchKey = draft.Assignments[0].SourcePunchKey;
        var rules = new AttendanceRules { MinimumWorkDuration = TimeSpan.FromHours(4), MaximumWorkDuration = TimeSpan.FromHours(18) };
        var cutoff = CutoffPeriod.Create(2026, 4, 1);

        var (isValid, reason) = _service.ValidateMoveTarget(draft, punchKey, targetRecord, PunchType.In, rules, cutoff);

        Assert.True(isValid, reason);
    }

    [Fact]
    public void ValidateMoveTarget_RejectsOutBeforeIn()
    {
        var workbook = Workbook(new DateTime(2026, 4, 1, 14, 0, 0));
        var draft = Draft(workbook);
        var targetRecord = new AttendanceRecord
        {
            EmployeeId = "1",
            WorkDate = new DateOnly(2026, 4, 1),
            TimeIn = new DateTime(2026, 4, 1, 17, 0, 0),
            TimeOut = null,
            Status = AttendanceStatus.MissingOut,
            RawPunches = "raw"
        };
        var punchKey = draft.Assignments[0].SourcePunchKey; // timestamp 14:00, which is BEFORE 17:00
        var rules = new AttendanceRules { MinimumWorkDuration = TimeSpan.FromHours(4), MaximumWorkDuration = TimeSpan.FromHours(18) };
        var cutoff = CutoffPeriod.Create(2026, 4, 1);

        var (isValid, reason) = _service.ValidateMoveTarget(draft, punchKey, targetRecord, PunchType.Out, rules, cutoff);

        Assert.False(isValid);
        Assert.Contains("after IN", reason);
    }
    [Fact]
    public void ValidateMoveTarget_RejectsEarlyNightIn()
    {
        var workbook = Workbook(new DateTime(2026, 4, 1, 14, 0, 0)); // Very early IN candidate
        var draft = Draft(workbook);
        var targetRecord = new AttendanceRecord
        {
            EmployeeId = "1",
            WorkDate = new DateOnly(2026, 4, 1),
            TimeIn = null,
            TimeOut = new DateTime(2026, 4, 2, 6, 0, 0), // Next day out
            Status = AttendanceStatus.MissingIn,
            RawPunches = "raw"
        };
        var punchKey = draft.Assignments[0].SourcePunchKey;
        var rules = new AttendanceRules { NightPairStart = new TimeSpan(16, 0, 0) };
        var cutoff = CutoffPeriod.Create(2026, 4, 1);

        var (isValid, reason) = _service.ValidateMoveTarget(draft, punchKey, targetRecord, PunchType.In, rules, cutoff);

        Assert.False(isValid);
        Assert.Contains("too early for night shift", reason);
    }

    private PunchCorrectionDraft Draft(BiometricWorkbook workbook)
    {
        return _service.CreateDraft(workbook, _analyzer.Analyze(workbook));
    }

    private static ReviewDatabase TempDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bio-attendance-punch-tests-{Guid.NewGuid():N}.db");
        return new ReviewDatabase(path);
    }

    private static BiometricWorkbook Workbook(params DateTime[] punches)
    {
        var employee = new EmployeeInfo("1", "Sample Employee", "SECURITY");
        var rawPunches = punches
            .Select((timestamp, index) => new RawPunch(
                employee.Id,
                employee.Name,
                employee.Department,
                timestamp,
                timestamp.Day,
                timestamp.ToString("HH:mm"),
                "Logs",
                index + 1,
                timestamp.Day))
            .ToList();

        return new BiometricWorkbook(
            "sample.xls",
            new DateOnly(2026, 4, 1),
            new DateOnly(2026, 4, 16),
            new[] { employee },
            rawPunches);
    }
}
