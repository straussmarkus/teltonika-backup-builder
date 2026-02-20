using System.Windows;
using TeltonikaBackupBuilder.App.ViewModels;

namespace TeltonikaBackupBuilder.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
