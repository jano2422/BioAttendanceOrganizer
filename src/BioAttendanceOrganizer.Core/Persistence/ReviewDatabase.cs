using System.Globalization;
using BioAttendanceOrganizer.Core.Models;
using BioAttendanceOrganizer.Core.Review;
using Microsoft.Data.Sqlite;

namespace BioAttendanceOrganizer.Core.Persistence;

public sealed class ReviewDatabase
{
    private readonly string _databasePath;

    public ReviewDatabase(string? databasePath = null)
    {
        _databasePath = databasePath ?? AppDataPaths.DefaultDatabasePath();
        Initialize();
    }

    public AttendanceRules LoadRules()
    {
        using var connection = OpenConnection();
        var values = LoadSettingMap(connection);
        var defaults = AttendanceRules.CreateDefault();

        return new AttendanceRules
        {
            DuplicateTapWindow = TimeSpan.FromMinutes(GetDouble(values, "DuplicateTapMinutes", defaults.DuplicateTapWindow.TotalMinutes)),
            MinimumWorkDuration = TimeSpan.FromHours(GetDouble(values, "MinimumWorkHours", defaults.MinimumWorkDuration.TotalHours)),
            MaximumWorkDuration = TimeSpan.FromHours(GetDouble(values, "MaximumWorkHours", defaults.MaximumWorkDuration.TotalHours)),
            DayStartEnd = ParseTime(values, "DayStartEnd", defaults.DayStartEnd),
            DayEndStart = ParseTime(values, "DayEndStart", defaults.DayEndStart),
            DayEndEnd = ParseTime(values, "DayEndEnd", defaults.DayEndEnd),
            NightPairStart = ParseTime(values, "NightPairStart", defaults.NightPairStart),
            StrongNightStart = ParseTime(values, "StrongNightStart", defaults.StrongNightStart),
            NightEndEnd = ParseTime(values, "NightEndEnd", defaults.NightEndEnd),
            EnableCarryoverBoundaryDetection = GetBool(values, "EnableCarryoverBoundaryDetection", defaults.EnableCarryoverBoundaryDetection),
            AutoApproveCleanNightShifts = GetBool(values, "AutoApproveCleanNightShifts", defaults.AutoApproveCleanNightShifts)
        };
    }

