using BioAttendanceOrganizer.Core.Analysis;
using BioAttendanceOrganizer.Core.Models;
using BioAttendanceOrganizer.Core.Persistence;
using BioAttendanceOrganizer.Core.Review;
using Microsoft.Data.Sqlite;

namespace BioAttendanceOrganizer.Tests;

public sealed class CorrectionPersistenceTests
{
    [Fact]
    public void SavesAndReloadsCorrectionForSameImportedCutoff()
    {
        var database = TempDatabase();
        var workbook = Workbook(
            new DateTime(2026, 4, 2, 6, 0, 0),
            new DateTime(2026, 4, 2, 6, 1, 0));
        var importKey = CorrectionKey.ForWorkbook(workbook);
        var report = new AttendanceAnalyzer().Analyze(workbook);
        var row = report.Records.Single(x => x.Status != AttendanceStatus.NoRecord);

        CorrectionService.Apply(
            row,
            CorrectionAction.EditTimes,
            new DateTime(2026, 4, 2, 6, 0, 0),
            new DateTime(2026, 4, 2, 17, 0, 0),
            "Confirmed by supervisor");
        database.SaveCorrection(CorrectionService.ToCorrectionRecord(row, importKey));

        var freshReport = new AttendanceAnalyzer().Analyze(workbook);
        database.ApplyCorrections(freshReport, importKey);
        var freshRow = freshReport.Records.Single(x => x.Status != AttendanceStatus.NoRecord);

        Assert.Equal(CorrectionAction.EditTimes, freshRow.CorrectionAction);
        Assert.Equal(CorrectionStatus.ManualCorrection, freshRow.FinalCorrectionStatus);
        Assert.Equal(AttendanceStatus.CleanDayShift, freshRow.FinalStatus);
        Assert.Equal("Confirmed by supervisor", freshRow.ReviewerNote);
        Assert.Contains("2026-04-02 06:00", freshRow.RawPunches);
    }

    [Fact]
    public void SavesCorrectionWithoutReviewerNote()
    {
        var database = TempDatabase();
        var workbook = Workbook(new DateTime(2026, 4, 2, 6, 0, 0));
        var importKey = CorrectionKey.ForWorkbook(workbook);
        var report = new AttendanceAnalyzer().Analyze(workbook);
        var row = report.Records.Single(x => x.Status != AttendanceStatus.NoRecord);

        CorrectionService.ApplyInlineEdit(
            row,
            new DateTime(2026, 4, 2, 6, 0, 0),
            new DateTime(2026, 4, 2, 18, 0, 0),
            AttendanceStatus.CleanDayShift,
            string.Empty);
        database.SaveCorrection(CorrectionService.ToCorrectionRecord(row, importKey));

        var freshReport = new AttendanceAnalyzer().Analyze(workbook);
        database.ApplyCorrections(freshReport, importKey);
        var freshRow = freshReport.Records.Single(x => x.Status != AttendanceStatus.NoRecord);

        Assert.Equal(CorrectionAction.EditTimes, freshRow.CorrectionAction);
        Assert.Equal(string.Empty, freshRow.ReviewerNote);
        Assert.Equal(CorrectionStatus.ManualCorrection, freshRow.FinalCorrectionStatus);
    }

    [Fact]
    public void LoadsDefaultRuleSettings()
    {
        var loaded = TempDatabase().LoadRules();

        Assert.Equal(TimeSpan.FromMinutes(2), loaded.DuplicateTapWindow);
        Assert.Equal(TimeSpan.FromHours(4), loaded.MinimumWorkDuration);
        Assert.Equal(TimeSpan.FromHours(20), loaded.MaximumWorkDuration);
        Assert.Equal(new TimeSpan(12, 0, 0), loaded.DayStartEnd);
        Assert.Equal(new TimeSpan(12, 0, 0), loaded.DayEndStart);
        Assert.Equal(new TimeSpan(20, 0, 0), loaded.DayEndEnd);
        Assert.Equal(new TimeSpan(4, 0, 0), loaded.NightPairStart);
        Assert.Equal(new TimeSpan(16, 0, 0), loaded.StrongNightStart);
        Assert.Equal(new TimeSpan(10, 0, 0), loaded.NightEndEnd);
        Assert.True(loaded.EnableCarryoverBoundaryDetection);
        Assert.True(loaded.AutoApproveCleanNightShifts);
    }

    [Fact]
    public void SavesAndReloadsRuleSettings()
    {
        var database = TempDatabase();
        var rules = new AttendanceRules
        {
            DuplicateTapWindow = TimeSpan.FromMinutes(5),
            MinimumWorkDuration = TimeSpan.FromHours(3),
            MaximumWorkDuration = TimeSpan.FromHours(16),
            DayStartEnd = new TimeSpan(11, 0, 0),
            DayEndStart = new TimeSpan(13, 0, 0),
            DayEndEnd = new TimeSpan(21, 0, 0),
            NightPairStart = new TimeSpan(18, 0, 0),
            StrongNightStart = new TimeSpan(22, 0, 0),
            NightEndEnd = new TimeSpan(9, 0, 0),
            EnableCarryoverBoundaryDetection = false,
            AutoApproveCleanNightShifts = false
        };

        database.SaveRules(rules);
        var loaded = database.LoadRules();

        Assert.Equal(TimeSpan.FromMinutes(5), loaded.DuplicateTapWindow);
        Assert.Equal(TimeSpan.FromHours(3), loaded.MinimumWorkDuration);
        Assert.Equal(new TimeSpan(18, 0, 0), loaded.NightPairStart);
        Assert.False(loaded.EnableCarryoverBoundaryDetection);
        Assert.False(loaded.AutoApproveCleanNightShifts);
    }

