using System.Windows;
using System.Windows.Controls;
using SporeModManager.App.ViewModels;

namespace SporeModManager.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (Tabs.SelectedIndex == 0) vm.Mods.Reload();
        else if (Tabs.SelectedIndex == 1) vm.Profiles.RefreshLocalTags();
        else if (Tabs.SelectedIndex == 2) vm.ModApi.RefreshLocalMarks();
    }
}