    public void SaveRules(AttendanceRules rules)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        SaveSetting(connection, "DuplicateTapMinutes", rules.DuplicateTapWindow.TotalMinutes.ToString(CultureInfo.InvariantCulture));
        SaveSetting(connection, "MinimumWorkHours", rules.MinimumWorkDuration.TotalHours.ToString(CultureInfo.InvariantCulture));
        SaveSetting(connection, "MaximumWorkHours", rules.MaximumWorkDuration.TotalHours.ToString(CultureInfo.InvariantCulture));
        SaveSetting(connection, "DayStartEnd", FormatTime(rules.DayStartEnd));
        SaveSetting(connection, "DayEndStart", FormatTime(rules.DayEndStart));
        SaveSetting(connection, "DayEndEnd", FormatTime(rules.DayEndEnd));
        SaveSetting(connection, "NightPairStart", FormatTime(rules.NightPairStart));
        SaveSetting(connection, "StrongNightStart", FormatTime(rules.StrongNightStart));
        SaveSetting(connection, "NightEndEnd", FormatTime(rules.NightEndEnd));
        SaveSetting(connection, "EnableCarryoverBoundaryDetection", rules.EnableCarryoverBoundaryDetection ? "true" : "false");
        SaveSetting(connection, "AutoApproveCleanNightShifts", rules.AutoApproveCleanNightShifts ? "true" : "false");
        transaction.Commit();
    }

    public void SaveInlineCorrectionAudit(
        string importKey,
        string recordKey,
        string cutoffKey,
        string employeeId,
        DateOnly workDate,
        DateTime? finalTimeIn,
        DateTime? finalTimeOut,
        AttendanceStatus finalStatus,
        string reviewerNote)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var hasScheduleCode = GetTableColumns(connection, "inline_correction_audit")
            .Any(x => string.Equals(x.Name, "schedule_code", StringComparison.OrdinalIgnoreCase));
        command.CommandText = hasScheduleCode
            ? """
                INSERT INTO inline_correction_audit (
                    import_key, record_key, cutoff_key, schedule_code, employee_id, work_date,
                    final_time_in, final_time_out, final_status, reviewer_note, updated_at
                )
                VALUES (
                    $importKey, $recordKey, $cutoffKey, $scheduleCode, $employeeId, $workDate,
                    $finalTimeIn, $finalTimeOut, $finalStatus, $reviewerNote, $updatedAt
                )
                """
            : """
                INSERT INTO inline_correction_audit (
                    import_key, record_key, cutoff_key, employee_id, work_date,
                    final_time_in, final_time_out, final_status, reviewer_note, updated_at
                )
                VALUES (
                    $importKey, $recordKey, $cutoffKey, $employeeId, $workDate,
                    $finalTimeIn, $finalTimeOut, $finalStatus, $reviewerNote, $updatedAt
                )
                """;
        command.Parameters.AddWithValue("$importKey", importKey);
        command.Parameters.AddWithValue("$recordKey", recordKey);
        command.Parameters.AddWithValue("$cutoffKey", cutoffKey);
        if (hasScheduleCode)
        {
            command.Parameters.AddWithValue("$scheduleCode", cutoffKey);
        }

        command.Parameters.AddWithValue("$employeeId", employeeId);
        command.Parameters.AddWithValue("$workDate", FormatDate(workDate));
        command.Parameters.AddWithValue("$finalTimeIn", DbValue(finalTimeIn));
        command.Parameters.AddWithValue("$finalTimeOut", DbValue(finalTimeOut));
        command.Parameters.AddWithValue("$finalStatus", finalStatus.ToString());
        command.Parameters.AddWithValue("$reviewerNote", reviewerNote.Trim());
        command.Parameters.AddWithValue("$updatedAt", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<InlineCorrectionAuditEntry> LoadInlineCorrectionAudit(string importKey)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, import_key, record_key, cutoff_key, employee_id, work_date,
                   final_time_in, final_time_out, final_status, reviewer_note, updated_at
            FROM inline_correction_audit
            WHERE import_key = $importKey
            ORDER BY id DESC
            """;
        command.Parameters.AddWithValue("$importKey", importKey);

        var results = new List<InlineCorrectionAuditEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new InlineCorrectionAuditEntry(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                DateOnly.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                ReadDateTime(reader, 6),
                ReadDateTime(reader, 7),
                Enum.Parse<AttendanceStatus>(reader.GetString(8)),
                reader.GetString(9),
                DateTime.Parse(reader.GetString(10), CultureInfo.InvariantCulture)));
        }

        return results;
    }

    public IReadOnlyDictionary<string, PunchCorrectionRecord> LoadPunchCorrections(string importKey)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT import_key, source_punch_key, original_employee_id, original_timestamp,
                   employee_id, employee_name, department, corrected_timestamp, punch_type,
                   pair_key, is_deleted, reason, updated_at
            FROM punch_corrections
            WHERE import_key = $importKey
            """;
        command.Parameters.AddWithValue("$importKey", importKey);

        var results = new Dictionary<string, PunchCorrectionRecord>(StringComparer.OrdinalIgnoreCase);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var record = new PunchCorrectionRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                DateTime.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
                Enum.Parse<PunchType>(reader.GetString(8)),
                reader.GetString(9),
                reader.GetInt32(10) == 1,
                reader.GetString(11),
                DateTime.Parse(reader.GetString(12), CultureInfo.InvariantCulture));
            results[record.SourcePunchKey] = record;
        }

        return results;
    }

    public void SavePunchCorrections(
        string importKey,
        string cutoffKey,
        IReadOnlyList<PunchCorrectionRecord> corrections,
        IReadOnlyList<PunchCorrectionAuditEntry> auditEntries)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        foreach (var correction in corrections)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO punch_corrections (
                    import_key, source_punch_key, original_employee_id, original_timestamp,
                    employee_id, employee_name, department, corrected_timestamp, punch_type,
                    pair_key, is_deleted, reason, updated_at
                )
                VALUES (
                    $importKey, $sourcePunchKey, $originalEmployeeId, $originalTimestamp,
                    $employeeId, $employeeName, $department, $correctedTimestamp, $punchType,
                    $pairKey, $isDeleted, $reason, $updatedAt
                )
                ON CONFLICT(import_key, source_punch_key) DO UPDATE SET
                    employee_id = excluded.employee_id,
                    employee_name = excluded.employee_name,
                    department = excluded.department,
                    corrected_timestamp = excluded.corrected_timestamp,
                    punch_type = excluded.punch_type,
                    pair_key = excluded.pair_key,
                    is_deleted = excluded.is_deleted,
                    reason = excluded.reason,
                    updated_at = excluded.updated_at
                """;
            command.Parameters.AddWithValue("$importKey", importKey);
            command.Parameters.AddWithValue("$sourcePunchKey", correction.SourcePunchKey);
            command.Parameters.AddWithValue("$originalEmployeeId", correction.OriginalEmployeeId);
            command.Parameters.AddWithValue("$originalTimestamp", correction.OriginalTimestamp.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$employeeId", correction.EmployeeId);
            command.Parameters.AddWithValue("$employeeName", correction.EmployeeName);
            command.Parameters.AddWithValue("$department", correction.Department);
            command.Parameters.AddWithValue("$correctedTimestamp", correction.Timestamp.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$punchType", correction.PunchType.ToString());
            command.Parameters.AddWithValue("$pairKey", correction.PairKey);
            command.Parameters.AddWithValue("$isDeleted", correction.IsDeleted ? 1 : 0);
            command.Parameters.AddWithValue("$reason", correction.Reason);
            command.Parameters.AddWithValue("$updatedAt", correction.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }

        foreach (var entry in auditEntries)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO punch_correction_audit (
                    import_key, cutoff_key, source_punch_key, employee_id, work_date,
                    action, before_value, after_value, reason, updated_at
                )
                VALUES (
                    $importKey, $cutoffKey, $sourcePunchKey, $employeeId, $workDate,
                    $action, $beforeValue, $afterValue, $reason, $updatedAt
                )
                """;
            command.Parameters.AddWithValue("$importKey", importKey);
            command.Parameters.AddWithValue("$cutoffKey", cutoffKey);
            command.Parameters.AddWithValue("$sourcePunchKey", entry.SourcePunchKey);
            command.Parameters.AddWithValue("$employeeId", entry.EmployeeId);
            command.Parameters.AddWithValue("$workDate", FormatDate(entry.WorkDate));
            command.Parameters.AddWithValue("$action", entry.Action.ToString());
            command.Parameters.AddWithValue("$beforeValue", entry.BeforeValue);
            command.Parameters.AddWithValue("$afterValue", entry.AfterValue);
            command.Parameters.AddWithValue("$reason", entry.Reason);
            command.Parameters.AddWithValue("$updatedAt", entry.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public IReadOnlyList<PunchCorrectionAuditEntry> LoadPunchCorrectionAudit(string importKey)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, import_key, cutoff_key, source_punch_key, employee_id, work_date,
                   action, before_value, after_value, reason, updated_at
            FROM punch_correction_audit
            WHERE import_key = $importKey
            ORDER BY id DESC
            """;
        command.Parameters.AddWithValue("$importKey", importKey);

        var results = new List<PunchCorrectionAuditEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new PunchCorrectionAuditEntry(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                DateOnly.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                Enum.Parse<PunchCorrectionAction>(reader.GetString(6)),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                DateTime.Parse(reader.GetString(10), CultureInfo.InvariantCulture)));
        }

        return results;
    }

    public IReadOnlyDictionary<string, CorrectionRecord> LoadCorrections(string importKey)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT import_key, record_key, employee_id, work_date, raw_punches, original_status, original_flags,
                   original_time_in, original_time_out, action, corrected_status, corrected_time_in, corrected_time_out,
                   reviewer_note, updated_at
            FROM corrections
            WHERE import_key = $importKey
            """;
        command.Parameters.AddWithValue("$importKey", importKey);

        var results = new Dictionary<string, CorrectionRecord>(StringComparer.OrdinalIgnoreCase);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var record = new CorrectionRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                DateOnly.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                reader.GetString(4),
                Enum.Parse<AttendanceStatus>(reader.GetString(5)),
                reader.GetString(6),
                ReadDateTime(reader, 7),
                ReadDateTime(reader, 8),
                Enum.Parse<CorrectionAction>(reader.GetString(9)),
                ReadEnum<AttendanceStatus>(reader, 10),
                ReadDateTime(reader, 11),
                ReadDateTime(reader, 12),
                reader.GetString(13),
                DateTime.Parse(reader.GetString(14), CultureInfo.InvariantCulture));
            results[record.RecordKey] = record;
        }

        return results;
    }

    public void SaveCorrection(CorrectionRecord correction)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO corrections (
                import_key, record_key, employee_id, work_date, raw_punches, original_status, original_flags,
                original_time_in, original_time_out, action, corrected_status, corrected_time_in, corrected_time_out,
                reviewer_note, updated_at
            )
            VALUES (
                $importKey, $recordKey, $employeeId, $workDate, $rawPunches, $originalStatus, $originalFlags,
                $originalTimeIn, $originalTimeOut, $action, $correctedStatus, $correctedTimeIn, $correctedTimeOut,
                $reviewerNote, $updatedAt
            )
            ON CONFLICT(import_key, record_key) DO UPDATE SET
                action = excluded.action,
                corrected_status = excluded.corrected_status,
                corrected_time_in = excluded.corrected_time_in,
                corrected_time_out = excluded.corrected_time_out,
                reviewer_note = excluded.reviewer_note,
                updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$importKey", correction.ImportKey);
        command.Parameters.AddWithValue("$recordKey", correction.RecordKey);
        command.Parameters.AddWithValue("$employeeId", correction.EmployeeId);
        command.Parameters.AddWithValue("$workDate", correction.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$rawPunches", correction.RawPunches);
        command.Parameters.AddWithValue("$originalStatus", correction.OriginalStatus.ToString());
        command.Parameters.AddWithValue("$originalFlags", correction.OriginalFlags);
        command.Parameters.AddWithValue("$originalTimeIn", DbValue(correction.OriginalTimeIn));
        command.Parameters.AddWithValue("$originalTimeOut", DbValue(correction.OriginalTimeOut));
        command.Parameters.AddWithValue("$action", correction.Action.ToString());
        command.Parameters.AddWithValue("$correctedStatus", correction.CorrectedStatus?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$correctedTimeIn", DbValue(correction.CorrectedTimeIn));
        command.Parameters.AddWithValue("$correctedTimeOut", DbValue(correction.CorrectedTimeOut));
        command.Parameters.AddWithValue("$reviewerNote", correction.ReviewerNote);
        command.Parameters.AddWithValue("$updatedAt", correction.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    public void ApplyCorrections(AttendanceReport report, string importKey)
    {
        var corrections = LoadCorrections(importKey);
        foreach (var record in report.Records)
        {
            if (corrections.TryGetValue(CorrectionKey.ForRecord(record), out var correction))
            {
                CorrectionService.ApplySavedCorrection(record, correction);
            }
        }
    }

    // ── Session persistence ──────────────────────────────────────────────────

    public void SaveImportedSession(string cutoffKey, string importKey, string sourcePath)
    {
        SaveImportedSession(cutoffKey, DtrImportSlot.Night, importKey, sourcePath);
    }

    public void SaveImportedSession(string cutoffKey, DtrImportSlot slot, string importKey, string sourcePath)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO imported_sessions (cutoff_key, slot, import_key, source_path, imported_at)
            VALUES ($cutoffKey, $slot, $importKey, $sourcePath, $importedAt)
            """;
        command.Parameters.AddWithValue("$cutoffKey", cutoffKey);
        command.Parameters.AddWithValue("$slot", slot.ToString());
        command.Parameters.AddWithValue("$importKey", importKey);
        command.Parameters.AddWithValue("$sourcePath", sourcePath);
        command.Parameters.AddWithValue("$importedAt", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    public (string ImportKey, string SourcePath)? LoadImportedSession(string cutoffKey)
    {
        return LoadImportedSession(cutoffKey, DtrImportSlot.Night);
    }

    public (string ImportKey, string SourcePath)? LoadImportedSession(string cutoffKey, DtrImportSlot slot)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT import_key, source_path
            FROM imported_sessions
            WHERE cutoff_key = $cutoffKey
              AND slot = $slot
            """;
        command.Parameters.AddWithValue("$cutoffKey", cutoffKey);
        command.Parameters.AddWithValue("$slot", slot.ToString());
        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            return (reader.GetString(0), reader.GetString(1));
        }

        return null;
    }

    public void DeleteImportedSession(string cutoffKey, DtrImportSlot slot)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM imported_sessions
            WHERE cutoff_key = $cutoffKey
              AND slot = $slot
            """;
        command.Parameters.AddWithValue("$cutoffKey", cutoffKey);
        command.Parameters.AddWithValue("$slot", slot.ToString());
        command.ExecuteNonQuery();
    }

    public IReadOnlySet<string> LoadAllImportedCutoffKeys()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT cutoff_key FROM imported_sessions";
        using var reader = command.ExecuteReader();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            keys.Add(reader.GetString(0));
        }

        return keys;
    }

    public IReadOnlyDictionary<string, IReadOnlySet<DtrImportSlot>> LoadAllImportedCutoffSlots()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT cutoff_key, slot FROM imported_sessions";
        using var reader = command.ExecuteReader();
        var slotsByCutoff = new Dictionary<string, HashSet<DtrImportSlot>>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            var cutoffKey = reader.GetString(0);
            var slotText = reader.GetString(1);
            var slot = Enum.TryParse<DtrImportSlot>(slotText, ignoreCase: true, out var parsed)
                ? parsed
                : DtrImportSlot.Night;

            if (!slotsByCutoff.TryGetValue(cutoffKey, out var slots))
            {
                slots = new HashSet<DtrImportSlot>();
                slotsByCutoff[cutoffKey] = slots;
            }

            slots.Add(slot);
        }

        return slotsByCutoff.ToDictionary(
            x => x.Key,
            x => (IReadOnlySet<DtrImportSlot>)x.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    public void SaveLastSession(string cutoffKey, string importKey, string sourcePath)
    {
        SaveLastSession(cutoffKey, DtrImportSlot.Night, importKey, sourcePath);
    }

    public void SaveLastSession(string cutoffKey, DtrImportSlot slot, string importKey, string sourcePath)
    {
        using var connection = OpenConnection();
        SaveSetting(connection, "LastCutoffKey", cutoffKey);
        SaveSetting(connection, "LastImportSlot", slot.ToString());
        SaveSetting(connection, "LastImportKey", importKey);
        SaveSetting(connection, "LastSourcePath", sourcePath);
    }

    public (string CutoffKey, DtrImportSlot Slot, string ImportKey, string SourcePath)? LoadLastSession()
    {
        using var connection = OpenConnection();
        var values = LoadSettingMap(connection);
        if (values.TryGetValue("LastCutoffKey", out var cutoffKey) &&
            values.TryGetValue("LastImportKey", out var importKey) &&
            values.TryGetValue("LastSourcePath", out var sourcePath) &&
            !string.IsNullOrWhiteSpace(cutoffKey) &&
            !string.IsNullOrWhiteSpace(sourcePath))
        {
            var slot = values.TryGetValue("LastImportSlot", out var slotText) &&
                       Enum.TryParse<DtrImportSlot>(slotText, ignoreCase: true, out var parsed)
                ? parsed
                : DtrImportSlot.Night;

            return (cutoffKey, slot, importKey, sourcePath);
        }

        return null;
    }

    public void ClearLastSession()
    {
        using var connection = OpenConnection();
        DeleteSetting(connection, "LastCutoffKey");
        DeleteSetting(connection, "LastImportSlot");
        DeleteSetting(connection, "LastImportKey");
        DeleteSetting(connection, "LastSourcePath");
    }

    private void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath) ?? ".");
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS app_settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS corrections (
                import_key TEXT NOT NULL,
                record_key TEXT NOT NULL,
                employee_id TEXT NOT NULL,
                work_date TEXT NOT NULL,
                raw_punches TEXT NOT NULL,
                original_status TEXT NOT NULL,
                original_flags TEXT NOT NULL,
                original_time_in TEXT NULL,
                original_time_out TEXT NULL,
                action TEXT NOT NULL,
                corrected_status TEXT NULL,
                corrected_time_in TEXT NULL,
                corrected_time_out TEXT NULL,
                reviewer_note TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                PRIMARY KEY(import_key, record_key)
            );

            CREATE TABLE IF NOT EXISTS inline_correction_audit (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                import_key TEXT NOT NULL,
                record_key TEXT NOT NULL,
                cutoff_key TEXT NOT NULL,
                employee_id TEXT NOT NULL,
                work_date TEXT NOT NULL,
                final_time_in TEXT NULL,
                final_time_out TEXT NULL,
                final_status TEXT NOT NULL,
                reviewer_note TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS punch_corrections (
                import_key TEXT NOT NULL,
                source_punch_key TEXT NOT NULL,
                original_employee_id TEXT NOT NULL,
                original_timestamp TEXT NOT NULL,
                employee_id TEXT NOT NULL,
                employee_name TEXT NOT NULL,
                department TEXT NOT NULL,
                corrected_timestamp TEXT NOT NULL,
                punch_type TEXT NOT NULL,
                pair_key TEXT NOT NULL,
                is_deleted INTEGER NOT NULL,
                reason TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                PRIMARY KEY(import_key, source_punch_key)
            );

            CREATE TABLE IF NOT EXISTS punch_correction_audit (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                import_key TEXT NOT NULL,
                cutoff_key TEXT NOT NULL,
                source_punch_key TEXT NOT NULL,
                employee_id TEXT NOT NULL,
                work_date TEXT NOT NULL,
                action TEXT NOT NULL,
                before_value TEXT NOT NULL,
                after_value TEXT NOT NULL,
                reason TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS imported_sessions (
                cutoff_key TEXT NOT NULL,
                slot TEXT NOT NULL DEFAULT 'Night',
                import_key TEXT NOT NULL,
                source_path TEXT NOT NULL,
                imported_at TEXT NOT NULL,
                PRIMARY KEY(cutoff_key, slot)
            );
            """;
        command.ExecuteNonQuery();
        EnsureImportedSessionsSchema(connection);
        AddColumnIfMissing(connection, "inline_correction_audit", "reviewer_note", "TEXT NOT NULL DEFAULT ''");
    }

    private static void EnsureImportedSessionsSchema(SqliteConnection connection)
    {
        var columns = GetTableColumns(connection, "imported_sessions");
        var primaryKeyColumns = columns
            .Where(x => x.PrimaryKeyOrder > 0)
            .OrderBy(x => x.PrimaryKeyOrder)
            .Select(x => x.Name)
            .ToList();

        var hasSlotColumn = columns.Any(x => string.Equals(x.Name, "slot", StringComparison.OrdinalIgnoreCase));
        if (hasSlotColumn &&
            primaryKeyColumns.Count == 2 &&
            string.Equals(primaryKeyColumns[0], "cutoff_key", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(primaryKeyColumns[1], "slot", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var slotExpression = hasSlotColumn ? "COALESCE(NULLIF(slot, ''), 'Night')" : "'Night'";
        using var transaction = connection.BeginTransaction();
        ExecuteNonQuery(connection, transaction, "DROP TABLE IF EXISTS imported_sessions_legacy");
        ExecuteNonQuery(connection, transaction, "ALTER TABLE imported_sessions RENAME TO imported_sessions_legacy");
        ExecuteNonQuery(connection, transaction, """
            CREATE TABLE imported_sessions (
                cutoff_key TEXT NOT NULL,
                slot TEXT NOT NULL DEFAULT 'Night',
                import_key TEXT NOT NULL,
                source_path TEXT NOT NULL,
                imported_at TEXT NOT NULL,
                PRIMARY KEY(cutoff_key, slot)
            )
            """);
        ExecuteNonQuery(connection, transaction, $"""
            INSERT OR REPLACE INTO imported_sessions (cutoff_key, slot, import_key, source_path, imported_at)
            SELECT cutoff_key, {slotExpression}, import_key, source_path, imported_at
            FROM imported_sessions_legacy
            """);
        ExecuteNonQuery(connection, transaction, "DROP TABLE imported_sessions_legacy");
        transaction.Commit();
    }

    private static IReadOnlyList<(string Name, int PrimaryKeyOrder)> GetTableColumns(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName})";
        using var reader = command.ExecuteReader();
        var columns = new List<(string Name, int PrimaryKeyOrder)>();
        while (reader.Read())
        {
            columns.Add((reader.GetString(1), reader.GetInt32(5)));
        }

        return columns;
    }

    private static void ExecuteNonQuery(SqliteConnection connection, SqliteTransaction transaction, string commandText)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private static void AddColumnIfMissing(SqliteConnection connection, string tableName, string columnName, string definition)
    {
        using var checkCommand = connection.CreateCommand();
        checkCommand.CommandText = $"PRAGMA table_info({tableName})";
        using var reader = checkCommand.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition}";
        alterCommand.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        return connection;
    }

    private static Dictionary<string, string> LoadSettingMap(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM app_settings";
        using var reader = command.ExecuteReader();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            values[reader.GetString(0)] = reader.GetString(1);
        }

        return values;
    }

    private static void SaveSetting(SqliteConnection connection, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_settings (key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static void DeleteSetting(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM app_settings WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        command.ExecuteNonQuery();
    }

    private static double GetDouble(IReadOnlyDictionary<string, string> values, string key, double fallback)
    {
        return values.TryGetValue(key, out var value) &&
               double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static bool GetBool(IReadOnlyDictionary<string, string> values, string key, bool fallback)
    {
        return values.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static TimeSpan ParseTime(IReadOnlyDictionary<string, string> values, string key, TimeSpan fallback)
    {
        return values.TryGetValue(key, out var value) && TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static string FormatTime(TimeSpan value)
    {
        return value.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
    }

    private static string FormatDate(DateOnly value)
    {
        return value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static object DbValue(DateTime? value)
    {
        return value is null ? DBNull.Value : value.Value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTime? ReadDateTime(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : DateTime.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture);
    }

    private static T? ReadEnum<T>(SqliteDataReader reader, int ordinal)
        where T : struct
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var text = reader.GetString(ordinal);
        return string.IsNullOrWhiteSpace(text) ? null : Enum.Parse<T>(text);
    }
}
