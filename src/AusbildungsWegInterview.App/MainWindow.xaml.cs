using System.Windows;
using System.Windows.Controls;
using AusbildungsWegInterview.App.ViewModels;

namespace AusbildungsWegInterview.App;

/// <summary>
/// The single window. Code-behind stays deliberately minimal (MVVM): it only
/// constructs the view model, sets the DataContext, and opens the "Add document"
/// menu (a pure view concern — every menu entry runs a real view-model command).
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(AppServices services)
    {
        InitializeComponent();
        var vm = new MainViewModel(services, Dispatcher);
        DataContext = vm;
    }

    /// <summary>
    /// Opens the CV / job-description / notes menu of the Document Context panel.
    /// The entries are bound to the same commands used on the Documents page.
    /// </summary>
    private void AddDocument_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is not { } menu)
            return;

        menu.PlacementTarget = button;
        menu.DataContext = DataContext;
        menu.IsOpen = true;
    }
}
