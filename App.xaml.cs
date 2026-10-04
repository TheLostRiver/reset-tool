using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
using System.Text.Json;
using Frostbound.Core;

namespace Frostbound;

public partial class App : Application
{
    public static bool IsRendering { get; private set; }
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool connectionReport = e.Args.Length >= 2 && e.Args[0] == "--connection-report";
        IsRendering = connectionReport || e.Args.Length >= 2 && e.Args[0] == "--render-preview";
        if (!IsRendering) {
            instance = new Mutex(true, @"Local\Frostbound.MHWI.Reset", out bool first);
            if (!first) { MessageBox.Show("霜序已经运行，可从任务栏通知区域打开。\nFrostbound is already running in the notification area.", "Frostbound"); Shutdown(); return; }
        }
        DispatcherUnhandledException += (_, args) => {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Frostbound");
            try { Directory.CreateDirectory(directory); File.AppendAllText(Path.Combine(directory, "error.log"), $"{DateTime.Now:O}\n{args.Exception}\n"); } catch (IOException) { }
            if (IsRendering) Shutdown(1);
            else MessageBox.Show(args.Exception.Message, "Frostbound", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        if (connectionReport) { Dispatcher.BeginInvoke(async () => await SaveConnectionReport(e.Args[1])); return; }
        MainWindow = new MainWindow();
        if (IsRendering) Dispatcher.BeginInvoke(async () => await RenderPreview((MainWindow)MainWindow, e.Args[1]));
        else MainWindow.Show();
    }
    private async Task SaveConnectionReport(string path)
    {
        using var engine = new GameEngine(); await engine.PollAsync(true);
        string destination = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, JsonSerializer.Serialize(engine.Snapshot, SettingsStore.Json));
        await engine.StopAsync(); Shutdown();
    }
    private async Task RenderPreview(MainWindow window, string path)
    {
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(1210, 850)); root.Arrange(new Rect(0, 0, 1210, 850)); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1210, 850, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string destination = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using (var file = File.Create(destination)) encoder.Save(file);
        await window.CloseRender(); Shutdown();
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
