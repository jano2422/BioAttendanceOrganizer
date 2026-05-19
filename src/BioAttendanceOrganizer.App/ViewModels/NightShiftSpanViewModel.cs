namespace BioAttendanceOrganizer.App.ViewModels;

public sealed class NightShiftSpanViewModel
{
    public NightShiftSpanViewModel(PunchCardViewModel? inPunch, PunchCardViewModel? outPunch, string spanLabel)
    {
        InPunch = inPunch;
        OutPunch = outPunch;
        SpanLabel = spanLabel;
    }

    public PunchCardViewModel? InPunch { get; }
    public PunchCardViewModel? OutPunch { get; }
    public string SpanLabel { get; }
}
