using BioAttendanceOrganizer.Core.Models;

namespace BioAttendanceOrganizer.App.ViewModels;

public sealed class EmployeeOptionViewModel
{
    public EmployeeOptionViewModel(EmployeeInfo employee)
    {
        Employee = employee;
    }

    public EmployeeInfo Employee { get; }
    public string EmployeeId => Employee.Id;
    public string EmployeeName => Employee.Name;
    public string Label => $"{Employee.Id} - {Employee.Name}";
}
