using Microsoft.Extensions.DependencyInjection;
using SporeModManager.Core.Services;
using SporeModManager.App.ViewModels;
using System;
using System.IO;
using System.Windows;

namespace SporeModManager.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = default!;

    protected override void OnStartup(StartupEventArgs e)
    {
        LegalNoticeService.EnsureExtracted();

        var services = new ServiceCollection();
        var baseDir = AppContext.BaseDirectory;
        services.AddSingleton(new SettingsService());
        services.AddSingleton<GamePathService>();
        services.AddSingleton(new GitHubReleaseService());
        services.AddSingleton(new ModApiService());
        services.AddSingleton(sp => new ModInstallationService(baseDir, sp.GetRequiredService<GamePathService>()));
        services.AddSingleton(sp => new SporeLauncherService(baseDir, sp.GetRequiredService<GamePathService>()));
        services.AddSingleton<ProfileService>();
        services.AddSingleton<LauncherKitBootstrapService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<ModsViewModel>();
        services.AddSingleton<InstallViewModel>();
        services.AddSingleton<ModApiViewModel>();
        services.AddSingleton<ProfilesViewModel>();
        services.AddSingleton<LaunchViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<AboutViewModel>();
        Services = services.BuildServiceProvider();

        var main = new MainWindow { DataContext = Services.GetRequiredService<MainViewModel>() };
        main.Show();
        base.OnStartup(e);
    }
}