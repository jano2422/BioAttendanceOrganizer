using BioAttendanceOrganizer.Core.Models;

namespace BioAttendanceOrganizer.Core.Services;

public sealed class DtrImportWorkflowService
{
    public DtrCutoffInferenceResult InferCutoff(BiometricWorkbook? workbook, int currentYear)
    {
        return InferCutoff(workbook, currentYear, workbook?.DetectedSlot ?? DtrImportSlot.Night);
    }

    public DtrCutoffInferenceResult InferCutoff(BiometricWorkbook? workbook, int currentYear, DtrImportSlot slot)
    {
        if (workbook is null)
        {
            return DtrCutoffInferenceResult.Invalid(
                "No DTR workbook selected",
                "Choose a biometric/DTR workbook to preview and validate.");
        }

        var candidateYears = new[]
            {
                workbook.PeriodStart.Year,
                workbook.PeriodEnd.Year
            }
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        var exactMatch = CandidateCutoffs(candidateYears)
            .FirstOrDefault(cutoff => workbook.PeriodStart == cutoff.StartDate && workbook.PeriodEnd == cutoff.EndDate);
        if (exactMatch is not null)
        {
            return ValidateInferredYear(exactMatch, currentYear, "Workbook period exactly matches the selected cutoff.");
        }

        var allowedMatch = CandidateCutoffs(candidateYears)
            .Where(cutoff => IsAllowedCutoffPeriod(workbook, cutoff, slot))
            .OrderByDescending(cutoff => OverlapDays(workbook.PeriodStart, workbook.PeriodEnd, cutoff.StartDate, cutoff.EndDate))
            .FirstOrDefault();
        if (allowedMatch is not null)
        {
            var message = slot == DtrImportSlot.Morning
                ? $"Morning Shift workbook covers a partial cutoff period ({workbook.PeriodStart:yyyy-MM-dd} to {workbook.PeriodEnd:yyyy-MM-dd})."
                : $"Workbook includes night-shift boundary overlap ({workbook.PeriodStart:yyyy-MM-dd} to {workbook.PeriodEnd:yyyy-MM-dd}).";

            return ValidateInferredYear(
                allowedMatch,
                currentYear,
                message);
        }

        var slotLabel = FormatSlot(slot);
        var allowanceText = slot == DtrImportSlot.Morning
            ? "Morning Shift workbooks may cover a partial period, but they must overlap a 1-15 or 16-end cutoff."
            : "Night Shift workbooks must match a 1-15 or 16-end cutoff with the allowed one-day boundary overlap.";

        return DtrCutoffInferenceResult.Invalid(
            "DTR period does not match any cutoff",
            $"This {slotLabel} workbook contains {workbook.PeriodStart:yyyy-MM-dd} to {workbook.PeriodEnd:yyyy-MM-dd}. {allowanceText}");
    }

    public DtrImportValidationResult ValidateDtrOnly(
        BiometricWorkbook? workbook,
        CutoffPeriod? selectedCutoff)
    {
        return ValidateDtrOnly(workbook, selectedCutoff, workbook?.DetectedSlot ?? DtrImportSlot.Night);
    }

    public DtrImportValidationResult ValidateDtrOnly(
        BiometricWorkbook? workbook,
        CutoffPeriod? selectedCutoff,
        DtrImportSlot slot)
    {
        if (selectedCutoff is null)
        {
            return DtrImportValidationResult.Invalid(
                "Select a cutoff first",
                "Choose a cutoff tile before selecting a DTR workbook.");
        }

        if (workbook is null)
        {
            return DtrImportValidationResult.Invalid(
                "No DTR workbook selected",
                "Choose a biometric/DTR workbook to preview and validate.");
        }

        if (!IsAllowedCutoffPeriod(workbook, selectedCutoff, slot))
        {
            var allowanceText = slot == DtrImportSlot.Morning
                ? "Morning Shift workbooks may be partial, but their dates must overlap the selected cutoff."
                : "Workbooks may include a one-day boundary overlap for night-shift time-outs.";

            return DtrImportValidationResult.Invalid(
                "DTR period does not match the selected cutoff",
                $"Selected cutoff is {selectedCutoff.DisplayName}. {allowanceText} This workbook contains {workbook.PeriodStart:yyyy-MM-dd} to {workbook.PeriodEnd:yyyy-MM-dd}.");
        }

        var periodNote = BuildPeriodNote(workbook, selectedCutoff, slot);

        return new DtrImportValidationResult(
            true,
            $"{FormatSlot(slot)} DTR is ready to import",
            $"{periodNote} Time In/Out will be recognized from the imported punches.",
            workbook.Employees.Count,
            workbook.RawPunches.Count,
            0,
            0);
    }

