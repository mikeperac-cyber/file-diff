using System.Windows;
using FileDiff.App.ViewModels;

namespace FileDiff.App;

/// <summary>Interaction logic for MainWindow.xaml — only the view model is wired up here.</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}