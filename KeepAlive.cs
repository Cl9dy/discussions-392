namespace Repro38612;

// Poskus: posnema to, kar (verjetno) naredi Hot Reload, torej drži močne reference na vse
// elemente, njihove handlerje, nativne poglede in WrapperView (ContainerView), da jih GC ne more pobrati.
// Namerno uhaja pomnilnik.
public static class KeepAlive
{
    private static readonly List<object> items = [];
    private static readonly object gate = new();

    public static int Count
    {
        get { lock (gate) { return items.Count; } }
    }

    public static void Track(VisualElement element)
    {
        lock (gate) { items.Add(element); }

        Capture(element);
        element.HandlerChanged += (_, _) => Capture(element);

        if (element is IVisualTreeElement tree)
        {
            foreach (IVisualTreeElement child in tree.GetVisualChildren())
            {
                if (child is VisualElement childElement)
                {
                    Track(childElement);
                }
            }
        }
    }

    private static void Capture(VisualElement element)
    {
        if (element.Handler is not IViewHandler handler) return;

        lock (gate)
        {
            items.Add(handler);
            if (handler.PlatformView is not null) items.Add(handler.PlatformView);
            if (handler.ContainerView is not null) items.Add(handler.ContainerView);
        }
    }
}
