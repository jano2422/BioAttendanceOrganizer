namespace BioAttendanceOrganizer.App.ViewModels;

public sealed class MonthCutoffGroupViewModel
{
    public MonthCutoffGroupViewModel(string monthName, CutoffTileViewModel firstCutoff, CutoffTileViewModel secondCutoff)
    {
        MonthName = monthName;
        FirstCutoff = firstCutoff;
        SecondCutoff = secondCutoff;
    }

    public string MonthName { get; }
    public CutoffTileViewModel FirstCutoff { get; }
    public CutoffTileViewModel SecondCutoff { get; }
}
