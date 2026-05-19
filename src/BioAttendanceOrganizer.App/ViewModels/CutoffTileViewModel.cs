using System.Globalization;
using BioAttendanceOrganizer.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BioAttendanceOrganizer.App.ViewModels;

public sealed class CutoffTileViewModel : ObservableObject
{
    private bool _isSelected;
    private string _importStatus = "Choose";
    private bool _hasSavedSession;
    private bool _hasMorningSession;
    private bool _hasNightSession;

    public CutoffTileViewModel(CutoffPeriod cutoff)
    {
        Cutoff = cutoff;
    }

    public CutoffPeriod Cutoff { get; }
    public int Year => Cutoff.Year;
    public int Month => Cutoff.Month;
    public int CutoffNumber => Cutoff.CutoffNumber;
    public DateOnly StartDate => Cutoff.StartDate;
    public DateOnly EndDate => Cutoff.EndDate;
    public string Key => Cutoff.Key;
    public string MonthName => CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(Month);
    public string CutoffLabel => CutoffNumber == 1 ? "1st Cutoff" : "2nd Cutoff";
    public string RangeLabel => $"{StartDate:MMM d} - {EndDate:MMM d}";
    public string DayRangeLabel => $"{StartDate.Day}-{EndDate.Day}";
    public string DisplayName => Cutoff.DisplayName;

    /// <summary>
    /// Visual state for color-coding: "Complete", "Partial", or "Empty".
    /// </summary>
    public string ImportVisualState => (HasMorningSession, HasNightSession) switch
    {
        (true, true)   => "Complete",
        (true, false)  => "Partial",
        (false, true)  => "Partial",
        _              => "Empty"
    };

    /// <summary>
    /// Icon glyph for at-a-glance import status.
    /// </summary>
    public string ImportStatusIcon => ImportVisualState switch
    {
        "Complete" => "✓",
        "Partial"  => "◐",
        _          => "○"
    };

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string ImportStatus
    {
        get => _importStatus;
        set => SetProperty(ref _importStatus, value);
    }

    public bool HasSavedSession
    {
        get => _hasSavedSession;
        set => SetProperty(ref _hasSavedSession, value);
    }

    public bool HasMorningSession
    {
        get => _hasMorningSession;
        set
        {
            if (SetProperty(ref _hasMorningSession, value))
            {
                HasSavedSession = HasMorningSession || HasNightSession;
                OnPropertyChanged(nameof(ImportVisualState));
                OnPropertyChanged(nameof(ImportStatusIcon));
            }
        }
    }

    public bool HasNightSession
    {
        get => _hasNightSession;
        set
        {
            if (SetProperty(ref _hasNightSession, value))
            {
                HasSavedSession = HasMorningSession || HasNightSession;
                OnPropertyChanged(nameof(ImportVisualState));
                OnPropertyChanged(nameof(ImportStatusIcon));
            }
        }
    }

    public bool HasSession(DtrImportSlot slot)
    {
        return slot == DtrImportSlot.Morning ? HasMorningSession : HasNightSession;
    }

    public void SetSession(DtrImportSlot slot, bool hasSession)
    {
        if (slot == DtrImportSlot.Morning)
        {
            HasMorningSession = hasSession;
        }
        else
        {
            HasNightSession = hasSession;
        }
    }
}
