# Repro for syncfusion/maui-toolkit#392

`System.ObjectDisposedException: Cannot access disposed object ... Object name: 'Microsoft.Maui.Graphics.Platform.PlatformGraphicsView'`
thrown from `SfDrawableViewHandler.Invalidate()` (Android), plus UI freezes when the Y axis `Minimum` / `Maximum` / `Interval` of
`SfCartesianChart` are updated quickly.

This project is the app from [dotnet/maui#38612](https://github.com/dotnet/maui/issues/38612) (`../Repro38612_last`) with
`SfCartesianChart` added, as requested in the discussion.

## Environment
- .NET 10, Microsoft.Maui.Controls 10.0.110, **Syncfusion.Maui.Toolkit 1.0.11**
- Targets `net10.0-android` and `net10.0-ios` (iOS signing settings in the csproj are only applied for iOS)
- Physical devices recommended (the MAUI #38612 crash did not reproduce on the iOS simulator)

## The chart (same as in our app)
`Controls/TemperatureChart.xaml` – `SfCartesianChart` with a `LineSeries` and a Y `NumericalAxis` named `OtherData` whose
`Minimum`, `Maximum` and `Interval` are bound to the model (`DemoControl.TemperatureMinimum/Maximum/Interval`).
Every "MQTT message" (`DemoControl.PushSample`) adds a point and then sets **Minimum → Maximum → Interval** in that order.

## Where the chart is used
1. **Naprave** (devices) tab – one chart per group inside the `CollectionView` → `TemplateHost` → `Border` (same structure as the
   real app). Charts are updated by the MQTT simulator (`MQTT bad` / `MQTT good` buttons, `MQTT good` starts automatically) and by
   the Chaos engine (flood, Rows replace, Right flip, IsVisible toggle, ScrollTo, GC/CPU pressure).
2. **Graf (#392)** tab – one chart, no CollectionView, isolated scenarios (below).
3. **Chaos** tab – switches: *Charts in list*, *MQTT updates charts*, *100x Min/Max/Interval per update*, *Shell page switching
   during chaos* (navigates away from the page with charts and back while updates continue), min. interval between chart updates.

## Scenarios on the "Graf (#392)" tab
| Button | What it does |
|---|---|
| MQTT good | Background loop, every 20 ms `BeginInvokeOnMainThread(PushSample)`. Keeps running when you leave the page. |
| MQTT bad (10 s) | `while` loop on the main thread for 10 s (no `Task.Delay`), `PushSample` every iteration, chart replaced every 500 iterations. Scheduled chart updates run after the main thread is released, also for the removed charts. |
| Os 100x | 100 synchronous Minimum/Maximum/Interval updates (“fast update, 100 iterations”). |
| Ponovno ustvarjaj graf | Every 16 ms: `PushSample()` (schedules `UpdateArea`) and then replaces the chart in the same main-loop turn; old charts stay bound to the model; GC every 30 charts. |
| **[A]** Dispose native + update | *Synthetic.* Disposes the `PlatformGraphicsView`s of the current chart while their handlers stay connected, then pushes one sample. Produces the exact stack trace from #392 (see below). |
| **[B]** 1e16 / Interval 1 | *Synthetic.* Hangs the app forever (see below). |
| **[C]** 0..1e9 / Interval 5 | *Synthetic.* Hangs the app / OOM (see below). |

The top label shows which `CoreScheduler` the chart uses on the device.

## Findings from reading the toolkit source (1.0.11 / main)

### 1. Scheduled update is not cancelled and the platform view is not checked
`AreaBase.ScheduleUpdateArea()` → `CoreScheduler.ScheduleCallback(UpdateArea)`. With `CoreMainScheduler` the callback is posted via
`Device.BeginInvokeOnMainThread` and always runs later – there is no cancellation when the chart is removed, its handler is
disconnected, or the page is destroyed. `SfDrawableViewHandler.Invalidate()` (Android) only does `PlatformView?.Invalidate()`; if the
peer is already disposed this throws. A guard (e.g. checking `Handle != IntPtr.Zero` on the platform view, or skipping
`UpdateArea` when `Handler == null`) would turn the crash into a no-op.

Scenario **[A]** demonstrates exactly this path:
```
CoreScheduler.InvokeCallback → AreaBase.UpdateArea → CartesianChartArea.UpdateAreaCore → UpdateVisibleSeries
→ ChartPlotArea.UpdateVisibleSeries → SeriesView.UpdateSeries → SfDrawableView.InvalidateDrawable
→ SfDrawableViewHandler.Invalidate → Android.Views.View.Invalidate → ObjectDisposedException
```
(What disposes the native view in production is not yet known – the real-world scenarios above are meant to find it.)

### 2. Why the Android stack trace goes through `Dispatcher` (Main scheduler)
`AreaBase` chooses `CoreSchedulerType.Frame` only when power save is off **and** animations are on. Animations are detected with
```csharp
Settings.Global.GetFloat(resolver, Settings.Global.AnimatorDurationScale, 0.0f)
```
The default `0.0f` is used when the setting has never been written, so on many devices the chart treats animations as *disabled*
and uses the Main scheduler. The default should probably be `1.0f`. To force the Main scheduler: enable **Battery saver** or set
*Developer options → Animator duration scale → Animation off*.

### 3. Possible freezes in `NumericalAxis.GenerateVisibleLabels`
```csharp
while (position <= VisibleRange.End && !double.IsInfinity(position)) { ... actualLabels.Add(...); position += interval; }
```
- **[B]** When `interval` is smaller than the spacing between doubles at `position` (e.g. `Minimum = 1e16`, `Interval = 1`),
  `position += interval` does not change `position` → infinite loop on the UI thread, `actualLabels` grows until OOM.
- **[C]** No upper bound on the number of labels: `Minimum = 0`, `Maximum = 1e9`, `Interval = 5` → 2·10⁸ labels on the UI thread.

A transient bad combination (e.g. from bound values that arrive in separate messages, or a sensor error value) is enough.

## Steps
### Android (crash from #392)
1. Optional: enable Battery saver (Main scheduler, see 2.). The top label of the *Graf (#392)* tab confirms the scheduler.
2. **Naprave** tab: `MQTT good` runs automatically. Press `Chaos START` (Chaos tab: enable *Shell page switching*), or `MQTT bad`.
3. **Graf (#392)** tab: start `MQTT good` and `Ponovno ustvarjaj graf`, press `GC` a few times, switch tabs, put the app in
   background and back (also try *Developer options → Don't keep activities*).
4. `[A]` reproduces the stack trace immediately.

### iOS
Same steps; `Chaos START` + `MQTT bad` on the *Naprave* tab is the combination from dotnet/maui#38612.

## Crash log
`CrashLogger` writes unhandled exceptions (`AppDomain`, `TaskScheduler`, `AndroidEnvironment.UnhandledExceptionRaiser`,
iOS marshal exceptions), the active Chaos options and the chart scheduler to `crash.log`. After a crash relaunch and open **Log**.
