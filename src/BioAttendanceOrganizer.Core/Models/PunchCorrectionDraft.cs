namespace BioAttendanceOrganizer.Core.Models;

public sealed class PunchCorrectionDraft
{
    public PunchCorrectionDraft(IEnumerable<PunchAssignment> assignments)
    {
        Assignments = assignments.Select(x => x.Clone()).ToList();
    }

    public List<PunchAssignment> Assignments { get; }
    public List<PunchCorrectionAuditEntry> PendingAuditEntries { get; } = new();
    public bool HasChanges => PendingAuditEntries.Count > 0 || Assignments.Any(x => x.IsChanged);
    public bool HasPendingChanges => PendingAuditEntries.Count > 0;
}
