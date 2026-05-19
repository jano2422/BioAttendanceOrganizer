using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace BioAttendanceOrganizer.App.ViewModels;

public sealed class PunchMoveOptionViewModel
{
    public PunchMoveOptionViewModel(string label, string reasonText, bool isValid, IRelayCommand moveCommand)
    {
        Label = label;
        ReasonText = reasonText;
        IsValid = isValid;
        MoveCommand = moveCommand;
    }

    public string Label { get; }
    public string ReasonText { get; }
    public bool IsValid { get; }
    public IRelayCommand MoveCommand { get; }
}
