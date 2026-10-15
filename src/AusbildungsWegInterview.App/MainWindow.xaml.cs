using System.Windows;
using AusbildungsWegInterview.App.ViewModels;

namespace AusbildungsWegInterview.App;

/// <summary>
/// The single floating window. Code-behind is intentionally minimal (MVVM):
/// it only constructs the view model and sets the DataContext.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(AppServices services)
    {
        InitializeComponent();
        var vm = new MainViewModel(services, Dispatcher);
        DataContext = vm;
    }
}
