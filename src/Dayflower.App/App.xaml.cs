// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Dayflower;

public partial class App : Application
{
    private AppServices? _services;
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
            _services?.Loggers.CreateLogger<App>().LogCritical(e.Exception, "Unhandled exception");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _services = AppServices.Create(DispatcherQueue.GetForCurrentThread());
        _window = new MainWindow(_services);
        _window.Closed += (_, _) => _services.Dispose();
        _window.Activate();
        _services.Sync.Start();
    }
}
