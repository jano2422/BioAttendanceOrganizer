namespace BioAttendanceOrganizer.Core.Models;

public sealed record DtrCutoffInferenceResult(
    bool IsValid,
    CutoffPeriod? Cutoff,
    string Title,
    string Message)
{
    public static DtrCutoffInferenceResult Valid(CutoffPeriod cutoff, string message)
    {
        return new DtrCutoffInferenceResult(true, cutoff, "Cutoff selected from DTR", message);
    }

    public static DtrCutoffInferenceResult Invalid(string title, string message)
    {
        return new DtrCutoffInferenceResult(false, null, title, message);
    }
}
