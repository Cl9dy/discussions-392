using System.Diagnostics;
using Microsoft.Maui.Dispatching;
using Repro38612.Controls;
using Repro38612.Models;

namespace Repro38612;

// Izolirani scenariji za syncfusion/maui-toolkit#392 (en sam SfCartesianChart, brez CollectionView)
public partial class ChartPage : ContentPage
{
    private readonly DemoControl model = new() { Name = "Temperatura", IsChart = true, IsFullWidth = true };

    private CancellationTokenSource? mqttGoodCts;
    private IDispatcherTimer? recreateTimer;
    private int recreated;

    public ChartPage()
    {
        InitializeComponent();
        model.SeedChart();
        chartHost.Content = CreateChart();

        string info = DescribeScheduler();
        schedulerLabel.Text = info;
        CrashLogger.Write("ChartPage", info);
    }

    private TemperatureChart CreateChart() => new() { BindingContext = model };

    private void SetStatus(string text)
    {
        statusLabel.Text = $"{DateTime.Now:HH:mm:ss}  {text}";
        CrashLogger.Write("ChartPage: " + text);
    }

    // --- 1) MQTT good: zanka v ozadju, vsako sporočilo -> BeginInvokeOnMainThread -> nova točka + Min/Max/Interval
    //     Teče NAPREJ tudi, ko zapustite stran (graf ni več prikazan, posodobitve pa še prihajajo).
    private void OnMqttGood(object? sender, EventArgs e)
    {
        if (mqttGoodCts is not null)
        {
            mqttGoodCts.Cancel();
            mqttGoodCts = null;
            mqttGoodButton.Text = "MQTT good: START";
            SetStatus("MQTT good STOP");
            return;
        }

        mqttGoodCts = new CancellationTokenSource();
        CancellationToken ct = mqttGoodCts.Token;
        mqttGoodButton.Text = "MQTT good: STOP";
        SetStatus("MQTT good START (20 ms)");

        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    model.PushSample();
                    if (ChaosOptions.Current.ChartAxisBurst) model.AxisBurst(100);
                });

                try { await Task.Delay(20, ct); }
                catch (OperationCanceledException) { break; }
            }
        }, ct);
    }

    // --- 2) MQTT bad: "while(true)" na glavni niti 10 s, brez Task.Delay.
    //     Posodobitve grafa se samo planirajo (CoreScheduler), med tem pa graf vsakih 500 iteracij
    //     zamenjamo z novim. Ko se glavna nit sprosti, se izvedejo planirani klici tudi za odstranjene grafe.
    private void OnMqttBad(object? sender, EventArgs e)
    {
        SetStatus("MQTT bad START (glavna nit zasedena 10 s)");

        Stopwatch sw = Stopwatch.StartNew();
        int iterations = 0;
        int swaps = 0;

        while (sw.Elapsed < TimeSpan.FromSeconds(10))
        {
            model.PushSample();
            iterations++;

            if (iterations % 500 == 0)
            {
                chartHost.Content = CreateChart();
                swaps++;
            }
        }

        SetStatus($"MQTT bad END: {iterations} posodobitev, {swaps} zamenjav grafa");
    }

    // --- 3) "Do fast update (100 iterations) on TemperatureMinimum" iz diskusije
    private void OnAxisBurst(object? sender, EventArgs e)
    {
        Stopwatch sw = Stopwatch.StartNew();
        model.AxisBurst(100);
        SetStatus($"Os 100x: {sw.ElapsedMilliseconds} ms (sinhroni del)");
    }

    // --- 4) Graf odstranimo iz drevesa takoj po posodobitvi podatkov (enako kot TemplateHost / reciklaža celic),
    //     stari grafi ostanejo vezani na isti model in prejemajo nadaljnje posodobitve.
    private void OnRecreate(object? sender, EventArgs e)
    {
        if (recreateTimer is not null)
        {
            recreateTimer.Stop();
            recreateTimer = null;
            recreateButton.Text = "Ponovno ustvarjaj graf: START";
            SetStatus($"Ponovno ustvarjanje STOP po {recreated} grafih");
            return;
        }

        recreated = 0;
        recreateTimer = Dispatcher.CreateTimer();
        recreateTimer.Interval = TimeSpan.FromMilliseconds(16);
        recreateTimer.Tick += (_, _) =>
        {
            model.PushSample();                 // planira UpdateArea na trenutnem grafu
            chartHost.Content = CreateChart();  // ... ki ga v istem obratu zanke odstranimo
            recreated++;

            if (recreated % 30 == 0)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                statusLabel.Text = $"Ustvarjenih grafov: {recreated}";
            }
        };
        recreateTimer.Start();
        recreateButton.Text = "Ponovno ustvarjaj graf: STOP";
        SetStatus("Ponovno ustvarjanje START (16 ms)");
    }

    private void OnGc(object? sender, EventArgs e)
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        SetStatus("GC.Collect");
    }

    // --- [A] SINTETIČNO: nativni PlatformGraphicsView zavržemo, handler pa ostane povezan
    //     (to stanje imamo v produkciji; kdo ga zavrže, še ni znano). Naslednja posodobitev podatkov
    //     da natanko sled sklada iz #392: CoreScheduler.InvokeCallback -> ... -> SfDrawableViewHandler.Invalidate()
    //     -> ObjectDisposedException. Dokazuje, da Invalidate() nima zaščite pred zavrženim pogledom.
    private void OnSyntheticDispose(object? sender, EventArgs e)
    {
        if (chartHost.Content is not View chart) return;

        int disposed = 0;
        foreach (IView view in Descendants(chart))
        {
            if (view.Handler?.PlatformView is IDisposable platformView
                && platformView.GetType().Name == "PlatformGraphicsView")
            {
                platformView.Dispose();
                disposed++;
            }
        }

        SetStatus($"[A] Zavrženih PlatformGraphicsView: {disposed}. Sledi posodobitev podatkov ...");
        model.PushSample();
    }

    // SfCartesianChart (ChartBase) svojo vsebino izpostavi samo prek IContentView.PresentedContent,
    // ne prek logičnega drevesa, zato hodimo po IContentView / ILayout / IVisualTreeElement.
    private static IEnumerable<IView> Descendants(IView root)
    {
        HashSet<IView> seen = [];
        Stack<IView> stack = new();
        stack.Push(root);

        while (stack.Count > 0)
        {
            IView view = stack.Pop();
            if (!seen.Add(view)) continue;
            yield return view;

            if (view is IContentView { PresentedContent: { } content }) stack.Push(content);
            if (view is Microsoft.Maui.ILayout layout)
            {
                foreach (IView child in layout) stack.Push(child);
            }
            if (view is IVisualTreeElement tree)
            {
                foreach (IVisualTreeElement child in tree.GetVisualChildren())
                {
                    if (child is IView childView) stack.Push(childView);
                }
            }
        }
    }

    // --- [B] SINTETIČNO: NumericalAxis.GenerateVisibleLabels: "position += interval" se pri 1e16 ne spremeni
    //     (razmik med double vrednostmi je 2), zanka "while (position <= VisibleRange.End)" se nikoli ne konča.
    private async void OnPrecisionFreeze(object? sender, EventArgs e)
    {
        if (!await DisplayAlertAsync("[B]", "Aplikacija bo zamrznila (neskončna zanka). Nadaljujem?", "Da", "Ne")) return;

        StopAll();
        SetStatus("[B] Minimum=1e16, Maximum=1e16+100, Interval=1");
        model.TemperatureInterval = 1;
        model.TemperatureMinimum = 1e16;
        model.TemperatureMaximum = 1e16 + 100;
    }

    // --- [C] SINTETIČNO: ni omejitve števila oznak -> 2e8 ChartAxisLabel objektov na glavni niti
    private async void OnHugeRange(object? sender, EventArgs e)
    {
        if (!await DisplayAlertAsync("[C]", "Aplikacija bo zamrznila (2e8 oznak osi). Nadaljujem?", "Da", "Ne")) return;

        StopAll();
        SetStatus("[C] Minimum=0, Maximum=1e9, Interval=5");
        model.TemperatureMinimum = 0;
        model.TemperatureMaximum = 1e9;
        model.TemperatureInterval = 5;
    }

    private void StopAll()
    {
        mqttGoodCts?.Cancel();
        mqttGoodCts = null;
        mqttGoodButton.Text = "MQTT good: START";

        recreateTimer?.Stop();
        recreateTimer = null;
        recreateButton.Text = "Ponovno ustvarjaj graf: START";
    }

    // Enaka logika kot AreaBase konstruktor v Syncfusion.Maui.Toolkit (Core/Legend/AreaBase.cs):
    // Frame scheduler (animacije) ali Main scheduler (Device.BeginInvokeOnMainThread).
    // Sled sklada v #392 gre skozi Dispatcher.Android -> torej je bil aktiven Main scheduler.
    private static string DescribeScheduler()
    {
#if ANDROID
        var context = Microsoft.Maui.ApplicationModel.Platform.AppContext;

        // Opomba: Syncfusion uporabi privzeto 0.0f, ko nastavitev ne obstaja -> "animacije izklopljene"
        float scale = Android.Provider.Settings.Global.GetFloat(
            context.ContentResolver, Android.Provider.Settings.Global.AnimatorDurationScale, 0.0f);
        var powerManager = context.GetSystemService(Android.Content.Context.PowerService) as Android.OS.PowerManager;
        bool powerSave = powerManager?.IsPowerSaveMode ?? false;
        bool frame = !powerSave && scale != 0.0f;

        return $"Android: animator_duration_scale={scale}, power save={powerSave} -> CoreScheduler: " +
               (frame ? "Frame (animacija)" : "Main (Dispatcher, enako kot sled v #392)");
#else
        return $"{DeviceInfo.Platform}: CoreScheduler: Frame (animacija)";
#endif
    }
}
