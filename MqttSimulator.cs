using System.Diagnostics;
using Microsoft.Maui.Dispatching;
using Repro38612.ViewModels;

namespace Repro38612;

public enum MqttMode
{
    Off,

    // SLABO: zanka teče na glavni niti (brez Task.Run, brez Task.Delay), handlerji delajo UI delo kar tam
    BadMainThreadLoop,

    // DOBRO: zanka v Task.Run z Task.Delay, handlerji samo BeginInvokeOnMainThread / združevanje posodobitev
    GoodBackgroundLoop
}

// Enaka oblika kot MQTTnet: dogodki so Func<..., Task> in se awaitajo
public sealed class FakeMqttClient
{
    public event Func<Task>? ConnectedAsync;
    public event Func<Task>? DisconnectedAsync;
    public event Func<string, Task>? MessageReceivedAsync;

    public bool IsConnected { get; private set; }

    public async Task<bool> TryPingAsync(CancellationToken ct)
    {
        // Občasna izguba povezave, da zanka stalno dela reconnect
        if (IsConnected && Random.Shared.Next(20) == 0)
        {
            IsConnected = false;
            await RaiseAsync(DisconnectedAsync);
        }

        return IsConnected;
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        IsConnected = true;
        await RaiseAsync(ConnectedAsync);
    }

    public async Task ReceiveAsync(string topic)
    {
        if (MessageReceivedAsync is { } handlers)
        {
            foreach (Delegate d in handlers.GetInvocationList())
            {
                await ((Func<string, Task>)d)(topic);
            }
        }
    }

    private static async Task RaiseAsync(Func<Task>? ev)
    {
        if (ev is null) return;

        foreach (Delegate d in ev.GetInvocationList())
        {
            await ((Func<Task>)d)();
        }
    }
}

public sealed class MqttSimulator
{
    private readonly FakeMqttClient client = new();
    private readonly MainViewModel vm;
    private readonly ChaosEngine chaos;
    private readonly IDispatcher dispatcher;

    private CancellationTokenSource? cts;
    private IDispatcherTimer? timer;
    private volatile MqttMode mode = MqttMode.Off;
    private int pendingMessages;

    public MqttMode Mode => mode;

    public MqttSimulator(MainViewModel vm, ChaosEngine chaos, IDispatcher dispatcher)
    {
        this.vm = vm;
        this.chaos = chaos;
        this.dispatcher = dispatcher;

        client.ConnectedAsync += OnConnectedAsync;
        client.DisconnectedAsync += OnDisconnectedAsync;
        client.MessageReceivedAsync += OnMessageReceivedAsync;
    }

    public async Task StartAsync(MqttMode newMode)
    {
        Stop();
        if (newMode == MqttMode.Off) return;

        cts = new CancellationTokenSource();
        CancellationToken ct = cts.Token;
        mode = newMode;

        if (newMode == MqttMode.BadMainThreadLoop)
        {
            // Klic NA glavni niti, brez Task.Run. Ker se vsi awaiti zaključijo sinhrono,
            // se zanka nikoli ne vrne v run loop. 20 s, nato se vrne, da se app ne zaklene za vedno.
            await BadLoopAsync(ct, TimeSpan.FromSeconds(20));
            mode = MqttMode.Off;
            return;
        }

        // Dobra različica: UI posodobitve združimo in jih uveljavimo na 200 ms
        timer = dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(200);
        timer.Tick += (_, _) =>
        {
            if (Interlocked.Exchange(ref pendingMessages, 0) > 0)
            {
                vm.TouchRandomControls();
            }
        };
        timer.Start();

        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (!await client.TryPingAsync(ct))
                    {
                        await client.ConnectAsync(ct);
                    }

                    for (int i = 0; i < 30; i++)
                    {
                        await client.ReceiveAsync("topic/x");
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { /* log */ }

                try { await Task.Delay(TimeSpan.FromMilliseconds(50), ct); }
                catch (OperationCanceledException) { break; }
            }
        }, ct);
    }

    public void Stop()
    {
        cts?.Cancel();
        cts = null;

        timer?.Stop();
        timer = null;

        mode = MqttMode.Off;
    }

    // Kot "while (true)" reconnect zanka na glavni niti: samo preverja in nikoli ne čaka
    private async Task BadLoopAsync(CancellationToken ct, TimeSpan duration)
    {
        Stopwatch sw = Stopwatch.StartNew();

        while (sw.Elapsed < duration)
        {
            try
            {
                if (!await client.TryPingAsync(ct))
                {
                    await client.ConnectAsync(ct);
                }

                await client.ReceiveAsync("topic/x");
            }
            catch
            {
                // log
            }

            // NI Task.Delay: zanka se nikoli ne vrne v run loop
        }
    }

    private Task OnConnectedAsync()
    {
        if (mode == MqttMode.BadMainThreadLoop)
        {
            // SLABO: delo kar v handlerju, ki teče na glavni niti sredi zanke
            vm.ConnectedToMqtt = true;
            chaos.ApplyConnectedWork();
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(() => vm.ConnectedToMqtt = true);
        }

        return Task.CompletedTask;
    }

    private Task OnDisconnectedAsync()
    {
        if (mode == MqttMode.BadMainThreadLoop)
        {
            vm.ConnectedToMqtt = false;
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(() => vm.ConnectedToMqtt = false);
        }

        return Task.CompletedTask;
    }

    private Task OnMessageReceivedAsync(string topic)
    {
        if (mode == MqttMode.BadMainThreadLoop)
        {
            // SLABO: vsako sporočilo posodobi UI takoj
            vm.TouchRandomControls();
        }
        else
        {
            // DOBRO: samo štejemo, UI posodobi timer
            Interlocked.Increment(ref pendingMessages);
        }

        return Task.CompletedTask;
    }
}
