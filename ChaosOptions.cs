using CommunityToolkit.Mvvm.ComponentModel;

namespace Repro38612;

// Stikala za posamezne dele kaosa (bisekcija brez ponovnega prevajanja)
public partial class ChaosOptions : ObservableObject
{
    public static ChaosOptions Current { get; } = new();

    [ObservableProperty] private bool gcHammer = true;          // GC.Collect v zanki
    [ObservableProperty] private bool cpuBurn = true;           // zasedena jedra
    [ObservableProperty] private bool uiFlood = true;           // poplava BeginInvokeOnMainThread
    [ObservableProperty] private bool flipRight = true;         // Right: null <-> vrednost
    [ObservableProperty] private bool replaceRows = true;       // Rows = nova kolekcija
    [ObservableProperty] private bool toggleVisibility = true;  // IsVisible seznama
    [ObservableProperty] private bool autoScroll = true;        // ScrollTo na naključno vrstico
    [ObservableProperty] private bool cameraPolls = true;       // klici kamere ob Scrolled
    [ObservableProperty] private int durationSeconds = 10;      // 0 = brez omejitve

    // Poskusi (veljajo za NOVO ustvarjene celice: po spremembi pritisnite Rebuild)
    [ObservableProperty] private bool keepAliveViews = false;   // posnema Hot Reload: drži reference na vse elemente in nativne poglede
    [ObservableProperty] private bool cardShadow = true;        // Border.Shadow (ustvari WrapperView)
    [ObservableProperty] private bool cardRoundedShape = true;  // Border.StrokeShape RoundRectangle

    // syncfusion/maui-toolkit#392 (SfCartesianChart)
    [ObservableProperty] private bool chartsInCells = true;     // graf v vsaki skupini (velja po Rebuild)
    [ObservableProperty] private bool chartUpdates = true;      // MQTT sporočila posodabljajo grafe (točka + Min/Max/Interval)
    [ObservableProperty] private bool chartAxisBurst = false;   // ob vsaki posodobitvi še 100x Min/Max/Interval
    [ObservableProperty] private int chartUpdateMinMs = 50;     // najmanjši razmik med posodobitvami grafov
    [ObservableProperty] private bool shellNavigate = false;    // med kaosom preklaplja Shell strani (uničenje fragmenta / handlerjev)

    public override string ToString() =>
        $"GC={GcHammer}, CPU={CpuBurn}, Flood={UiFlood}, FlipRight={FlipRight}, ReplaceRows={ReplaceRows}, " +
        $"ToggleVisibility={ToggleVisibility}, AutoScroll={AutoScroll}, CameraPolls={CameraPolls}, Duration={DurationSeconds}s, " +
        $"KeepAliveViews={KeepAliveViews}, CardShadow={CardShadow}, CardRoundedShape={CardRoundedShape}, " +
        $"ChartsInCells={ChartsInCells}, ChartUpdates={ChartUpdates}, ChartAxisBurst={ChartAxisBurst}, " +
        $"ChartUpdateMinMs={ChartUpdateMinMs}, ShellNavigate={ShellNavigate}";
}
