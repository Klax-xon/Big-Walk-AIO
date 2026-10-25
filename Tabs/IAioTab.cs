namespace BigWalk.AIO.Tabs;

/// <summary>
/// Modular tab view interface for the Big Walk AIO harness.
/// Adding a new custom tool or diagnostic tab requires implementing
/// this interface and registering it in <see cref="BigWalk.AIO.UI.MainWindow"/>.
/// </summary>
public interface IAioTab
{
    /// <summary>
    /// Display name rendered on the tab header / sidebar item.
    /// </summary>
    string TabName { get; }

    /// <summary>
    /// ImGui draw routine executed when this tab is selected and visible.
    /// </summary>
    void Draw();
}
