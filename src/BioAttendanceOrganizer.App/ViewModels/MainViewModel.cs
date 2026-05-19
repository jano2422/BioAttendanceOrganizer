using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using BioAttendanceOrganizer.Core.Export;
using BioAttendanceOrganizer.Core.Models;
using BioAttendanceOrganizer.Core.Persistence;
using BioAttendanceOrganizer.Core.Review;
using BioAttendanceOrganizer.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace BioAttendanceOrganizer.App.ViewModels;

public enum DtrWorkspaceView
{
    CutoffBoard,
    ImportReview,
    DtrChecker,
    RecognitionSettings
}

public sealed class MainViewModel : ObservableObject
{
    private readonly ReviewDatabase _database = new();
    private readonly DtrImportService _importService = new();
    private readonly AttendanceComputationService _computationService = new();
    private readonly DtrImportWorkflowService _importWorkflowService = new();
    private readonly PunchCorrectionService _punchCorrectionService = new();
    private readonly AttendanceExcelExporter _excelExporter = new();
    private AttendanceRules _rules;
    private BiometricWorkbook? _workbook;
    private PunchCorrectionDraft? _punchDraft;
    private BiometricWorkbook? _pendingWorkbook;
    private AttendanceReport? _report;
    private string _importKey = string.Empty;
    private int _selectedYear;
    private CutoffTileViewModel? _selectedCutoffTile;
    private EmployeeReviewRow? _selectedEmployee;
    private EmployeeTimelineRow? _selectedTimelineRow;
    private string _selectedEmployeeFilter = "Needs Checking";
    private string _reviewEmployeeSearchText = string.Empty;
    private string _pendingSourcePath = string.Empty;
    private string _sourcePath = string.Empty;
    private DtrImportSlot _selectedImportSlot = DtrImportSlot.Night;
    private DtrWorkspaceView _activeDtrView = DtrWorkspaceView.CutoffBoard;
    private string _importValidationTitle = "Select cutoff and DTR file";
    private string _importValidationMessage = "Choose a cutoff tile, then select a DTR workbook.";
    private string _importEmployeeCountText = "0";
    private string _importRawPunchCountText = "0";
    private string _importWorkbookPeriodText = "-";
    private string _statusMessage = "Select a cutoff, import a DTR workbook, then check Time In/Out records.";
    private string _periodText = "-";
    private string _employeeCountText = "0";
    private string _rawPunchCountText = "0";
    private string _cleanCountText = "0";
    private string _reviewCountText = "0";
    private string _nightCountText = "0";
    private string _carryoverCountText = "0";
    private string _missingRecordCountText = "0";
    private bool _hasLoadedReport;
    private string _dtrEmptyStateText = "Select a cutoff and import a DTR workbook.";
    private bool _canConfirmImport;
    private DateTime? _editorTimeInDate;
    private string _editorTimeInTime = string.Empty;
    private DateTime? _editorTimeOutDate;
    private string _editorTimeOutTime = string.Empty;
    private AttendanceStatus _editorStatus = AttendanceStatus.NeedsReview;
    private string _editorReviewerNote = string.Empty;
    private string _editorDurationPreview = "-";
    private string _editorAutoStatusPreview = "-";
    private string _editorAutoFlagsPreview = "-";
    private string _editorRawPunches = string.Empty;
    private string _duplicateTapMinutes = "2";
    private string _minimumWorkHours = "4";
    private string _maximumWorkHours = "20";
    private string _dayStartEnd = "12:00";
    private string _dayEndStart = "12:00";
    private string _dayEndEnd = "20:00";
    private string _nightPairStart = "16:00";
    private string _strongNightStart = "20:00";
    private string _nightEndEnd = "10:00";
    private bool _enableCarryoverBoundaryDetection = true;
    private bool _autoApproveCleanNightShifts = true;
    private bool _isClearImportConfirmationVisible;
    private string _clearImportConfirmationTitle = "Clear import?";
    private string _clearImportConfirmationMessage = string.Empty;
    private string _clearImportConfirmationIcon = "\uE74D";
    private CutoffTileViewModel? _clearImportTile;
    private DtrImportSlot _clearImportSlot;
    private DateOnly _selectedWeekStart;
    private PunchCardViewModel? _selectedPunch;
    private PunchCardViewModel? _selectedPairPunch;
    private EmployeeOptionViewModel? _selectedPunchEmployee;
    private DateTime? _punchEditDate;
    private string _punchEditTime = string.Empty;
    private string _correctionReason = string.Empty;
    private AttendanceRecord? _selectedPunchRecord;
    private readonly Stack<(List<PunchAssignment> Assignments, int AuditCount)> _punchUndoSnapshots = new();
    private bool _isLoadingDtrEditor;

    public MainViewModel()
    {
        _rules = _database.LoadRules();
        LoadRuleFields(_rules);

        EmployeeRowsView = CollectionViewSource.GetDefaultView(EmployeeRows);
        EmployeeRowsView.Filter = FilterEmployee;

        SelectCutoffCommand = new RelayCommand<CutoffTileViewModel>(tile => SelectCutoff(tile));
        PreviousYearCommand = new RelayCommand(() => SelectedYear--);
        CurrentYearCommand = new RelayCommand(() => SelectedYear = CurrentYear);
        NextYearCommand = new RelayCommand(() => SelectedYear++, () => SelectedYear < CurrentYear);
        SwitchDtrViewCommand = new RelayCommand<string>(SwitchDtrView);
        ImportCommand = new RelayCommand(Import);
        ConfirmImportCommand = new RelayCommand(ConfirmImport, () => CanConfirmImport && _pendingWorkbook is not null);
        ExportDtrCommand = new RelayCommand(ExportDtr, () => _report is not null);
        ClearCurrentImportCommand = new RelayCommand(ShowClearImportConfirmation, CanClearCurrentImport);
        ConfirmClearImportCommand = new RelayCommand(ConfirmClearImport, () => IsClearImportConfirmationVisible);
        CancelClearImportCommand = new RelayCommand(CancelClearImport, () => IsClearImportConfirmationVisible);
        SaveSelectedDtrEditCommand = new RelayCommand(SaveSelectedDtrEdit, () => SelectedTimelineRow is not null);
        ResetSelectedDtrEditCommand = new RelayCommand(ResetSelectedDtrEdit, () => SelectedTimelineRow is not null);
        ClearEditorTimeInCommand = new RelayCommand(ClearEditorTimeIn, () => SelectedTimelineRow is not null);
        ClearEditorTimeOutCommand = new RelayCommand(ClearEditorTimeOut, () => SelectedTimelineRow is not null);
        ApproveSelectedDtrCommand = new RelayCommand(() => ApplySelectedDtrAction(CorrectionAction.ApproveAsCorrect, "DTR row marked correct."), () => SelectedTimelineRow is not null);
        MarkSelectedDtrNeedsReviewCommand = new RelayCommand(() => ApplySelectedDtrAction(CorrectionAction.MarkNeedsReview, "DTR row marked for review."), () => SelectedTimelineRow is not null);
        MarkSelectedDtrMissingInCommand = new RelayCommand(() => ApplySelectedDtrAction(CorrectionAction.MarkMissingIn, "DTR row marked missing Time In."), () => SelectedTimelineRow is not null);
        MarkSelectedDtrMissingOutCommand = new RelayCommand(() => ApplySelectedDtrAction(CorrectionAction.MarkMissingOut, "DTR row marked missing Time Out."), () => SelectedTimelineRow is not null);
        SaveRulesCommand = new RelayCommand(SaveRulesAndReanalyze);
        PreviousWeekCommand = new RelayCommand(() => ShiftPunchWeek(-7));
        NextWeekCommand = new RelayCommand(() => ShiftPunchWeek(7));
        SelectPunchCommand = new RelayCommand<PunchCardViewModel>(SelectPunch);
        ApplyPunchEditCommand = new RelayCommand(ApplyPunchEdit, () => SelectedPunch is { IsPlaceholder: false });
        MarkPunchInCommand = new RelayCommand(() => SetSelectedPunchType(PunchType.In), () => SelectedPunch is { IsPlaceholder: false });
        MarkPunchOutCommand = new RelayCommand(() => SetSelectedPunchType(PunchType.Out), () => SelectedPunch is { IsPlaceholder: false });
        UnpairPunchCommand = new RelayCommand(UnpairSelectedPunch, () => SelectedPunch is { IsPlaceholder: false });
        PairPunchesCommand = new RelayCommand(PairSelectedPunches, () => SelectedPunch is { IsPlaceholder: false } && SelectedPairPunch is { IsPlaceholder: false });
        DeletePunchCommand = new RelayCommand(DeleteSelectedPunch, () => SelectedPunch is { IsPlaceholder: false });
        RestorePunchCommand = new RelayCommand(RestoreSelectedPunch, () => SelectedPunch is { IsPlaceholder: false });
        UndoPunchCorrectionCommand = new RelayCommand(UndoPunchCorrection, () => _punchUndoSnapshots.Count > 0);
        SavePunchCorrectionsCommand = new RelayCommand(SavePunchCorrections, () => _punchDraft?.HasPendingChanges == true);

        var today = DateOnly.FromDateTime(DateTime.Today);
        _selectedYear = today.Year;
        BuildCutoffBoard(CutoffService.ForDate(today).Key);
        LoadLastImportedSession();
    }

    public int CurrentYear => DateTime.Today.Year;
    public ObservableCollection<MonthCutoffGroupViewModel> CutoffMonths { get; } = new();
    public ObservableCollection<CutoffTileViewModel> CutoffTiles { get; } = new();
    public ObservableCollection<EmployeeReviewRow> EmployeeRows { get; } = new();
    public ObservableCollection<EmployeeTimelineRow> SelectedEmployeeRecords { get; } = new();
    public ObservableCollection<RawPunch> SelectedEmployeeRawPunches { get; } = new();
    public ObservableCollection<EditorDateOptionViewModel> EditorTimeInDateOptions { get; } = new();
    public ObservableCollection<EditorDateOptionViewModel> EditorTimeOutDateOptions { get; } = new();
    public ObservableCollection<WeekDayColumnViewModel> WeekDayColumns { get; } = new();
    public ObservableCollection<PunchCardViewModel> PairCandidatePunches { get; } = new();
    public ObservableCollection<PunchAuditRowViewModel> PunchAuditRows { get; } = new();
    public ObservableCollection<EmployeeOptionViewModel> PunchEmployeeOptions { get; } = new();
    public ObservableCollection<PunchMoveOptionViewModel> SmartMoveOptions { get; } = new();
    public ICollectionView EmployeeRowsView { get; }

