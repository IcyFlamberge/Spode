using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SporeModManager.Core.Services;
using System;
using System.Threading.Tasks;

namespace SporeModManager.App.ViewModels;

public partial class LaunchViewModel : ObservableObject
{
    private readonly SporeLauncherService _launcher;
    private readonly ProfileService _profiles;
    private readonly LauncherKitBootstrapService _bootstrap;

    [ObservableProperty] private string status = "Ready";
    [ObservableProperty] private bool isLaunching;

    public LaunchViewModel(SporeLauncherService launcher, ProfileService profiles, LauncherKitBootstrapService bootstrap)
    {
        _launcher = launcher;
        _profiles = profiles;
        _bootstrap = bootstrap;
    }

    [RelayCommand]
    private async Task LaunchAsync()
    {
        if (IsLaunching) return;
        IsLaunching = true;
        Status = "Preparing active profile...";
        try
        {
            var progress = new Progress<string>(s => Status = s);
            await _bootstrap.EnsureInjectorAsync(progress);
            await _profiles.ApplyActiveProfileModApiAsync(progress);
            await Task.Run(() => _launcher.Launch(progress));
            Status = "Spore launched";
        }
        catch (Exception ex) { Status = "Error: " + ex.Message; }
        finally { IsLaunching = false; }
    }
}