    private static bool IsAllowedCutoffPeriod(BiometricWorkbook workbook, CutoffPeriod selectedCutoff, DtrImportSlot slot)
    {
        return slot == DtrImportSlot.Morning
            ? OverlapDays(workbook.PeriodStart, workbook.PeriodEnd, selectedCutoff.StartDate, selectedCutoff.EndDate) > 0
            : IsAllowedNightCutoffPeriod(workbook, selectedCutoff);
    }

    private static bool IsAllowedNightCutoffPeriod(BiometricWorkbook workbook, CutoffPeriod selectedCutoff)
    {
        var earliestAllowedStart = selectedCutoff.StartDate.AddDays(-1);
        var latestAllowedEnd = selectedCutoff.EndDate.AddDays(1);

        return workbook.PeriodStart >= earliestAllowedStart &&
               workbook.PeriodStart <= selectedCutoff.StartDate &&
               workbook.PeriodEnd >= selectedCutoff.EndDate &&
               workbook.PeriodEnd <= latestAllowedEnd;
    }

    private static string BuildPeriodNote(BiometricWorkbook workbook, CutoffPeriod selectedCutoff, DtrImportSlot slot)
    {
        if (workbook.PeriodStart == selectedCutoff.StartDate && workbook.PeriodEnd == selectedCutoff.EndDate)
        {
            return "Workbook period matches the selected cutoff.";
        }

        return slot == DtrImportSlot.Morning
            ? $"Morning Shift workbook covers {workbook.PeriodStart:yyyy-MM-dd} to {workbook.PeriodEnd:yyyy-MM-dd}; checking will show that imported period only."
            : $"Workbook includes night-shift boundary overlap ({workbook.PeriodStart:yyyy-MM-dd} to {workbook.PeriodEnd:yyyy-MM-dd}); checking will use raw DTR punches for {selectedCutoff.DisplayName}.";
    }

    private static int OverlapDays(DateOnly firstStart, DateOnly firstEnd, DateOnly secondStart, DateOnly secondEnd)
    {
        var start = firstStart > secondStart ? firstStart : secondStart;
        var end = firstEnd < secondEnd ? firstEnd : secondEnd;
        return end < start ? 0 : end.DayNumber - start.DayNumber + 1;
    }

    private static string FormatSlot(DtrImportSlot slot)
    {
        return slot == DtrImportSlot.Morning ? "Morning Shift" : "Night Shift";
    }

    private static IEnumerable<CutoffPeriod> CandidateCutoffs(IEnumerable<int> years)
    {
        foreach (var year in years)
        {
            for (var month = 1; month <= 12; month++)
            {
                yield return CutoffPeriod.Create(year, month, 1);
                yield return CutoffPeriod.Create(year, month, 2);
            }
        }
    }

    private static DtrCutoffInferenceResult ValidateInferredYear(CutoffPeriod cutoff, int currentYear, string message)
    {
        if (cutoff.Year > currentYear)
        {
            return DtrCutoffInferenceResult.Invalid(
                "Future cutoff is not selectable",
                $"The DTR matches {cutoff.DisplayName}, but future years are not selectable.");
        }

        return DtrCutoffInferenceResult.Valid(cutoff, $"{message} Auto-selected {cutoff.DisplayName}.");
    }
}
