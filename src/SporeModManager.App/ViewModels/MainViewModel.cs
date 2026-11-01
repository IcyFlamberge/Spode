using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace SporeModManager.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public ModsViewModel Mods { get; }
    public InstallViewModel Install { get; }
    public ModApiViewModel ModApi { get; }
    public ProfilesViewModel Profiles { get; }
    public LaunchViewModel Launch { get; }
    public SettingsViewModel Settings { get; }
    public AboutViewModel About { get; }

    public MainViewModel(IServiceProvider sp)
    {
        Mods = sp.GetRequiredService<ModsViewModel>();
        Install = sp.GetRequiredService<InstallViewModel>();
        ModApi = sp.GetRequiredService<ModApiViewModel>();
        Profiles = sp.GetRequiredService<ProfilesViewModel>();
        Launch = sp.GetRequiredService<LaunchViewModel>();
        Settings = sp.GetRequiredService<SettingsViewModel>();
        About = sp.GetRequiredService<AboutViewModel>();
    }
}