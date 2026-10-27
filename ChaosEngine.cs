using Microsoft.Maui.Dispatching;
using Repro38612.Models;
using Repro38612.ViewModels;

namespace Repro38612;

// Namenoma najslabši možen scenarij; vsak del je izklopljiv prek ChaosOptions
public sealed class ChaosEngine
{
    private readonly MainViewModel vm;
    private readonly CollectionView list;
    private readonly IDispatcher dispatcher;

    private CancellationTokenSource? cts;
    private IDispatcherTimer? timer;
    private int tick;

    public bool IsRunning => cts is not null;

    public ChaosEngine(MainViewModel vm, CollectionView list, IDispatcher dispatcher)
    {
        this.vm = vm;
        this.list = list;
        this.dispatcher = dispatcher;
    }

    public void Start()
    {
        if (cts is not null) return;

        ChaosOptions o = ChaosOptions.Current;
        CrashLogger.Write("Chaos START", o.ToString());

        cts = new CancellationTokenSource();
        CancellationTokenSource thisRun = cts;
        CancellationToken ct = cts.Token;

        // 1) GC kladivo
        if (o.GcHammer)
        {
            Task.Factory.StartNew(() =>
            {
                while (!ct.IsCancellationRequested)
                {
                    List<byte[]> junk = new();
                    for (int i = 0; i < 2000; i++) junk.Add(new byte[512]);
                    junk.Clear();

                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                    GC.WaitForPendingFinalizers();
                    Thread.Sleep(2);
                }
            }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        // 2) "MQTT" poplava na UI niti
        if (o.UiFlood)
        {
            Task.Factory.StartNew(() =>
            {
                while (!ct.IsCancellationRequested)
                {
                    for (int i = 0; i < 30; i++)
                    {
                        MainThread.BeginInvokeOnMainThread(vm.TouchRandomControls);
                    }
                    Thread.Sleep(1);
                }
            }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        // 3) CPU burn na vseh razen enem jedru
        if (o.CpuBurn)
        {
            int burners = Math.Max(1, Environment.ProcessorCount - 1);
            for (int b = 0; b < burners; b++)
            {
                Task.Factory.StartNew(() =>
                {
                    double x = 1;
                    while (!ct.IsCancellationRequested)
                    {
                        x = Math.Sqrt(x + 1) * Math.Sin(x);
                    }
                }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
        }

        // 4) UI kaos na vsakih 10 ms (posamezna stikala se berejo v DoTick)
        timer = dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(10);
        timer.Tick += OnTick;
        timer.Start();

        // Samodejni konec
        if (o.DurationSeconds > 0)
        {
            thisRun.CancelAfter(TimeSpan.FromSeconds(o.DurationSeconds));
            ct.Register(() => dispatcher.Dispatch(() =>
            {
                if (cts == thisRun)
                {
                    Stop();
                }
            }));
        }
    }

    public void Stop()
    {
        timer?.Stop();
        if (timer is not null) timer.Tick -= OnTick;
        timer = null;

        bool wasRunning = cts is not null;
        cts?.Cancel();
        cts = null;

        vm.IsListVisible = true;
        vm.ChaosText = "Chaos START";

        if (wasRunning)
        {
            CrashLogger.Write("Chaos STOP");
        }
    }

    // Delo, ki ga v pravi aplikaciji opravi handler onConnected / sporočil:
    // posodobitve ViewModela, nadomestitev Rows, preklop Right, IsVisible, ScrollTo + prisiljen layout
    public void ApplyConnectedWork()
    {
        DoTick();

#if IOS
        // Run loop ne teče, zato layout prisilimo sami (podobno kot UpdateVisibility -> LayoutIfNeeded)
        if (list.Handler?.PlatformView is UIKit.UIView view)
        {
            view.SetNeedsLayout();
            view.LayoutIfNeeded();
            view.Window?.RootViewController?.View?.LayoutIfNeeded();
        }
#endif
    }

    private void OnTick(object? sender, EventArgs e) => DoTick();

    private void DoTick()
    {
        ChaosOptions o = ChaosOptions.Current;
        tick++;

        // Right: null <-> vrednost
        if (o.FlipRight)
        {
            foreach (DeviceRow row in vm.Devices.Rows)
            {
                if (Random.Shared.Next(3) == 0)
                {
                    row.FlipRight();
                }
            }
        }

        // Rows nadomestimo z novo kolekcijo
        if (o.ReplaceRows && tick % 50 == 0)
        {
            vm.Devices.BuildRows(DemoData.Create());
        }

        // Seznam skrijemo in kmalu spet pokažemo
        if (o.ToggleVisibility)
        {
            if (tick % 70 == 0) vm.IsListVisible = false;
            if (tick % 70 == 3) vm.IsListVisible = true;
        }

        // Shell: stran z grafi zapustimo in se vrnemo (Android: ShellContentFragment.Destroy, page.Handler = null),
        // medtem pa MQTT še naprej posodablja grafe na odstranjeni strani
        if (o.ShellNavigate && Shell.Current is { } shell)
        {
            if (tick % 150 == 0) _ = shell.GoToAsync("//log", animate: false);
            if (tick % 150 == 40) _ = shell.GoToAsync("//devices", animate: false);
        }

        // Nenehen scroll (reciklaža celic; sproži tudi Scrolled in klice kamer)
        if (o.AutoScroll && tick % 2 == 0 && vm.Devices.Rows.Count > 0 && vm.IsListVisible)
        {
            int index = Random.Shared.Next(vm.Devices.Rows.Count);
            list.ScrollTo(index, position: ScrollToPosition.Start, animate: false);
        }
    }
}