    public IReadOnlyList<DtrImportSlotOption> ImportSlotOptions { get; } =
    [
        new DtrImportSlotOption(DtrImportSlot.Morning, "Morning Shift"),
        new DtrImportSlotOption(DtrImportSlot.Night, "Night Shift")
    ];

    public IRelayCommand<CutoffTileViewModel> SelectCutoffCommand { get; }
    public IRelayCommand PreviousYearCommand { get; }
    public IRelayCommand CurrentYearCommand { get; }
    public IRelayCommand NextYearCommand { get; }
    public IRelayCommand<string> SwitchDtrViewCommand { get; }
    public IRelayCommand ImportCommand { get; }
    public IRelayCommand ConfirmImportCommand { get; }
    public IRelayCommand ExportDtrCommand { get; }
    public IRelayCommand ClearCurrentImportCommand { get; }
    public IRelayCommand ConfirmClearImportCommand { get; }
    public IRelayCommand CancelClearImportCommand { get; }
    public IRelayCommand SaveSelectedDtrEditCommand { get; }
    public IRelayCommand ResetSelectedDtrEditCommand { get; }
    public IRelayCommand ClearEditorTimeInCommand { get; }
    public IRelayCommand ClearEditorTimeOutCommand { get; }
    public IRelayCommand ApproveSelectedDtrCommand { get; }
    public IRelayCommand MarkSelectedDtrNeedsReviewCommand { get; }
    public IRelayCommand MarkSelectedDtrMissingInCommand { get; }
    public IRelayCommand MarkSelectedDtrMissingOutCommand { get; }
    public IRelayCommand SaveRulesCommand { get; }
    public IRelayCommand PreviousWeekCommand { get; }
    public IRelayCommand NextWeekCommand { get; }
    public IRelayCommand<PunchCardViewModel> SelectPunchCommand { get; }
    public IRelayCommand ApplyPunchEditCommand { get; }
    public IRelayCommand MarkPunchInCommand { get; }
    public IRelayCommand MarkPunchOutCommand { get; }
    public IRelayCommand UnpairPunchCommand { get; }
    public IRelayCommand PairPunchesCommand { get; }
    public IRelayCommand DeletePunchCommand { get; }
    public IRelayCommand RestorePunchCommand { get; }
    public IRelayCommand UndoPunchCorrectionCommand { get; }
    public IRelayCommand SavePunchCorrectionsCommand { get; }

    public IReadOnlyList<string> EmployeeFilters { get; } =
    [
        "Needs Checking",
        "All Employees",
        "Ready",
            "Missing Time In",
            "Missing Time Out",
            "Cutoff Overlap",
            "Extra Punches",
            "Night Shift"
    ];

    public IReadOnlyList<AttendanceStatusOption> AttendanceStatusOptions { get; } =
    [
        new AttendanceStatusOption(AttendanceStatus.CleanDayShift, "Ready - Day Shift"),
        new AttendanceStatusOption(AttendanceStatus.LikelyNightShift, "Ready - Night Shift"),
        new AttendanceStatusOption(AttendanceStatus.NoRecord, "No Record"),
        new AttendanceStatusOption(AttendanceStatus.MissingIn, "Missing Time In"),
        new AttendanceStatusOption(AttendanceStatus.MissingOut, "Missing Time Out"),
        new AttendanceStatusOption(AttendanceStatus.CarryoverFromPreviousCutoff, "From Previous Cutoff"),
        new AttendanceStatusOption(AttendanceStatus.CarryoverToNextCutoff, "To Next Cutoff"),
        new AttendanceStatusOption(AttendanceStatus.TooShort, "Too Short"),
        new AttendanceStatusOption(AttendanceStatus.NeedsReview, "Needs Checking")
    ];

    public int SelectedYear
    {
        get => _selectedYear;
        set
        {
            var clamped = Math.Clamp(value, 1900, CurrentYear);
            if (SetProperty(ref _selectedYear, clamped))
            {
                BuildCutoffBoard();
                NextYearCommand.NotifyCanExecuteChanged();
                StatusMessage = clamped == CurrentYear
                    ? $"Showing current year {CurrentYear}."
                    : $"Showing past year {clamped}.";
            }
            else if (value != clamped)
            {
                OnPropertyChanged();
                StatusMessage = $"Future years are not selectable. Showing {CurrentYear}.";
            }
        }
    }

    public CutoffTileViewModel? SelectedCutoffTile
    {
        get => _selectedCutoffTile;
        private set
        {
            if (SetProperty(ref _selectedCutoffTile, value))
            {
                OnPropertyChanged(nameof(SelectedCutoffText));
                ImportCommand.NotifyCanExecuteChanged();
                ClearCurrentImportCommand.NotifyCanExecuteChanged();
                ValidatePendingImport();
            }
        }
    }

    public DtrImportSlot SelectedImportSlot
    {
        get => _selectedImportSlot;
        set
        {
            if (SetProperty(ref _selectedImportSlot, value))
            {
                OnPropertyChanged(nameof(SelectedImportSlotText));
                OnPropertyChanged(nameof(SelectedCutoffText));
                OnPropertyChanged(nameof(IsMorningSlotSelected));
                OnPropertyChanged(nameof(IsNightSlotSelected));
                ClearCurrentImportCommand.NotifyCanExecuteChanged();
                ValidatePendingImport();
                if (_pendingWorkbook is null && SelectedCutoffTile is not null)
                {
                    LoadSelectedSlotSession();
                }
            }
        }
    }

    public bool IsMorningSlotSelected
    {
        get => SelectedImportSlot == DtrImportSlot.Morning;
        set { if (value) SelectedImportSlot = DtrImportSlot.Morning; }
    }

    public bool IsNightSlotSelected
    {
        get => SelectedImportSlot == DtrImportSlot.Night;
        set { if (value) SelectedImportSlot = DtrImportSlot.Night; }
    }

    public string SelectedImportSlotText => FormatSlot(SelectedImportSlot);

    public string SelectedCutoffText => SelectedCutoffTile is null
        ? $"Select a cutoff | {SelectedImportSlotText}"
        : $"{SelectedCutoffTile.DisplayName} | {SelectedImportSlotText}";

    public DtrWorkspaceView ActiveDtrView
    {
        get => _activeDtrView;
        private set
        {
            if (SetProperty(ref _activeDtrView, value))
            {
                OnPropertyChanged(nameof(IsCutoffBoardViewActive));
                OnPropertyChanged(nameof(IsImportReviewViewActive));
                OnPropertyChanged(nameof(IsDtrCheckerViewActive));
                OnPropertyChanged(nameof(IsRecognitionSettingsViewActive));
            }
        }
    }

    public bool IsCutoffBoardViewActive => ActiveDtrView == DtrWorkspaceView.CutoffBoard;
    public bool IsImportReviewViewActive => ActiveDtrView == DtrWorkspaceView.ImportReview;
    public bool IsDtrCheckerViewActive => ActiveDtrView == DtrWorkspaceView.DtrChecker;
    public bool IsRecognitionSettingsViewActive => ActiveDtrView == DtrWorkspaceView.RecognitionSettings;

    public EmployeeReviewRow? SelectedEmployee
    {
        get => _selectedEmployee;
        set
        {
            if (SetProperty(ref _selectedEmployee, value))
            {
                LoadSelectedEmployeeDetails();
            }
        }
    }

    public EmployeeTimelineRow? SelectedTimelineRow
    {
        get => _selectedTimelineRow;
        set
        {
            if (SetProperty(ref _selectedTimelineRow, value))
            {
                LoadDtrEditor();
                NotifyDtrCommandStates();
                if (value is not null)
                {
                    _selectedWeekStart = StartOfWeek(value.Record.WorkDate);
                    OnPropertyChanged(nameof(SelectedWeekText));
                    BuildPunchCorrectionBoard();
                }
            }
        }
    }

    public string SelectedEmployeeFilter
    {
        get => _selectedEmployeeFilter;
        set
        {
            if (SetProperty(ref _selectedEmployeeFilter, value))
            {
                EmployeeRowsView.Refresh();
            }
        }
    }

    public string ReviewEmployeeSearchText
    {
        get => _reviewEmployeeSearchText;
        set
        {
            if (SetProperty(ref _reviewEmployeeSearchText, value))
            {
                EmployeeRowsView.Refresh();
            }
        }
    }

    public string PendingSourcePath
    {
        get => _pendingSourcePath;
        private set
        {
            if (SetProperty(ref _pendingSourcePath, value))
            {
                OnPropertyChanged(nameof(SourceFileText));
            }
        }
    }

    public string SourcePath
    {
        get => _sourcePath;
        private set
        {
            if (SetProperty(ref _sourcePath, value))
            {
                OnPropertyChanged(nameof(SourceFileText));
            }
        }
    }

    public string SourceFileText
    {
        get
        {
            var path = !string.IsNullOrWhiteSpace(PendingSourcePath) ? PendingSourcePath : SourcePath;
            return string.IsNullOrWhiteSpace(path) ? "No DTR file selected" : Path.GetFileName(path);
        }
    }

    public string ImportValidationTitle
    {
        get => _importValidationTitle;
        private set => SetProperty(ref _importValidationTitle, value);
    }

    public string ImportValidationMessage
    {
        get => _importValidationMessage;
        private set => SetProperty(ref _importValidationMessage, value);
    }

    public string ImportEmployeeCountText
    {
        get => _importEmployeeCountText;
        private set => SetProperty(ref _importEmployeeCountText, value);
    }

    public string ImportRawPunchCountText
    {
        get => _importRawPunchCountText;
        private set => SetProperty(ref _importRawPunchCountText, value);
    }

    public string ImportWorkbookPeriodText
    {
        get => _importWorkbookPeriodText;
        private set => SetProperty(ref _importWorkbookPeriodText, value);
    }

