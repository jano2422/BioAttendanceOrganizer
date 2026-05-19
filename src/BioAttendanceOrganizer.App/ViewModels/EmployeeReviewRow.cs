using BioAttendanceOrganizer.Core.Models;

namespace BioAttendanceOrganizer.App.ViewModels;

public sealed class EmployeeReviewRow
{
    public EmployeeReviewRow(EmployeeAttendanceSummary summary)
    {
        EmployeeId = summary.EmployeeId;
        EmployeeName = summary.EmployeeName;
        Department = summary.Department;
        CleanSessions = summary.CleanSessions;
        NightSessions = summary.NightSessions;
        MissingInRows = summary.MissingInRows;
        MissingOutRows = summary.MissingOutRows;
        CarryoverRows = summary.CarryoverRows;
        DuplicateTapRows = summary.DuplicateTapRows;
        ExtraPunchRows = summary.ExtraPunchRows;
        IssueRows = summary.IssueRows;
        ReviewedRows = summary.ReviewedRows;
        NoRecordRows = summary.NoRecordRows;
        TotalHours = summary.TotalHours;
    }

    public string EmployeeId { get; }
    public string EmployeeName { get; }
    public string Department { get; }
    public int CleanSessions { get; }
    public int NightSessions { get; }
    public int MissingInRows { get; }
    public int MissingOutRows { get; }
    public int CarryoverRows { get; }
    public int DuplicateTapRows { get; }
    public int ExtraPunchRows { get; }
    public int IssueRows { get; }
    public int ReviewedRows { get; }
    public int NoRecordRows { get; }
    public double TotalHours { get; }
    public bool NeedsReview => IssueRows > 0;
    public bool Reviewed => ReviewedRows > 0 && IssueRows == 0;
    public string CheckStatusText => NeedsReview ? "Needs Checking" : "Ready";

    public string IssueSummaryText
    {
        get
        {
            var parts = new List<string>();
            if (MissingInRows > 0)
            {
                parts.Add($"{MissingInRows} missing in");
            }

            if (MissingOutRows > 0)
            {
                parts.Add($"{MissingOutRows} missing out");
            }

            if (ExtraPunchRows > 0)
            {
                parts.Add($"{ExtraPunchRows} extra punch");
            }

            if (NightSessions > 0)
            {
                parts.Add($"{NightSessions} night shift");
            }

            return parts.Count == 0 ? "No issues" : string.Join(", ", parts);
        }
    }
}
