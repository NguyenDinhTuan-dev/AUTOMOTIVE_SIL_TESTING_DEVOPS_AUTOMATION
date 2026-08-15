using System.Windows;
using AutVecu.Desktop.ViewModels;

namespace AutVecu.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