    [Fact]
    public void RuleSettingCanDisableBoundaryCarryoverDetection()
    {
        var workbook = Workbook(new DateTime(2026, 4, 1, 6, 0, 0));
        var report = new AttendanceAnalyzer().Analyze(workbook, new AttendanceRules
        {
            EnableCarryoverBoundaryDetection = false
        });

        var row = report.Records.Single(x => x.Status != AttendanceStatus.NoRecord);
        Assert.Equal(AttendanceStatus.MissingIn, row.Status);
        Assert.DoesNotContain(IssueFlag.Carryover, row.Flags);
    }

    [Fact]
    public void ImportedSessionRoundTripsByCutoff()
    {
        var database = TempDatabase();

        database.SaveImportedSession("2026-04-1", "import-a", @"C:\cache\april-a.xlsx");
        var loaded = database.LoadImportedSession("2026-04-1");

        Assert.NotNull(loaded);
        Assert.Equal("import-a", loaded.Value.ImportKey);
        Assert.Equal(@"C:\cache\april-a.xlsx", loaded.Value.SourcePath);
    }

    [Fact]
    public void ImportedSessionsRoundTripSeparatelyByCutoffAndSlot()
    {
        var database = TempDatabase();

        database.SaveImportedSession("2026-04-1", DtrImportSlot.Morning, "morning-import", @"C:\cache\morning.xlsx");
        database.SaveImportedSession("2026-04-1", DtrImportSlot.Night, "night-import", @"C:\cache\night.xlsx");

        var morning = database.LoadImportedSession("2026-04-1", DtrImportSlot.Morning);
        var night = database.LoadImportedSession("2026-04-1", DtrImportSlot.Night);

        Assert.NotNull(morning);
        Assert.NotNull(night);
        Assert.Equal("morning-import", morning.Value.ImportKey);
        Assert.Equal(@"C:\cache\morning.xlsx", morning.Value.SourcePath);
        Assert.Equal("night-import", night.Value.ImportKey);
        Assert.Equal(@"C:\cache\night.xlsx", night.Value.SourcePath);
    }

    [Fact]
    public void SaveImportedSessionRejectsDuplicateCutoffSlot()
    {
        var database = TempDatabase();

        database.SaveImportedSession("2026-04-1", DtrImportSlot.Morning, "morning-import", @"C:\cache\morning.xlsx");

        Assert.Throws<SqliteException>(() =>
            database.SaveImportedSession("2026-04-1", DtrImportSlot.Morning, "replacement-import", @"C:\cache\replacement.xlsx"));

        var saved = database.LoadImportedSession("2026-04-1", DtrImportSlot.Morning);
        Assert.NotNull(saved);
        Assert.Equal("morning-import", saved.Value.ImportKey);
        Assert.Equal(@"C:\cache\morning.xlsx", saved.Value.SourcePath);
    }

    [Fact]
    public void DeleteImportedSessionClearsOnlySelectedSlot()
    {
        var database = TempDatabase();

        database.SaveImportedSession("2026-04-1", DtrImportSlot.Morning, "morning-import", @"C:\cache\morning.xlsx");
        database.SaveImportedSession("2026-04-1", DtrImportSlot.Night, "night-import", @"C:\cache\night.xlsx");

        database.DeleteImportedSession("2026-04-1", DtrImportSlot.Morning);

        Assert.Null(database.LoadImportedSession("2026-04-1", DtrImportSlot.Morning));
        var night = database.LoadImportedSession("2026-04-1", DtrImportSlot.Night);
        Assert.NotNull(night);
        Assert.Equal("night-import", night.Value.ImportKey);

        var slots = database.LoadAllImportedCutoffSlots();
        Assert.True(slots["2026-04-1"].Contains(DtrImportSlot.Night));
        Assert.False(slots["2026-04-1"].Contains(DtrImportSlot.Morning));
    }

    [Fact]
    public void LastSessionRoundTrips()
    {
        var database = TempDatabase();

        database.SaveLastSession("2026-04-2", DtrImportSlot.Morning, "import-b", @"C:\cache\april-b.xlsx");
        var loaded = database.LoadLastSession();

        Assert.NotNull(loaded);
        Assert.Equal("2026-04-2", loaded.Value.CutoffKey);
        Assert.Equal(DtrImportSlot.Morning, loaded.Value.Slot);
        Assert.Equal("import-b", loaded.Value.ImportKey);
        Assert.Equal(@"C:\cache\april-b.xlsx", loaded.Value.SourcePath);
    }

