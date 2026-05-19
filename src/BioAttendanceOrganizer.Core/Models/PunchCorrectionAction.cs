namespace BioAttendanceOrganizer.Core.Models;

public enum PunchCorrectionAction
{
    MoveDate,
    ReassignEmployee,
    EditTime,
    SetPunchType,
    PairPunches,
    UnpairPunches,
    DeletePunch,
    RestorePunch
}