    public bool CanConfirmImport
    {
        get => _canConfirmImport;
        private set
        {
            if (SetProperty(ref _canConfirmImport, value))
            {
                ConfirmImportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string PeriodText
    {
        get => _periodText;
        private set => SetProperty(ref _periodText, value);
    }

    public string EmployeeCountText
    {
        get => _employeeCountText;
        private set => SetProperty(ref _employeeCountText, value);
    }

    public string RawPunchCountText
    {
        get => _rawPunchCountText;
        private set => SetProperty(ref _rawPunchCountText, value);
    }

    public string CleanCountText
    {
        get => _cleanCountText;
        private set => SetProperty(ref _cleanCountText, value);
    }

    public string ReviewCountText
    {
        get => _reviewCountText;
        private set => SetProperty(ref _reviewCountText, value);
    }

    public string NightCountText
    {
        get => _nightCountText;
        private set => SetProperty(ref _nightCountText, value);
    }

    public string CarryoverCountText
    {
        get => _carryoverCountText;
        private set => SetProperty(ref _carryoverCountText, value);
    }

    public string MissingRecordCountText
    {
        get => _missingRecordCountText;
        private set => SetProperty(ref _missingRecordCountText, value);
    }

    public bool HasLoadedReport
    {
        get => _hasLoadedReport;
        private set
        {
            if (SetProperty(ref _hasLoadedReport, value))
            {
                OnPropertyChanged(nameof(IsDtrEmptyVisible));
            }
        }
    }

    public bool IsDtrEmptyVisible => !HasLoadedReport;

    public string DtrEmptyStateText
    {
        get => _dtrEmptyStateText;
        private set => SetProperty(ref _dtrEmptyStateText, value);
    }

    public DateTime? EditorTimeInDate
    {
        get => _editorTimeInDate;
        set
        {
            if (SetProperty(ref _editorTimeInDate, value))
            {
                RefreshDtrEditorPreview();
            }
        }
    }

    public string EditorTimeInTime
    {
        get => _editorTimeInTime;
        set
        {
            if (SetProperty(ref _editorTimeInTime, value))
            {
                RefreshDtrEditorPreview();
            }
        }
    }

    public DateTime? EditorTimeOutDate
    {
        get => _editorTimeOutDate;
        set
        {
            if (SetProperty(ref _editorTimeOutDate, value))
            {
                RefreshDtrEditorPreview();
            }
        }
    }

    public string EditorTimeOutTime
    {
        get => _editorTimeOutTime;
        set
        {
            if (SetProperty(ref _editorTimeOutTime, value))
            {
                RefreshDtrEditorPreview();
            }
        }
    }

    public AttendanceStatus EditorStatus
    {
        get => _editorStatus;
        set => SetProperty(ref _editorStatus, value);
    }

    public string EditorReviewerNote
    {
        get => _editorReviewerNote;
        set => SetProperty(ref _editorReviewerNote, value);
    }

    public string EditorDurationPreview
    {
        get => _editorDurationPreview;
        private set => SetProperty(ref _editorDurationPreview, value);
    }

    public string EditorAutoStatusPreview
    {
        get => _editorAutoStatusPreview;
        private set => SetProperty(ref _editorAutoStatusPreview, value);
    }

    public string EditorAutoFlagsPreview
    {
        get => _editorAutoFlagsPreview;
        private set => SetProperty(ref _editorAutoFlagsPreview, value);
    }

    public string EditorRawPunches
    {
        get => _editorRawPunches;
        private set => SetProperty(ref _editorRawPunches, value);
    }

    public string DuplicateTapMinutes
    {
        get => _duplicateTapMinutes;
        set => SetProperty(ref _duplicateTapMinutes, value);
    }

    public string MinimumWorkHours
    {
        get => _minimumWorkHours;
        set => SetProperty(ref _minimumWorkHours, value);
    }

    public string MaximumWorkHours
    {
        get => _maximumWorkHours;
        set => SetProperty(ref _maximumWorkHours, value);
    }

    public string DayStartEnd
    {
        get => _dayStartEnd;
        set => SetProperty(ref _dayStartEnd, value);
    }

    public string DayEndStart
    {
        get => _dayEndStart;
        set => SetProperty(ref _dayEndStart, value);
    }

    public string DayEndEnd
    {
        get => _dayEndEnd;
        set => SetProperty(ref _dayEndEnd, value);
    }

    public string NightPairStart
    {
        get => _nightPairStart;
        set => SetProperty(ref _nightPairStart, value);
    }

    public string StrongNightStart
    {
        get => _strongNightStart;
        set => SetProperty(ref _strongNightStart, value);
    }

    public string NightEndEnd
    {
        get => _nightEndEnd;
        set => SetProperty(ref _nightEndEnd, value);
    }

    public bool EnableCarryoverBoundaryDetection
    {
        get => _enableCarryoverBoundaryDetection;
        set => SetProperty(ref _enableCarryoverBoundaryDetection, value);
    }

    public bool AutoApproveCleanNightShifts
    {
        get => _autoApproveCleanNightShifts;
        set => SetProperty(ref _autoApproveCleanNightShifts, value);
    }

    public bool IsClearImportConfirmationVisible
    {
        get => _isClearImportConfirmationVisible;
        private set
        {
            if (SetProperty(ref _isClearImportConfirmationVisible, value))
            {
                ConfirmClearImportCommand.NotifyCanExecuteChanged();
                CancelClearImportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ClearImportConfirmationTitle
    {
        get => _clearImportConfirmationTitle;
        private set => SetProperty(ref _clearImportConfirmationTitle, value);
    }

    public string ClearImportConfirmationMessage
    {
        get => _clearImportConfirmationMessage;
        private set => SetProperty(ref _clearImportConfirmationMessage, value);
    }

    public string ClearImportConfirmationIcon
    {
        get => _clearImportConfirmationIcon;
        private set => SetProperty(ref _clearImportConfirmationIcon, value);
    }

    public string SelectedWeekText => $"{_selectedWeekStart:MMM d} - {_selectedWeekStart.AddDays(6):MMM d, yyyy}";

    public PunchCardViewModel? SelectedPunch
    {
        get => _selectedPunch;
        set
        {
            if (SetProperty(ref _selectedPunch, value))
            {
                SelectedPunchRecord = _report?.Records.FirstOrDefault(x => x.EmployeeId == SelectedPunch?.EmployeeId && x.WorkDate == SelectedPunch?.WorkDate);
                LoadSelectedPunchEditor();
                ComputeSmartMoveOptions();
                NotifyPunchCommandStates();
            }
        }
    }

    public AttendanceRecord? SelectedPunchRecord
    {
        get => _selectedPunchRecord;
        private set => SetProperty(ref _selectedPunchRecord, value);
    }

    public PunchCardViewModel? SelectedPairPunch
    {
        get => _selectedPairPunch;
        set
        {
            if (SetProperty(ref _selectedPairPunch, value))
            {
                PairPunchesCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public EmployeeOptionViewModel? SelectedPunchEmployee
    {
        get => _selectedPunchEmployee;
        set => SetProperty(ref _selectedPunchEmployee, value);
    }

    public DateTime? PunchEditDate
    {
        get => _punchEditDate;
        set => SetProperty(ref _punchEditDate, value);
    }

    public string PunchEditTime
    {
        get => _punchEditTime;
        set => SetProperty(ref _punchEditTime, value);
    }

    public string CorrectionReason
    {
        get => _correctionReason;
        set => SetProperty(ref _correctionReason, value);
    }

    private void BuildCutoffBoard(string? preferredCutoffKey = null, bool loadSelectedSession = false)
    {
        preferredCutoffKey ??= SelectedCutoffTile?.Key;
        var importedCutoffSlots = _database.LoadAllImportedCutoffSlots();
        var tiles = Enumerable.Range(1, 12)
            .SelectMany(month => CutoffService.GenerateMonth(SelectedYear, month))
            .Select(cutoff => new CutoffTileViewModel(cutoff))
            .ToList();

        foreach (var tile in tiles)
        {
            if (importedCutoffSlots.TryGetValue(tile.Key, out var slots))
            {
                tile.HasMorningSession = slots.Contains(DtrImportSlot.Morning);
                tile.HasNightSession = slots.Contains(DtrImportSlot.Night);
            }

            ApplyCutoffTileImportStatus(tile);
        }

        Replace(CutoffTiles, tiles);
        Replace(CutoffMonths, tiles
            .GroupBy(x => x.Month)
            .OrderBy(x => x.Key)
            .Select(group =>
            {
                var ordered = group.OrderBy(x => x.CutoffNumber).ToList();
                return new MonthCutoffGroupViewModel(ordered[0].MonthName, ordered[0], ordered[1]);
            }));

        var selected = tiles.FirstOrDefault(x => x.Key == preferredCutoffKey) ??
                       tiles.FirstOrDefault(x => x.Month == DateTime.Today.Month && x.CutoffNumber == (DateTime.Today.Day <= 15 ? 1 : 2)) ??
                       tiles.First();
        SelectCutoff(selected, loadSelectedSession);
    }

    private void SelectCutoff(CutoffTileViewModel? tile, bool loadSavedSession = true)
    {
        if (tile is null)
        {
            return;
        }

        foreach (var cutoffTile in CutoffTiles)
        {
            cutoffTile.IsSelected = ReferenceEquals(cutoffTile, tile);
        }

        SelectedCutoffTile = tile;
        StatusMessage = $"Selected {tile.DisplayName}.";
        if (loadSavedSession && _pendingWorkbook is null)
        {
            LoadSelectedSlotSession();
        }
    }

    private void SetSelectedImportSlotSilently(DtrImportSlot slot)
    {
        if (_selectedImportSlot == slot)
        {
            return;
        }

        _selectedImportSlot = slot;
        OnPropertyChanged(nameof(SelectedImportSlot));
        OnPropertyChanged(nameof(SelectedImportSlotText));
        OnPropertyChanged(nameof(SelectedCutoffText));
        ClearCurrentImportCommand.NotifyCanExecuteChanged();
        ValidatePendingImport();
    }

    private static void ApplyCutoffTileImportStatus(CutoffTileViewModel tile)
    {
        tile.ImportStatus = (tile.HasMorningSession, tile.HasNightSession) switch
        {
            (true, true) => "Both imported",
            (true, false) => "Morning imported",
            (false, true) => "Night imported",
            _ => "Choose"
        };
    }

    private void SwitchDtrView(string? viewName)
    {
        if (string.IsNullOrWhiteSpace(viewName) ||
            !Enum.TryParse<DtrWorkspaceView>(viewName, ignoreCase: true, out var view))
        {
            return;
        }

        ActiveDtrView = view;
    }

    private void Import()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Biometric Attendance Workbook",
            Filter = "Excel workbooks (*.xls;*.xlsx)|*.xls;*.xlsx|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            StatusMessage = "Reading DTR workbook for preview...";
            _pendingWorkbook = _importService.ImportWorkbook(dialog.FileName);
            PendingSourcePath = dialog.FileName;
            SetSelectedImportSlotSilently(_pendingWorkbook.DetectedSlot);
            var inferred = _importWorkflowService.InferCutoff(_pendingWorkbook, CurrentYear);
            if (!inferred.IsValid || inferred.Cutoff is null)
            {
                CanConfirmImport = false;
                ImportEmployeeCountText = _pendingWorkbook.Employees.Count.ToString(CultureInfo.InvariantCulture);
                ImportRawPunchCountText = _pendingWorkbook.RawPunches.Count.ToString(CultureInfo.InvariantCulture);
                ImportWorkbookPeriodText = $"{_pendingWorkbook.PeriodStart:yyyy-MM-dd} to {_pendingWorkbook.PeriodEnd:yyyy-MM-dd}";
                ImportValidationTitle = inferred.Title;
                ImportValidationMessage = inferred.Message;
                ActiveDtrView = DtrWorkspaceView.ImportReview;
                StatusMessage = inferred.Title;
                return;
            }

            SelectInferredCutoff(inferred.Cutoff);
            ValidatePendingImport();
            ActiveDtrView = DtrWorkspaceView.ImportReview;
            StatusMessage = CanConfirmImport
                ? $"Detected {SelectedImportSlotText} format from {Path.GetFileName(dialog.FileName)}. {inferred.Message} Confirm import to check Time In/Out."
                : "DTR preview found a validation issue. Resolve it before confirming import.";
        }
        catch (Exception ex)
        {
            _pendingWorkbook = null;
            CanConfirmImport = false;
            ImportEmployeeCountText = "0";
            ImportRawPunchCountText = "0";
            ImportWorkbookPeriodText = "-";
            ImportValidationTitle = "Import failed";
            ImportValidationMessage = ex.Message;
            ActiveDtrView = DtrWorkspaceView.ImportReview;
            StatusMessage = "Import preview failed.";
            MessageBox.Show(ex.Message, "Import preview failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SelectInferredCutoff(CutoffPeriod cutoff)
    {
        if (SelectedYear != cutoff.Year)
        {
            _selectedYear = cutoff.Year;
            OnPropertyChanged(nameof(SelectedYear));
            BuildCutoffBoard(cutoff.Key, loadSelectedSession: false);
            NextYearCommand.NotifyCanExecuteChanged();
            return;
        }

        var tile = CutoffTiles.FirstOrDefault(x => x.Key == cutoff.Key);
        if (tile is null)
        {
            BuildCutoffBoard(cutoff.Key, loadSelectedSession: false);
            return;
        }

        SelectCutoff(tile, loadSavedSession: false);
    }

    private void ConfirmImport()
    {
        if (_pendingWorkbook is null || !CanConfirmImport || SelectedCutoffTile is null)
        {
            StatusMessage = "Choose a valid DTR workbook before confirming import.";
            return;
        }

        try
        {
            if (HasExistingSavedImport(SelectedCutoffTile, SelectedImportSlot))
            {
                var message = BuildExistingImportMessage(SelectedCutoffTile, SelectedImportSlot);
                CanConfirmImport = false;
                ImportValidationTitle = "Import already exists";
                ImportValidationMessage = message;
                StatusMessage = message;
                ActiveDtrView = DtrWorkspaceView.ImportReview;
                return;
            }

            var cachedPath = CacheImportedWorkbook(PendingSourcePath, SelectedCutoffTile.Key, SelectedImportSlot);
            _workbook = _importService.ImportWorkbook(cachedPath);
            _pendingWorkbook = null;
            PendingSourcePath = string.Empty;
            CanConfirmImport = false;
            SourcePath = cachedPath;
            AnalyzeCurrentWorkbook();
            _database.SaveImportedSession(SelectedCutoffTile.Key, SelectedImportSlot, _importKey, cachedPath);
            _database.SaveLastSession(SelectedCutoffTile.Key, SelectedImportSlot, _importKey, cachedPath);
            SelectedCutoffTile.SetSession(SelectedImportSlot, true);
            ApplyCutoffTileImportStatus(SelectedCutoffTile);
            ClearCurrentImportCommand.NotifyCanExecuteChanged();
            ActiveDtrView = DtrWorkspaceView.DtrChecker;
            StatusMessage = $"Imported {Path.GetFileName(SourcePath)} for {SelectedCutoffTile.DisplayName} ({SelectedImportSlotText}).";
        }
        catch (Exception ex)
        {
            StatusMessage = "Import failed while saving the cutoff session.";
            MessageBox.Show(ex.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportDtr()
    {
        if (_report is null)
        {
            StatusMessage = "Load a DTR import before exporting.";
            return;
        }

        var cutoffPart = SelectedCutoffTile?.Key ?? $"{_report.Summary.PeriodStart:yyyyMMdd}-{_report.Summary.PeriodEnd:yyyyMMdd}";
        var slotPart = SelectedImportSlotText.Replace(" ", "-", StringComparison.OrdinalIgnoreCase);
        var dialog = new SaveFileDialog
        {
            Title = "Export DTR Excel Workbook",
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
            FileName = $"{SanitizeFileName(cutoffPart)}-{slotPart}-DTR-Export.xlsx",
            AddExtension = true,
            DefaultExt = ".xlsx",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _excelExporter.Export(
                _report,
                dialog.FileName,
                SelectedImportSlot,
                SelectedCutoffTile?.DisplayName ?? PeriodText);
            StatusMessage = $"Exported DTR Excel workbook: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = "DTR export failed.";
            MessageBox.Show(ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool CanClearCurrentImport()
    {
        return SelectedCutoffTile?.HasSession(SelectedImportSlot) == true;
    }

    private void ShowClearImportConfirmation()
    {
        if (SelectedCutoffTile is null || !CanClearCurrentImport())
        {
            StatusMessage = $"No {SelectedImportSlotText} import is saved for the selected cutoff.";
            return;
        }

        _clearImportTile = SelectedCutoffTile;
        _clearImportSlot = SelectedImportSlot;
        var slotText = FormatSlot(_clearImportSlot);
        ClearImportConfirmationIcon = "\uE74D";
        ClearImportConfirmationTitle = $"Clear {slotText} import?";
        ClearImportConfirmationMessage =
            $"This removes the saved {slotText} import for {_clearImportTile.DisplayName}. " +
            "The other shift, source files, corrections, and audit history will not be deleted.";
        IsClearImportConfirmationVisible = true;
    }

    private void CancelClearImport()
    {
        IsClearImportConfirmationVisible = false;
        _clearImportTile = null;
    }

    private void ConfirmClearImport()
    {
        if (_clearImportTile is null)
        {
            CancelClearImport();
            return;
        }

        var tile = _clearImportTile;
        var slot = _clearImportSlot;
        var slotText = FormatSlot(slot);

        _database.DeleteImportedSession(tile.Key, slot);
        var lastSession = _database.LoadLastSession();
        if (lastSession is not null &&
            string.Equals(lastSession.Value.CutoffKey, tile.Key, StringComparison.OrdinalIgnoreCase) &&
            lastSession.Value.Slot == slot)
        {
            _database.ClearLastSession();
        }

        tile.SetSession(slot, false);
        ApplyCutoffTileImportStatus(tile);
        ClearCurrentImportCommand.NotifyCanExecuteChanged();
        IsClearImportConfirmationVisible = false;
        _clearImportTile = null;

        if (ReferenceEquals(tile, SelectedCutoffTile) && SelectedImportSlot == slot)
        {
            ClearLoadedWorkbook($"No {slotText} DTR imported for {tile.DisplayName}.");
        }

        StatusMessage = $"Cleared {slotText} import for {tile.DisplayName}.";
    }

    private void LoadLastImportedSession()
    {
        var session = _database.LoadLastSession();
        if (session is null)
        {
            return;
        }

        SetSelectedImportSlotSilently(session.Value.Slot);

        if (!TrySelectCutoffKey(session.Value.CutoffKey, out var tile))
        {
            StatusMessage = "Last imported cutoff could not be found.";
            return;
        }

        LoadSavedCutoffSession(tile, session.Value.Slot, (session.Value.ImportKey, session.Value.SourcePath));
    }

    private bool TrySelectCutoffKey(string cutoffKey, out CutoffTileViewModel tile)
    {
        tile = CutoffTiles.FirstOrDefault(x => x.Key == cutoffKey)!;
        if (tile is not null)
        {
            SelectCutoff(tile, loadSavedSession: false);
            return true;
        }

        var parts = cutoffKey.Split('-');
        if (parts.Length >= 1 && int.TryParse(parts[0], CultureInfo.InvariantCulture, out var year))
        {
            _selectedYear = year;
            OnPropertyChanged(nameof(SelectedYear));
            BuildCutoffBoard(cutoffKey, loadSelectedSession: false);
            NextYearCommand.NotifyCanExecuteChanged();
            tile = CutoffTiles.FirstOrDefault(x => x.Key == cutoffKey)!;
            if (tile is not null)
            {
                SelectCutoff(tile, loadSavedSession: false);
                return true;
            }
        }

        tile = null!;
        return false;
    }

    private bool LoadSelectedSlotSession()
    {
        if (SelectedCutoffTile is null)
        {
            return false;
        }

        return LoadSavedCutoffSession(SelectedCutoffTile, SelectedImportSlot);
    }

    private bool LoadSavedCutoffSession(
        CutoffTileViewModel tile,
        DtrImportSlot slot,
        (string ImportKey, string SourcePath)? savedSession = null)
    {
        var session = savedSession ?? _database.LoadImportedSession(tile.Key, slot);
        if (session is null)
        {
            tile.SetSession(slot, false);
            ApplyCutoffTileImportStatus(tile);
            var message = $"No {FormatSlot(slot)} DTR imported for {tile.DisplayName}.";
            ClearLoadedWorkbook(message);
            StatusMessage = message;
            return false;
        }

        tile.SetSession(slot, true);
        if (!File.Exists(session.Value.SourcePath))
        {
            tile.ImportStatus = "File missing";
            var message = $"Import file missing for {tile.DisplayName} ({FormatSlot(slot)}): {session.Value.SourcePath}";
            ClearLoadedWorkbook(message);
            StatusMessage = message;
            return false;
        }

        try
        {
            _pendingWorkbook = null;
            PendingSourcePath = string.Empty;
            CanConfirmImport = false;
            _workbook = _importService.ImportWorkbook(session.Value.SourcePath);
            SourcePath = session.Value.SourcePath;
            AnalyzeCurrentWorkbook();
            _database.SaveLastSession(tile.Key, slot, _importKey, session.Value.SourcePath);
            ApplyCutoffTileImportStatus(tile);
            ClearCurrentImportCommand.NotifyCanExecuteChanged();
            ActiveDtrView = DtrWorkspaceView.DtrChecker;
            StatusMessage = $"Loaded saved {FormatSlot(slot)} import for {tile.DisplayName}.";
            return true;
        }
        catch (Exception ex)
        {
            tile.ImportStatus = "Load failed";
            StatusMessage = $"Saved import could not be loaded: {ex.Message}";
            return false;
        }
    }

    private void ClearLoadedWorkbook(string? emptyStateText = null)
    {
        _workbook = null;
        _punchDraft = null;
        _report = null;
        _importKey = string.Empty;
        ExportDtrCommand.NotifyCanExecuteChanged();
        ClearCurrentImportCommand.NotifyCanExecuteChanged();
        HasLoadedReport = false;
        DtrEmptyStateText = string.IsNullOrWhiteSpace(emptyStateText)
            ? $"No {SelectedImportSlotText} DTR imported for the selected cutoff."
            : emptyStateText;
        SourcePath = string.Empty;
        PeriodText = "-";
        EmployeeCountText = "0";
        RawPunchCountText = "0";
        CleanCountText = "0";
        ReviewCountText = "0";
        NightCountText = "0";
        CarryoverCountText = "0";
        MissingRecordCountText = "0";
        EmployeeRows.Clear();
        EmployeeRowsView.Refresh();
        SelectedEmployeeRecords.Clear();
        SelectedEmployeeRawPunches.Clear();
        WeekDayColumns.Clear();
        PunchAuditRows.Clear();
        SelectedEmployee = null;
        SelectedTimelineRow = null;
        ActiveDtrView = DtrWorkspaceView.DtrChecker;
    }

    private static string CacheImportedWorkbook(string sourcePath, string cutoffKey, DtrImportSlot slot)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The selected DTR workbook could not be found.", sourcePath);
        }

        var extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".xlsx";
        }

        var fileName = $"{SanitizeFileName(cutoffKey)}-{slot}-{DateTime.Now:yyyyMMddHHmmssfff}{extension}";
        var destination = Path.Combine(AppDataPaths.ImportCacheFolder(), fileName);
        File.Copy(sourcePath, destination, overwrite: true);
        return destination;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }

    private void ValidatePendingImport()
    {
        if (_pendingWorkbook is null)
        {
            ImportValidationTitle = SelectedCutoffTile is null ? "Select cutoff first" : "No DTR workbook selected";
            ImportValidationMessage = SelectedCutoffTile is null
                ? "Choose one of the January to December cutoff tiles before importing."
                : "Choose a biometric/DTR workbook to preview and validate.";
            ImportEmployeeCountText = "0";
            ImportRawPunchCountText = "0";
            ImportWorkbookPeriodText = "-";
            CanConfirmImport = false;
            return;
        }

        var pendingWorkbook = _pendingWorkbook;
        if (pendingWorkbook is not null && pendingWorkbook.DetectedSlot != SelectedImportSlot)
        {
            SetSelectedImportSlotSilently(pendingWorkbook.DetectedSlot);
        }

        var result = _importWorkflowService.ValidateDtrOnly(pendingWorkbook, SelectedCutoffTile?.Cutoff);
        ImportValidationTitle = result.Title;
        ImportValidationMessage = result.Message;
        ImportEmployeeCountText = result.EmployeeCount.ToString(CultureInfo.InvariantCulture);
        ImportRawPunchCountText = result.RawPunchCount.ToString(CultureInfo.InvariantCulture);
        ImportWorkbookPeriodText = pendingWorkbook is null
            ? "-"
            : $"{pendingWorkbook.PeriodStart:yyyy-MM-dd} to {pendingWorkbook.PeriodEnd:yyyy-MM-dd}";
        if (result.IsValid && SelectedCutoffTile is not null && HasExistingSavedImport(SelectedCutoffTile, SelectedImportSlot))
        {
            ImportValidationTitle = "Import already exists";
            ImportValidationMessage = BuildExistingImportMessage(SelectedCutoffTile, SelectedImportSlot);
            CanConfirmImport = false;
            return;
        }

        CanConfirmImport = result.IsValid;
    }

    private bool HasExistingSavedImport(CutoffTileViewModel tile, DtrImportSlot slot)
    {
        return tile.HasSession(slot) || _database.LoadImportedSession(tile.Key, slot) is not null;
    }

    private static string BuildExistingImportMessage(CutoffTileViewModel tile, DtrImportSlot slot)
    {
        return $"{FormatSlot(slot)} DTR is already imported for {tile.DisplayName}. Clear the existing import first before importing another workbook.";
    }

    private void AnalyzeCurrentWorkbook()
    {
        if (_workbook is null || SelectedCutoffTile is null)
        {
            return;
        }

        StatusMessage = "Recognizing DTR Time In/Out from raw punches...";
        _importKey = CorrectionKey.ForWorkbook(_workbook);
        var baseReport = _computationService.Analyze(_workbook, _rules);
        _punchDraft = _punchCorrectionService.CreateDraft(
            _workbook,
            baseReport,
            _database.LoadPunchCorrections(_importKey));
        var correctedWorkbook = _punchCorrectionService.ApplyDraftToWorkbook(_workbook, _punchDraft);
        _report = _computationService.Analyze(correctedWorkbook, _rules);
        _database.ApplyCorrections(_report, _importKey);
        RefreshFromReport(SelectedEmployee?.EmployeeId);
        LoadPunchAuditRows();
        HasLoadedReport = true;
        DtrEmptyStateText = string.Empty;
        ExportDtrCommand.NotifyCanExecuteChanged();
    }

    private void RefreshFromReport(string? employeeToSelect = null, AttendanceRecord? recordToSelect = null)
    {
        if (_report is null)
        {
            return;
        }

        var refreshedSummary = AttendanceSummary.From(
            _report.Summary.PeriodStart,
            _report.Summary.PeriodEnd,
            _report.Summary.EmployeeCount,
            _report.Workbook.RawPunches.Count,
            _report.Records);
        _report = _report with { Summary = refreshedSummary };

        PeriodText = $"{_report.Summary.PeriodStart:yyyy-MM-dd} to {_report.Summary.PeriodEnd:yyyy-MM-dd}";
        EmployeeCountText = _report.Summary.EmployeeCount.ToString(CultureInfo.InvariantCulture);
        RawPunchCountText = _report.Summary.RawPunchCount.ToString(CultureInfo.InvariantCulture);
        CleanCountText = _report.Summary.CleanSessions.ToString(CultureInfo.InvariantCulture);
        ReviewCountText = _report.Summary.IssueRows.ToString(CultureInfo.InvariantCulture);
        NightCountText = _report.Summary.NightSessions.ToString(CultureInfo.InvariantCulture);
        CarryoverCountText = _report.Summary.CarryoverRows.ToString(CultureInfo.InvariantCulture);
        MissingRecordCountText = _report.Summary.NoRecordRows.ToString(CultureInfo.InvariantCulture);

        Replace(EmployeeRows, _report.EmployeeSummaries.Select(x => new EmployeeReviewRow(x)));
        EmployeeRowsView.Refresh();

        SelectedEmployee = EmployeeRows.FirstOrDefault(x => x.EmployeeId == employeeToSelect) ??
                           EmployeeRows.FirstOrDefault(x => x.NeedsReview) ??
                           EmployeeRows.FirstOrDefault();
        if (recordToSelect is not null)
        {
            SelectedTimelineRow = SelectedEmployeeRecords.FirstOrDefault(x =>
                x.Record.EmployeeId == recordToSelect.EmployeeId &&
                x.Record.WorkDate == recordToSelect.WorkDate &&
                x.Record.RawPunches == recordToSelect.RawPunches);
        }
    }

    private void LoadSelectedEmployeeDetails()
    {
        SelectedTimelineRow = null;
        SelectedEmployeeRecords.Clear();
        SelectedEmployeeRawPunches.Clear();

        if (_report is null || SelectedEmployee is null)
        {
            return;
        }

        Replace(SelectedEmployeeRecords, _report.Records
            .Where(x => x.EmployeeId == SelectedEmployee.EmployeeId)
            .OrderBy(x => x.WorkDate)
            .ThenBy(x => x.FinalTimeIn ?? x.FinalTimeOut ?? x.WorkDate.ToDateTime(TimeOnly.MinValue))
            .Select(x => new EmployeeTimelineRow(x)));
        Replace(SelectedEmployeeRawPunches, _report.Workbook.RawPunches
            .Where(x => x.EmployeeId == SelectedEmployee.EmployeeId)
            .OrderBy(x => x.Timestamp));
        SelectedTimelineRow = SelectedEmployeeRecords.FirstOrDefault(x => x.Record.NeedsReview) ??
                              SelectedEmployeeRecords.FirstOrDefault();
        BuildPunchCorrectionBoard();
    }

    private void BuildPunchCorrectionBoard()
    {
        WeekDayColumns.Clear();
        PairCandidatePunches.Clear();
        PunchEmployeeOptions.Clear();

        if (SelectedEmployee is null)
        {
            return;
        }

        if (_selectedWeekStart == default)
        {
            var firstDate = SelectedEmployeeRecords.FirstOrDefault()?.Record.WorkDate ??
                            SelectedCutoffTile?.StartDate ??
                            DateOnly.FromDateTime(DateTime.Today);
            _selectedWeekStart = StartOfWeek(firstDate);
            OnPropertyChanged(nameof(SelectedWeekText));
        }

        var rows = SelectedEmployeeRecords
            .Select(x => x.Record)
            .Where(x => x.EmployeeId == SelectedEmployee.EmployeeId)
            .ToList();

        for (var offset = 0; offset < 7; offset++)
        {
            var date = _selectedWeekStart.AddDays(offset);
            var column = new WeekDayColumnViewModel(date);
            foreach (var row in rows
                         .Where(x => x.WorkDate == date)
                         .OrderBy(x => x.FinalTimeIn ?? x.FinalTimeOut ?? x.WorkDate.ToDateTime(TimeOnly.MinValue)))
            {
                column.Shifts.Add(new WeeklyShiftViewModel(row));
            }

            WeekDayColumns.Add(column);
        }

        LoadPunchAuditRows();
        SavePunchCorrectionsCommand.NotifyCanExecuteChanged();
        UndoPunchCorrectionCommand.NotifyCanExecuteChanged();
    }

    private void LoadPunchAuditRows()
    {
        if (string.IsNullOrWhiteSpace(_importKey))
        {
            PunchAuditRows.Clear();
            return;
        }

        var employeeId = SelectedEmployee?.EmployeeId;
        var rows = _database.LoadPunchCorrectionAudit(_importKey)
            .Select(x => new PunchAuditRowViewModel(x))
            .Concat(_database.LoadInlineCorrectionAudit(_importKey).Select(x => new PunchAuditRowViewModel(x)))
            .Where(x => string.IsNullOrWhiteSpace(employeeId) || x.EmployeeId == employeeId)
            .OrderByDescending(x => x.UpdatedAt);
        Replace(PunchAuditRows, rows);
    }

    private void SelectPunch(PunchCardViewModel? punch)
    {
        SelectedPunch = punch;
    }

    private void LoadSelectedPunchEditor()
    {
        if (SelectedPunch is null || SelectedPunch.IsPlaceholder)
        {
            PunchEditDate = null;
            PunchEditTime = string.Empty;
            SelectedPunchEmployee = null;
            return;
        }

        PunchEditDate = SelectedPunch.Timestamp.Date;
        PunchEditTime = SelectedPunch.Timestamp.ToString("HH:mm", CultureInfo.InvariantCulture);
        SelectedPunchEmployee = PunchEmployeeOptions.FirstOrDefault(x => x.EmployeeId == SelectedPunch.EmployeeId);
    }

    private void ComputeSmartMoveOptions()
    {
        SmartMoveOptions.Clear();
        if (_punchDraft is null || SelectedPunch is null || SelectedPunch.IsPlaceholder || SelectedPunch.IsDeleted || SelectedCutoffTile is null)
        {
            return;
        }

        var sourceKey = SelectedPunch.SourcePunchKey;

        // Find candidate records to move to (MissingIn, MissingOut within a day or two)
        var employeeRecords = _report?.Records.Where(x => x.EmployeeId == SelectedPunch.EmployeeId).ToList() ?? new List<AttendanceRecord>();
        var candidates = employeeRecords.Where(x => Math.Abs((x.WorkDate.DayNumber - SelectedPunch.WorkDate.DayNumber)) <= 2).ToList();

        foreach (var record in candidates)
        {
            if (record.FinalStatus == AttendanceStatus.MissingIn)
            {
                var isNight = record.Status == AttendanceStatus.LikelyNightShift || record.IsOvernightShift;
                var targetDate = record.WorkDate;
                var (isValid, reason) = _punchCorrectionService.ValidateMoveTarget(_punchDraft, sourceKey, record, PunchType.In, _rules, SelectedCutoffTile.Cutoff);
                var label = isNight ? $"Use as night IN (Move to {targetDate:MMM d})" : $"Move before OUT on {targetDate:MMM d}";
                SmartMoveOptions.Add(new PunchMoveOptionViewModel(label, reason, isValid, new RelayCommand(() => ApplySmartMove(targetDate, PunchType.In), () => isValid)));
            }
            else if (record.FinalStatus == AttendanceStatus.MissingOut)
            {
                var isNight = record.Status == AttendanceStatus.LikelyNightShift || record.IsOvernightShift;
                var targetDate = isNight ? record.WorkDate.AddDays(1) : record.WorkDate; // OUT for night shift is next day
                var (isValid, reason) = _punchCorrectionService.ValidateMoveTarget(_punchDraft, sourceKey, record, PunchType.Out, _rules, SelectedCutoffTile.Cutoff);
                var label = isNight ? $"Use as next-day OUT (Move to {targetDate:MMM d})" : $"Move after IN on {targetDate:MMM d}";
                SmartMoveOptions.Add(new PunchMoveOptionViewModel(label, reason, isValid, new RelayCommand(() => ApplySmartMove(targetDate, PunchType.Out), () => isValid)));
            }
        }
    }

    private void ApplySmartMove(DateOnly targetDate, PunchType targetType)
    {
        if (_punchDraft is null || SelectedPunch is null) return;
        
        try
        {
            PushPunchUndoSnapshot();
            _punchCorrectionService.MoveDate(_punchDraft, SelectedPunch.SourcePunchKey, targetDate, SelectedCutoffTile?.Key ?? string.Empty, "Smart Move");
            _punchCorrectionService.SetPunchType(_punchDraft, SelectedPunch.SourcePunchKey, targetType, SelectedCutoffTile?.Key ?? string.Empty, "Smart Move");
            BuildPunchCorrectionBoard();
            StatusMessage = "Smart move applied. Enter a reason and save corrections.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }
    
    private void ApplyPunchEdit()
    {
        if (_punchDraft is null || SelectedPunch is null)
        {
            return;
        }

        if (PunchEditDate is null || !TimeSpan.TryParse(PunchEditTime, CultureInfo.InvariantCulture, out var time))
        {
            StatusMessage = "Use a valid punch date and HH:mm time.";
            return;
        }

        try
        {
            PushPunchUndoSnapshot();
            var timestamp = PunchEditDate.Value.Date.Add(time);
            _punchCorrectionService.EditTime(_punchDraft, SelectedPunch.SourcePunchKey, timestamp, SelectedCutoffTile?.Key ?? string.Empty, CorrectionReason);

            if (SelectedPunchEmployee is not null && SelectedPunchEmployee.EmployeeId != SelectedPunch.EmployeeId)
            {
                _punchCorrectionService.ReassignEmployee(_punchDraft, SelectedPunch.SourcePunchKey, SelectedPunchEmployee.Employee, SelectedCutoffTile?.Key ?? string.Empty, CorrectionReason);
            }

            BuildPunchCorrectionBoard();
            StatusMessage = "Punch edit staged. Enter a reason and save corrections.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void SetSelectedPunchType(PunchType punchType)
    {
        if (_punchDraft is null || SelectedPunch is null)
        {
            return;
        }

        PushPunchUndoSnapshot();
        _punchCorrectionService.SetPunchType(_punchDraft, SelectedPunch.SourcePunchKey, punchType, SelectedCutoffTile?.Key ?? string.Empty, CorrectionReason);
        BuildPunchCorrectionBoard();
        StatusMessage = $"Punch marked {punchType}.";
    }

    private void PairSelectedPunches()
    {
        if (_punchDraft is null || SelectedPunch is null || SelectedPairPunch is null)
        {
            return;
        }

        try
        {
            PushPunchUndoSnapshot();
            var ordered = new[] { SelectedPunch, SelectedPairPunch }.OrderBy(x => x.Timestamp).ToList();
            _punchCorrectionService.PairPunches(_punchDraft, ordered[0].SourcePunchKey, ordered[1].SourcePunchKey, SelectedCutoffTile?.Key ?? string.Empty, CorrectionReason);
            BuildPunchCorrectionBoard();
            StatusMessage = "Punches paired. Enter a reason and save corrections.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void UnpairSelectedPunch()
    {
        if (_punchDraft is null || SelectedPunch is null)
        {
            return;
        }

        PushPunchUndoSnapshot();
        _punchCorrectionService.UnpairPunch(_punchDraft, SelectedPunch.SourcePunchKey, SelectedCutoffTile?.Key ?? string.Empty, CorrectionReason);
        BuildPunchCorrectionBoard();
        StatusMessage = "Punch unpaired.";
    }

    private void DeleteSelectedPunch()
    {
        if (_punchDraft is null || SelectedPunch is null)
        {
            return;
        }

        PushPunchUndoSnapshot();
        _punchCorrectionService.DeletePunch(_punchDraft, SelectedPunch.SourcePunchKey, SelectedCutoffTile?.Key ?? string.Empty, CorrectionReason);
        BuildPunchCorrectionBoard();
        StatusMessage = "Punch deleted from corrected recognition. It can be restored.";
    }

    private void RestoreSelectedPunch()
    {
        if (_punchDraft is null || SelectedPunch is null)
        {
            return;
        }

        PushPunchUndoSnapshot();
        _punchCorrectionService.RestorePunch(_punchDraft, SelectedPunch.SourcePunchKey, SelectedCutoffTile?.Key ?? string.Empty, CorrectionReason);
        BuildPunchCorrectionBoard();
        StatusMessage = "Punch restored.";
    }

    private void SavePunchCorrections()
    {
        if (_punchDraft is null || _workbook is null || SelectedCutoffTile is null)
        {
            return;
        }

        var errors = _punchCorrectionService.Validate(_punchDraft, SelectedCutoffTile.Cutoff, CorrectionReason);
        if (errors.Count > 0)
        {
            StatusMessage = errors[0];
            MessageBox.Show(string.Join(Environment.NewLine, errors), "Correction validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var reason = CorrectionReason.Trim();
        var records = _punchCorrectionService.ToCorrectionRecords(_punchDraft, _importKey, reason);
        var audits = _punchDraft.PendingAuditEntries
            .Select(x => x with
            {
                ImportKey = _importKey,
                CutoffKey = SelectedCutoffTile.Key,
                Reason = reason
            })
            .ToList();
        _database.SavePunchCorrections(_importKey, SelectedCutoffTile.Key, records, audits);
        _punchUndoSnapshots.Clear();
        CorrectionReason = string.Empty;
        AnalyzeCurrentWorkbook();
        StatusMessage = "Punch corrections saved and DTR recognition refreshed.";
    }

    private void UndoPunchCorrection()
    {
        if (_punchDraft is null || _punchUndoSnapshots.Count == 0)
        {
            return;
        }

        var snapshot = _punchUndoSnapshots.Pop();
        _punchDraft.Assignments.Clear();
        _punchDraft.Assignments.AddRange(snapshot.Assignments.Select(x => x.Clone()));
        while (_punchDraft.PendingAuditEntries.Count > snapshot.AuditCount)
        {
            _punchDraft.PendingAuditEntries.RemoveAt(_punchDraft.PendingAuditEntries.Count - 1);
        }

        BuildPunchCorrectionBoard();
        StatusMessage = "Last unsaved punch correction was undone.";
    }

    private void ShiftPunchWeek(int days)
    {
        _selectedWeekStart = _selectedWeekStart == default
            ? StartOfWeek(SelectedCutoffTile?.StartDate ?? DateOnly.FromDateTime(DateTime.Today))
            : _selectedWeekStart.AddDays(days);
        OnPropertyChanged(nameof(SelectedWeekText));
        BuildPunchCorrectionBoard();
    }

    private void PushPunchUndoSnapshot()
    {
        if (_punchDraft is null)
        {
            return;
        }

        _punchUndoSnapshots.Push((_punchDraft.Assignments.Select(x => x.Clone()).ToList(), _punchDraft.PendingAuditEntries.Count));
        UndoPunchCorrectionCommand.NotifyCanExecuteChanged();
    }

    private void NotifyPunchCommandStates()
    {
        ApplyPunchEditCommand.NotifyCanExecuteChanged();
        MarkPunchInCommand.NotifyCanExecuteChanged();
        MarkPunchOutCommand.NotifyCanExecuteChanged();
        UnpairPunchCommand.NotifyCanExecuteChanged();
        PairPunchesCommand.NotifyCanExecuteChanged();
        DeletePunchCommand.NotifyCanExecuteChanged();
        RestorePunchCommand.NotifyCanExecuteChanged();
    }

    private void NotifyDtrCommandStates()
    {
        SaveSelectedDtrEditCommand.NotifyCanExecuteChanged();
        ResetSelectedDtrEditCommand.NotifyCanExecuteChanged();
        ClearEditorTimeInCommand.NotifyCanExecuteChanged();
        ClearEditorTimeOutCommand.NotifyCanExecuteChanged();
        ApproveSelectedDtrCommand.NotifyCanExecuteChanged();
        MarkSelectedDtrNeedsReviewCommand.NotifyCanExecuteChanged();
        MarkSelectedDtrMissingInCommand.NotifyCanExecuteChanged();
        MarkSelectedDtrMissingOutCommand.NotifyCanExecuteChanged();
    }

    private static DateOnly StartOfWeek(DateOnly date)
    {
        var day = (int)date.DayOfWeek;
        return date.AddDays(-day);
    }

    private void LoadDtrEditor()
    {
        var record = SelectedTimelineRow?.Record;
        _isLoadingDtrEditor = true;
        if (record is null)
        {
            EditorTimeInDateOptions.Clear();
            EditorTimeOutDateOptions.Clear();
            EditorTimeInDate = null;
            EditorTimeInTime = string.Empty;
            EditorTimeOutDate = null;
            EditorTimeOutTime = string.Empty;
            EditorStatus = AttendanceStatus.NeedsReview;
            EditorReviewerNote = string.Empty;
            EditorDurationPreview = "-";
            EditorAutoStatusPreview = "-";
            EditorAutoFlagsPreview = "-";
            EditorRawPunches = string.Empty;
            _isLoadingDtrEditor = false;
            return;
        }

        BuildEditorDateOptions(record);
        EditorTimeInDate = record.FinalTimeIn?.Date ?? DefaultEditorTimeInDate(record);
        EditorTimeInTime = record.FinalTimeIn?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
        EditorTimeOutDate = record.FinalTimeOut?.Date ?? DefaultEditorTimeOutDate(record);
        EditorTimeOutTime = record.FinalTimeOut?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
        EditorStatus = record.FinalStatus;
        EditorReviewerNote = record.ReviewerNote;
        EditorRawPunches = string.IsNullOrWhiteSpace(record.RawPunches) ? "No raw punches for this day." : record.RawPunches;
        _isLoadingDtrEditor = false;
        RefreshDtrEditorPreview();
    }

    private void RefreshDtrEditorPreview()
    {
        if (_isLoadingDtrEditor)
        {
            return;
        }

        if (SelectedTimelineRow?.Record is { } record)
        {
            RefreshEditorTimeOutDateOptions(record);
        }

        if (!TryReadEditorDateTime(EditorTimeInDate, EditorTimeInTime, out var timeIn) ||
            !TryReadEditorDateTime(EditorTimeOutDate, EditorTimeOutTime, out var timeOut))
        {
            EditorDurationPreview = "Invalid date/time";
            EditorAutoStatusPreview = "-";
            EditorAutoFlagsPreview = "-";
            EditorStatus = AttendanceStatus.NeedsReview;
            return;
        }

        if (!CorrectionService.TryInferInlineEdit(timeIn, timeOut, _rules, out var inference, out var error))
        {
            EditorDurationPreview = error;
            EditorAutoStatusPreview = "Invalid";
            EditorAutoFlagsPreview = "-";
            EditorStatus = AttendanceStatus.NeedsReview;
            return;
        }

        EditorStatus = inference.Status;
        EditorAutoStatusPreview = FormatStatus(inference.Status);
        EditorAutoFlagsPreview = inference.Flags.Count == 0 ? "-" : string.Join(", ", inference.Flags.Select(x => x.ToString()));
        if (inference.Duration is not null)
        {
            EditorDurationPreview = inference.Duration.Value.TotalHours.ToString("0.00", CultureInfo.InvariantCulture) + " hours";
        }
        else
        {
            EditorDurationPreview = inference.Status == AttendanceStatus.NoRecord ? "No time pair" : "Incomplete time pair";
        }
    }

    private void ClearEditorTimeIn()
    {
        EditorTimeInTime = string.Empty;
        RefreshDtrEditorPreview();
    }

    private void ClearEditorTimeOut()
    {
        EditorTimeOutTime = string.Empty;
        RefreshDtrEditorPreview();
    }

    private void RefreshEditorTimeOutDateOptions(AttendanceRecord record)
    {
        var currentOutDate = EditorTimeOutDate;
        var wasLoading = _isLoadingDtrEditor;
        _isLoadingDtrEditor = true;

        EditorTimeOutDateOptions.Clear();
        AddEditorDateOption(EditorTimeOutDateOptions, record.WorkDate, "work date");
        if (ShouldOfferNextOutDate(record) || EditorTimeInImpliesNextOutDate(record))
        {
            AddEditorDateOption(EditorTimeOutDateOptions, record.WorkDate.AddDays(1), "next day");
        }

        if (currentOutDate is null)
        {
            EditorTimeOutDate = DefaultEditorTimeOutDate(record);
        }
        else if (IsAllowedEditorDate(EditorTimeOutDateOptions, DateOnly.FromDateTime(currentOutDate.Value)))
        {
            EditorTimeOutDate = currentOutDate.Value.Date;
        }
        else
        {
            EditorTimeOutDate = record.WorkDate.ToDateTime(TimeOnly.MinValue);
        }

        _isLoadingDtrEditor = wasLoading;
    }

    private void BuildEditorDateOptions(AttendanceRecord record)
    {
        EditorTimeInDateOptions.Clear();
        EditorTimeOutDateOptions.Clear();

        AddEditorDateOption(EditorTimeInDateOptions, record.WorkDate, "work date");
        AddEditorDateOption(EditorTimeOutDateOptions, record.WorkDate, "work date");

        if (ShouldAllowPreviousInDate(record))
        {
            AddEditorDateOption(EditorTimeInDateOptions, record.WorkDate.AddDays(-1), "previous night");
        }

        if (ShouldOfferNextOutDate(record))
        {
            AddEditorDateOption(EditorTimeOutDateOptions, record.WorkDate.AddDays(1), "next day");
        }
    }

    private void AddEditorDateOption(ObservableCollection<EditorDateOptionViewModel> options, DateOnly date, string label)
    {
        if (!IsAllowedByCutoffBoundary(date) || options.Any(x => DateOnly.FromDateTime(x.Date) == date))
        {
            return;
        }

        options.Add(EditorDateOptionViewModel.For(date, label));
    }

    private bool ShouldAllowPreviousInDate(AttendanceRecord record)
    {
        if (record.FinalStatus == AttendanceStatus.CarryoverFromPreviousCutoff ||
            record.Status == AttendanceStatus.CarryoverFromPreviousCutoff)
        {
            return true;
        }

        return record.FinalStatus == AttendanceStatus.MissingIn &&
               record.FinalTimeOut.HasValue &&
               TimeOnly.FromDateTime(record.FinalTimeOut.Value).ToTimeSpan() <= _rules.NightEndEnd;
    }

    private bool ShouldOfferNextOutDate(AttendanceRecord record)
    {
        if (ShouldDefaultNextOutDate(record) ||
            record.FinalStatus == AttendanceStatus.MissingOut ||
            record.Status == AttendanceStatus.MissingOut ||
            record.Flags.Contains(IssueFlag.MissingOut))
        {
            return record.FinalTimeIn.HasValue || record.TimeIn.HasValue;
        }

        return false;
    }

    private bool EditorTimeInImpliesNextOutDate(AttendanceRecord record)
    {
        if (!TryReadEditorDateTime(EditorTimeInDate, EditorTimeInTime, out var timeIn) || timeIn is null)
        {
            return false;
        }

        return DateOnly.FromDateTime(timeIn.Value) == record.WorkDate &&
               TimeOnly.FromDateTime(timeIn.Value).ToTimeSpan() >= _rules.NightPairStart;
    }

    private bool ShouldDefaultNextOutDate(AttendanceRecord record)
    {
        if (record.FinalStatus == AttendanceStatus.LikelyNightShift ||
            record.Status == AttendanceStatus.LikelyNightShift ||
            record.FinalStatus == AttendanceStatus.CarryoverToNextCutoff ||
            record.Status == AttendanceStatus.CarryoverToNextCutoff ||
            record.IsOvernightShift ||
            record.Flags.Contains(IssueFlag.LikelyNightShift))
        {
            return true;
        }

        return record.FinalTimeIn.HasValue &&
               TimeOnly.FromDateTime(record.FinalTimeIn.Value).ToTimeSpan() >= _rules.NightPairStart;
    }

    private DateTime DefaultEditorTimeInDate(AttendanceRecord record)
    {
        var preferred = ShouldAllowPreviousInDate(record) ? record.WorkDate.AddDays(-1) : record.WorkDate;
        return IsAllowedEditorDate(EditorTimeInDateOptions, preferred)
            ? preferred.ToDateTime(TimeOnly.MinValue)
            : record.WorkDate.ToDateTime(TimeOnly.MinValue);
    }

    private DateTime DefaultEditorTimeOutDate(AttendanceRecord record)
    {
        var preferred = ShouldDefaultNextOutDate(record) ? record.WorkDate.AddDays(1) : record.WorkDate;
        return IsAllowedEditorDate(EditorTimeOutDateOptions, preferred)
            ? preferred.ToDateTime(TimeOnly.MinValue)
            : record.WorkDate.ToDateTime(TimeOnly.MinValue);
    }

    private bool ValidateEditorDateSelection(DateTime? timeIn, DateTime? timeOut, out string message)
    {
        if (timeIn is not null && !IsAllowedEditorDate(EditorTimeInDateOptions, DateOnly.FromDateTime(timeIn.Value)))
        {
            message = "Time In date is outside the allowed dates for the selected DTR row.";
            return false;
        }

        if (timeOut is not null && !IsAllowedEditorDate(EditorTimeOutDateOptions, DateOnly.FromDateTime(timeOut.Value)))
        {
            message = "Time Out date is outside the allowed dates for the selected DTR row.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private static bool IsAllowedEditorDate(IEnumerable<EditorDateOptionViewModel> options, DateOnly date)
    {
        return options.Any(x => DateOnly.FromDateTime(x.Date) == date);
    }

    private bool IsAllowedByCutoffBoundary(DateOnly date)
    {
        if (SelectedCutoffTile is null)
        {
            return true;
        }

        return date >= SelectedCutoffTile.StartDate.AddDays(-1) &&
               date <= SelectedCutoffTile.EndDate.AddDays(1);
    }

    private static string FormatSlot(DtrImportSlot slot)
    {
        return slot == DtrImportSlot.Morning ? "Morning Shift" : "Night Shift";
    }

    private static string FormatStatus(AttendanceStatus status)
    {
        return status switch
        {
            AttendanceStatus.CleanDayShift => "Day Shift",
            AttendanceStatus.LikelyNightShift => "Night Shift",
            AttendanceStatus.NoRecord => "No Record",
            AttendanceStatus.MissingIn => "Missing In",
            AttendanceStatus.MissingOut => "Missing Out",
            AttendanceStatus.CarryoverFromPreviousCutoff => "Carryover From Previous Cutoff",
            AttendanceStatus.CarryoverToNextCutoff => "Carryover To Next Cutoff",
            AttendanceStatus.TooShort => "Too Short",
            AttendanceStatus.NeedsReview => "Needs Review",
            _ => status.ToString()
        };
    }

    private void SaveSelectedDtrEdit()
    {
        if (_report is null || SelectedTimelineRow?.Record is null || string.IsNullOrWhiteSpace(_importKey))
        {
            StatusMessage = "Select an employee day before saving a correction.";
            return;
        }

        if (!TryReadEditorDateTime(EditorTimeInDate, EditorTimeInTime, out var timeIn) ||
            !TryReadEditorDateTime(EditorTimeOutDate, EditorTimeOutTime, out var timeOut))
        {
            StatusMessage = "Correction not saved. Use a valid date and HH:mm time.";
            return;
        }

        if (!ValidateEditorDateSelection(timeIn, timeOut, out var dateValidationMessage))
        {
            StatusMessage = dateValidationMessage;
            return;
        }

        var record = SelectedTimelineRow.Record;
        try
        {
            CorrectionService.ApplyInlineEdit(record, timeIn, timeOut, _rules, EditorReviewerNote);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Correction not saved. {ex.Message}";
            return;
        }

        SaveRecordCorrection(record);
        RefreshFromReport(SelectedEmployee?.EmployeeId, record);
        if (SelectedCutoffTile is not null)
        {
            ApplyCutoffTileImportStatus(SelectedCutoffTile);
        }

        StatusMessage = "DTR correction saved.";
    }

    private void ApplySelectedDtrAction(CorrectionAction action, string successMessage)
    {
        if (_report is null || SelectedTimelineRow?.Record is null || string.IsNullOrWhiteSpace(_importKey))
        {
            StatusMessage = "Select an employee day before applying a correction.";
            return;
        }

        if (!TryReadEditorDateTime(EditorTimeInDate, EditorTimeInTime, out var timeIn) ||
            !TryReadEditorDateTime(EditorTimeOutDate, EditorTimeOutTime, out var timeOut))
        {
            StatusMessage = "Correction not saved. Use a valid date and HH:mm time.";
            return;
        }

        var record = SelectedTimelineRow.Record;
        CorrectionService.Apply(record, action, timeIn, timeOut, EditorReviewerNote);
        SaveRecordCorrection(record);
        RefreshFromReport(SelectedEmployee?.EmployeeId, record);
        if (SelectedCutoffTile is not null)
        {
            ApplyCutoffTileImportStatus(SelectedCutoffTile);
        }

        StatusMessage = successMessage;
    }

    private void ResetSelectedDtrEdit()
    {
        if (_report is null || SelectedTimelineRow?.Record is null || string.IsNullOrWhiteSpace(_importKey))
        {
            return;
        }

        var record = SelectedTimelineRow.Record;
        CorrectionService.Apply(record, CorrectionAction.None, null, null, string.Empty);
        record.ReviewerNote = string.Empty;
        SaveRecordCorrection(record);
        RefreshFromReport(SelectedEmployee?.EmployeeId, record);
        StatusMessage = "DTR correction reset to the recognized values.";
    }

    private void SaveRecordCorrection(AttendanceRecord record)
    {
        var recordKey = CorrectionKey.ForRecord(record);
        _database.SaveCorrection(CorrectionService.ToCorrectionRecord(record, _importKey));
        _database.SaveInlineCorrectionAudit(
            _importKey,
            recordKey,
            SelectedCutoffTile?.Key ?? string.Empty,
            record.EmployeeId,
            record.WorkDate,
            record.FinalTimeIn,
            record.FinalTimeOut,
            record.FinalStatus,
            record.ReviewerNote);
    }

    private void SaveRulesAndReanalyze()
    {
        try
        {
            _rules = ReadRuleFields();
            _database.SaveRules(_rules);
            if (_workbook is not null)
            {
                AnalyzeCurrentWorkbook();
            }

            StatusMessage = _workbook is null
                ? "Recognition settings saved."
                : "Recognition settings saved and DTR analysis refreshed.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool FilterEmployee(object item)
    {
        if (item is not EmployeeReviewRow row)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(ReviewEmployeeSearchText) &&
            !Contains(row.EmployeeId, ReviewEmployeeSearchText) &&
            !Contains(row.EmployeeName, ReviewEmployeeSearchText) &&
            !Contains(row.Department, ReviewEmployeeSearchText))
        {
            return false;
        }

        return SelectedEmployeeFilter switch
        {
            "Needs Checking" => row.NeedsReview,
            "Ready" => !row.NeedsReview,
            "Missing Time In" => row.MissingInRows > 0,
            "Missing Time Out" => row.MissingOutRows > 0,
            "Cutoff Overlap" => row.CarryoverRows > 0,
            "Extra Punches" => row.ExtraPunchRows > 0,
            "Night Shift" => row.NightSessions > 0,
            _ => true
        };
    }

    private static bool Contains(string value, string searchText)
    {
        return value.Contains(searchText.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void LoadRuleFields(AttendanceRules rules)
    {
        DuplicateTapMinutes = rules.DuplicateTapWindow.TotalMinutes.ToString("0.##", CultureInfo.InvariantCulture);
        MinimumWorkHours = rules.MinimumWorkDuration.TotalHours.ToString("0.##", CultureInfo.InvariantCulture);
        MaximumWorkHours = rules.MaximumWorkDuration.TotalHours.ToString("0.##", CultureInfo.InvariantCulture);
        DayStartEnd = FormatTime(rules.DayStartEnd);
        DayEndStart = FormatTime(rules.DayEndStart);
        DayEndEnd = FormatTime(rules.DayEndEnd);
        NightPairStart = FormatTime(rules.NightPairStart);
        StrongNightStart = FormatTime(rules.StrongNightStart);
        NightEndEnd = FormatTime(rules.NightEndEnd);
        EnableCarryoverBoundaryDetection = rules.EnableCarryoverBoundaryDetection;
        AutoApproveCleanNightShifts = rules.AutoApproveCleanNightShifts;
    }

    private AttendanceRules ReadRuleFields()
    {
        return new AttendanceRules
        {
            DuplicateTapWindow = TimeSpan.FromMinutes(ParsePositiveDouble(DuplicateTapMinutes, "Duplicate tap minutes")),
            MinimumWorkDuration = TimeSpan.FromHours(ParsePositiveDouble(MinimumWorkHours, "Minimum work hours")),
            MaximumWorkDuration = TimeSpan.FromHours(ParsePositiveDouble(MaximumWorkHours, "Maximum work hours")),
            DayStartEnd = ParseTime(DayStartEnd, "Day start end"),
            DayEndStart = ParseTime(DayEndStart, "Day end start"),
            DayEndEnd = ParseTime(DayEndEnd, "Day end end"),
            NightPairStart = ParseTime(NightPairStart, "Night pair start"),
            StrongNightStart = ParseTime(StrongNightStart, "Strong night start"),
            NightEndEnd = ParseTime(NightEndEnd, "Night end end"),
            EnableCarryoverBoundaryDetection = EnableCarryoverBoundaryDetection,
            AutoApproveCleanNightShifts = AutoApproveCleanNightShifts
        };
    }

    private static bool TryReadEditorDateTime(DateTime? date, string timeText, out DateTime? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(timeText))
        {
            return true;
        }

        if (date is null)
        {
            return false;
        }

        if (!TimeSpan.TryParse(timeText.Trim(), CultureInfo.InvariantCulture, out var time))
        {
            return false;
        }

        parsed = date.Value.Date.Add(time);
        return true;
    }

    private static double ParsePositiveDouble(string value, string label)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            throw new InvalidOperationException($"{label} must be a positive number.");
        }

        return parsed;
    }

    private static TimeSpan ParseTime(string value, string label)
    {
        if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidOperationException($"{label} must use HH:mm format.");
        }

        return parsed;
    }

    private static string FormatTime(TimeSpan value)
    {
        return value.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
    }

    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }
}