    [Fact]
    public void ClearLastSessionRemovesLastSessionSettings()
    {
        var database = TempDatabase();

        database.SaveLastSession("2026-04-2", DtrImportSlot.Morning, "import-b", @"C:\cache\april-b.xlsx");
        database.ClearLastSession();

        Assert.Null(database.LoadLastSession());
    }

    [Fact]
    public void InlineCorrectionAuditSupportsLegacyScheduleCodeColumnWhenClearingTime()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bio-attendance-tests-{Guid.NewGuid():N}.db");
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE inline_correction_audit (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    import_key TEXT NOT NULL,
                    record_key TEXT NOT NULL,
                    cutoff_key TEXT NOT NULL,
                    schedule_code TEXT NOT NULL,
                    employee_id TEXT NOT NULL,
                    work_date TEXT NOT NULL,
                    final_time_in TEXT NULL,
                    final_time_out TEXT NULL,
                    final_status TEXT NOT NULL,
                    reviewer_note TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        var database = new ReviewDatabase(path);

        database.SaveInlineCorrectionAudit(
            "import-a",
            "record-a",
            "2026-04-1",
            "17",
            new DateOnly(2026, 4, 15),
            null,
            new DateTime(2026, 4, 15, 17, 37, 0),
            AttendanceStatus.MissingIn,
            "Cleared IN");

        var loaded = database.LoadInlineCorrectionAudit("import-a");
        Assert.Single(loaded);
        Assert.Equal("17", loaded[0].EmployeeId);
        Assert.Null(loaded[0].FinalTimeIn);
        Assert.Equal(new DateTime(2026, 4, 15, 17, 37, 0), loaded[0].FinalTimeOut);
        Assert.Equal(AttendanceStatus.MissingIn, loaded[0].FinalStatus);
        Assert.Equal("Cleared IN", loaded[0].ReviewerNote);

        using var verifyConnection = new SqliteConnection($"Data Source={path}");
        verifyConnection.Open();
        using var verifyCommand = verifyConnection.CreateCommand();
        verifyCommand.CommandText = """
            SELECT schedule_code, final_time_in, final_time_out
            FROM inline_correction_audit
            WHERE import_key = 'import-a'
            """;
        using var reader = verifyCommand.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("2026-04-1", reader.GetString(0));
        Assert.True(reader.IsDBNull(1));
        Assert.Equal("2026-04-15T17:37:00.0000000", reader.GetString(2));
    }

    [Fact]
    public void MultipleImportedCutoffsCanBeDiscovered()
    {
        var database = TempDatabase();

        database.SaveImportedSession("2026-04-1", "import-a", @"C:\cache\april-a.xlsx");
        database.SaveImportedSession("2026-04-2", "import-b", @"C:\cache\april-b.xlsx");

        var keys = database.LoadAllImportedCutoffKeys();

        Assert.Contains("2026-04-1", keys);
        Assert.Contains("2026-04-2", keys);
    }

    [Fact]
    public void ImportedCutoffSlotsCanBeDiscovered()
    {
        var database = TempDatabase();

        database.SaveImportedSession("2026-04-1", DtrImportSlot.Morning, "import-a", @"C:\cache\morning.xlsx");
        database.SaveImportedSession("2026-04-1", DtrImportSlot.Night, "import-b", @"C:\cache\night.xlsx");
        database.SaveImportedSession("2026-04-2", DtrImportSlot.Night, "import-c", @"C:\cache\night-2.xlsx");

        var slots = database.LoadAllImportedCutoffSlots();

        Assert.True(slots["2026-04-1"].Contains(DtrImportSlot.Morning));
        Assert.True(slots["2026-04-1"].Contains(DtrImportSlot.Night));
        Assert.False(slots["2026-04-2"].Contains(DtrImportSlot.Morning));
        Assert.True(slots["2026-04-2"].Contains(DtrImportSlot.Night));
    }

    [Fact]
    public void ExistingSingleSlotImportedSessionsMigrateToNightShift()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bio-attendance-tests-{Guid.NewGuid():N}.db");
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE imported_sessions (
                    cutoff_key TEXT PRIMARY KEY,
                    import_key TEXT NOT NULL,
                    source_path TEXT NOT NULL,
                    imported_at TEXT NOT NULL
                );

                INSERT INTO imported_sessions (cutoff_key, import_key, source_path, imported_at)
                VALUES ('2026-04-1', 'legacy-import', 'C:\cache\legacy-night.xlsx', '2026-05-01T00:00:00');
                """;
            command.ExecuteNonQuery();
        }

        var database = new ReviewDatabase(path);
        var loaded = database.LoadImportedSession("2026-04-1", DtrImportSlot.Night);
        var slots = database.LoadAllImportedCutoffSlots();

        Assert.NotNull(loaded);
        Assert.Equal("legacy-import", loaded.Value.ImportKey);
        Assert.True(slots["2026-04-1"].Contains(DtrImportSlot.Night));
        Assert.False(slots["2026-04-1"].Contains(DtrImportSlot.Morning));
    }

    private static ReviewDatabase TempDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bio-attendance-tests-{Guid.NewGuid():N}.db");
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
