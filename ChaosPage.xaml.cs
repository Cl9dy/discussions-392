namespace Repro38612;

public partial class ChaosPage : ContentPage
{
    public ChaosPage()
    {
        InitializeComponent();
        BindingContext = ChaosOptions.Current;
    }
}
