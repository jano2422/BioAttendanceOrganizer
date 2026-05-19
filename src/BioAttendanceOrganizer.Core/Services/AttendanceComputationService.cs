using BioAttendanceOrganizer.Core.Analysis;
using BioAttendanceOrganizer.Core.Models;

namespace BioAttendanceOrganizer.Core.Services;

public sealed class AttendanceComputationService
{
    private readonly AttendanceAnalyzer _analyzer;

    public AttendanceComputationService(AttendanceAnalyzer? analyzer = null)
    {
        _analyzer = analyzer ?? new AttendanceAnalyzer();
    }

    public AttendanceReport Analyze(BiometricWorkbook workbook, AttendanceRules rules)
    {
        return _analyzer.Analyze(workbook, rules);
    }
}
