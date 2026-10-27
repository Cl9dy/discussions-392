using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Repro38612.Models;

public sealed class ChartPoint
{
    public double X { get; init; }
    public double Y { get; init; }
}

// Podatkovni model (ni View), podoben SmartHouseUIControl
public partial class DemoControl : ObservableObject
{
    private const int MaxPoints = 40;
    private double x;

    public string Name { get; set; } = "";
    public bool IsFullWidth { get; set; }
    public bool IsCamera { get; set; }
    public bool IsChart { get; set; }

    [ObservableProperty]
    private string reading = "";

    // --- Graf (SfCartesianChart) ---------------------------------------------------------------
    // Enako kot v pravi aplikaciji: Y os ima vezane Minimum / Maximum / Interval.
    public ObservableCollection<ChartPoint> Points { get; } = [];

    [ObservableProperty]
    private double? temperatureMinimum = 0;

    [ObservableProperty]
    private double? temperatureMaximum = 30;

    [ObservableProperty]
    private double temperatureInterval = 5;

    // "MQTT sporočilo" za graf: nova točka + os v vrstnem redu Minimum -> Maximum -> Interval
    public void PushSample()
    {
        if (!IsChart) return;

        double value = 15 + (Random.Shared.NextDouble() * 20) - 10;
        Points.Add(new ChartPoint { X = x++, Y = value });
        while (Points.Count > MaxPoints)
        {
            Points.RemoveAt(0);
        }

        double min = Math.Floor(Points.Min(p => p.Y)) - Random.Shared.Next(0, 3);
        double max = Math.Ceiling(Points.Max(p => p.Y)) + Random.Shared.Next(0, 3);

        TemperatureMinimum = min;
        TemperatureMaximum = max;
        TemperatureInterval = Random.Shared.Next(2) == 0 ? 1 : 5;
    }

    // Posnema "hitro posodabljanje TemperatureMinimum (100 iteracij)" iz diskusije #392
    public void AxisBurst(int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            TemperatureMinimum = i % 2 == 0 ? 0 : -5 + Random.Shared.Next(10);
            TemperatureMaximum = 30;
            TemperatureInterval = 5;
        }
    }

    public void SeedChart()
    {
        if (!IsChart) return;
        for (int i = 0; i < 20; i++)
        {
            PushSample();
        }
    }
}

public class DemoGroup : List<DemoControl>
{
    public string Name { get; set; } = "";
    public bool IsGroupHeaderVisible { get; set; } = true;
}

public partial class DeviceRow : ObservableObject
{
    public bool IsHeader { get; set; }
    public string HeaderText { get; set; } = "";

    [ObservableProperty]
    private ObservableCollection<DemoControl>? left;

    [ObservableProperty]
    private ObservableCollection<DemoControl>? right;

    private ObservableCollection<DemoControl>? rightBackup;

    public bool IsFullWidth => Left is { Count: 1 } && Left[0].IsFullWidth;

    // Right preklaplja med null in vrednostjo (Right == null pomeni prazno vsebino)
    public void FlipRight()
    {
        if (Right is not null)
        {
            rightBackup = Right;
            Right = null;
        }
        else if (rightBackup is not null)
        {
            Right = rightBackup;
        }
    }
}

public static class DemoData
{
    // Vsak klic ustvari NOVE objekte, kot pri pravi aplikaciji
    public static List<DemoGroup> Create()
    {
        List<DemoGroup> groups = [];
        for (int g = 0; g < 6; g++)
        {
            DemoGroup group = new() { Name = $"Skupina {g + 1}" };
            for (int i = 0; i < 7; i++)
            {
                bool isCamera = i == 4;
                bool isChart = i == 2 && ChaosOptions.Current.ChartsInCells;

                DemoControl control = new()
                {
                    Name = isChart ? $"Temperatura {g + 1}" : $"Naprava {g + 1}.{i + 1}",
                    Reading = DateTime.Now.ToString("HH:mm:ss.fff"),
                    IsFullWidth = isCamera || isChart,
                    IsCamera = isCamera,
                    IsChart = isChart
                };
                control.SeedChart();
                group.Add(control);
            }
            groups.Add(group);
        }
        return groups;
    }
}
