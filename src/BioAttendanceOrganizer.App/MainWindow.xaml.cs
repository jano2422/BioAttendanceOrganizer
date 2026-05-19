using System.Windows;
using BioAttendanceOrganizer.App.ViewModels;

namespace BioAttendanceOrganizer.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
