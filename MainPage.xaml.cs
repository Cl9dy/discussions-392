using System.Net.Sockets;
using Repro38612.Models;
using Repro38612.ViewModels;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Repro38612;

public partial class MainPage : ContentPage
{
    // true = chaos (GC, CPU, poplava) se sam zažene 3 s po zagonu
    private const bool AutoStartChaos = false;

    // Kateri MQTT način se sam zažene 5 s po zagonu. BadMainThreadLoop = zanka na glavni niti 20 s.
    // Za primerjavo nastavite GoodBackgroundLoop ali Off.
    private const MqttMode AutoMqttMode = MqttMode.GoodBackgroundLoop;

    // Nedosegljiv naslov kamere (http kamera ni na voljo, kot v pravi aplikaciji)
    private const string CameraHost = "192.168.1.1";

    private readonly Dictionary<DemoControl, WeakReference<SKCanvasView>> canvases = [];
    private readonly MainViewModel viewModel = new();
    private ChaosEngine? chaos;
    private MqttSimulator? mqtt;

    public MainPage()
    {
        InitializeComponent();
        BindingContext = viewModel;
        Loaded += OnPageLoaded;
    }

    private async void OnPageLoaded(object? sender, EventArgs e)
    {
        chaos = new ChaosEngine(viewModel, listControlsItems, Dispatcher);
        mqtt = new MqttSimulator(viewModel, chaos, Dispatcher);

        if (AutoStartChaos)
        {
            await Task.Delay(3000);
            StartChaos();
        }

        if (AutoMqttMode != MqttMode.Off)
        {
            await Task.Delay(2000);
            await mqtt.StartAsync(AutoMqttMode);
        }
    }

    private void OnChaosClicked(object? sender, EventArgs e)
    {
        if (chaos is null) return;

        if (chaos.IsRunning)
        {
            chaos.Stop();
            viewModel.ChaosText = "Chaos START";
        }
        else
        {
            StartChaos();
        }
    }

    private void StartChaos()
    {
        chaos?.Start();
        viewModel.ChaosText = "Chaos STOP";
    }

    // Glavna nit bo 20 s zasedena (zanka brez Task.Run in brez Task.Delay)
    private async void OnMqttBadClicked(object? sender, EventArgs e)
    {
        if (mqtt is not null) await mqtt.StartAsync(MqttMode.BadMainThreadLoop);
    }

    private async void OnMqttGoodClicked(object? sender, EventArgs e)
    {
        if (mqtt is not null) await mqtt.StartAsync(MqttMode.GoodBackgroundLoop);
    }

    private void OnMqttStopClicked(object? sender, EventArgs e) => mqtt?.Stop();

    private void OnCameraPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        e.Surface.Canvas.Clear(SKColors.LightSteelBlue);

        if (sender is SKCanvasView canvas && canvas.BindingContext is DemoControl control)
        {
            canvases[control] = new WeakReference<SKCanvasView>(canvas);
        }
    }

    // Vsak scroll event sproži poizvedbe za vse vidne kamere, brez omejitve in brez preklica
    private void ListControlsItems_Scrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        if (!ChaosOptions.Current.CameraPolls) return;

        int first = Math.Max(e.FirstVisibleItemIndex, 0);
        int last = Math.Min(e.LastVisibleItemIndex, viewModel.Devices.Rows.Count - 1);

        for (int i = first; i <= last; i++)
        {
            DeviceRow row = viewModel.Devices.Rows[i];
            if (row.Left is { Count: 1 } && row.Left[0] is { IsCamera: true } control
                && canvases.TryGetValue(control, out WeakReference<SKCanvasView>? weak)
                && weak.TryGetTarget(out SKCanvasView? canvas))
            {
                _ = PollCameraAsync(canvas);
            }
        }
    }

    private static async Task PollCameraAsync(SKCanvasView canvas)
    {
        try
        {
            // Kot v pravi kodi: TCP preverjanje brez timeouta, nato HTTP (nikoli se ne dispose-a)
            TcpClient tcpClient = new();
            await tcpClient.ConnectAsync(CameraHost, 80);

            HttpClient httpClient = new() { BaseAddress = new Uri($"http://{CameraHost}/") };
            HttpResponseMessage response = await httpClient.GetAsync("");
            await response.Content.ReadAsByteArrayAsync();
        }
        catch
        {
            // Kamera ni na voljo
        }
        finally
        {
            // Simulacija uspešne poti iz prave kode; celica je lahko med čakanjem že reciklirana
            MainThread.BeginInvokeOnMainThread(() =>
            {
                canvas.InvalidateSurface();
                canvas.InvalidateMeasure();
            });
        }
    }
}
