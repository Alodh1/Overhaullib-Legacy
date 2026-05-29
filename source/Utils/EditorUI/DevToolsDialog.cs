#if DEBUG
using Vintagestory.API.Client;

namespace CombatOverhaul.Animations.EditorUI;

internal sealed class DevToolsDialog : GuiDialog
{
    private const string ComposerKey = "combatoverhaul:dev-tools-dialog";
    private readonly DebugWindowManager _manager;
    private readonly EditorAppState _state;
    private readonly EditorInputRouter _inputRouter;

    public DevToolsDialog(ICoreClientAPI capi, DebugWindowManager manager, EditorAppState state, EditorInputRouter inputRouter) : base(capi)
    {
        _manager = manager;
        _state = state;
        _inputRouter = inputRouter;
    }

    public override string ToggleKeyCombinationCode => "";
    public override bool PrefersUngrabbedMouse => false;

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        _inputRouter.SetActive(true);
        _state.SetStatus("Proper UI shell active. Use ImGui fallback for full tools while panels are ported.");
        ComposeDialog();
    }

    public override void OnGuiClosed()
    {
        _inputRouter.SetActive(false);
        base.OnGuiClosed();
        _manager.NotifyProperDevToolsClosed();
    }

    internal void RecomposeIfOpen()
    {
        if (IsOpened())
        {
            ComposeDialog();
        }
    }

    private void ComposeDialog()
    {
        ClearComposers();

        ElementBounds rootBounds = ElementBounds.Fixed(0, 0, EditorTheme.WindowWidth, EditorTheme.WindowHeight);
        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.CenterMiddle)
            .WithChild(rootBounds);

        SingleComposer = capi.Gui.CreateCompo(ComposerKey, dialogBounds);
        ElementBounds backgroundBounds = rootBounds.FlatCopy().FixedGrow(GuiStyle.ElementToDialogPadding);
        SingleComposer.AddShadedDialogBG(backgroundBounds, true);
        SingleComposer.AddDialogTitleBar("Dev tools", () => TryClose());

        ComposeToolbar();
        ComposePanels();
        ComposeFooter();

        SingleComposer.Compose();
    }

    private void ComposeToolbar()
    {
        double x = EditorTheme.Padding;
        double y = 34;
        double buttonWidth = 118;
        double buttonHeight = 28;

        AddTabButton("Animations", DevToolsTab.Animations, x, y, buttonWidth, buttonHeight); x += buttonWidth + EditorTheme.Gap;
        AddTabButton("Transforms", DevToolsTab.Transforms, x, y, buttonWidth, buttonHeight); x += buttonWidth + EditorTheme.Gap;
        AddTabButton("Particles", DevToolsTab.Particles, x, y, buttonWidth, buttonHeight); x += buttonWidth + EditorTheme.Gap;
        AddTabButton("Colliders", DevToolsTab.Colliders, x, y, buttonWidth, buttonHeight); x += buttonWidth + EditorTheme.Gap;
        AddTabButton("Debug", DevToolsTab.Debug, x, y, buttonWidth, buttonHeight); x += buttonWidth + EditorTheme.Gap;
        AddTabButton("Generic", DevToolsTab.GenericDisplay, x, y, buttonWidth, buttonHeight);

        ElementBounds modeTextBounds = ElementBounds.Fixed(EditorTheme.WindowWidth - 342, y + 5, 150, 24);
        SingleComposer.AddDynamicText("UI mode: Proper UI", EditorTheme.BodyFont, modeTextBounds, "editor-ui-mode-label");
        ElementBounds fallbackButtonBounds = ElementBounds.Fixed(EditorTheme.WindowWidth - 184, y, 158, buttonHeight);
        SingleComposer.AddButton("Use ImGui", SwitchToImGui, fallbackButtonBounds, EditorTheme.ButtonFont, EnumButtonStyle.Normal, "switch-to-imgui");
    }

    private void ComposePanels()
    {
        double top = 34 + EditorTheme.ToolbarHeight + EditorTheme.Gap;
        double bottomTop = EditorTheme.WindowHeight - EditorTheme.FooterHeight - EditorTheme.BottomPanelHeight - EditorTheme.Padding;
        double mainHeight = bottomTop - top - EditorTheme.Gap;
        double left = EditorTheme.Padding;
        double centerLeft = left + EditorTheme.LeftPanelWidth + EditorTheme.Gap;
        double rightLeft = EditorTheme.WindowWidth - EditorTheme.Padding - EditorTheme.RightPanelWidth;
        double centerWidth = rightLeft - centerLeft - EditorTheme.Gap;

        ElementBounds browser = ElementBounds.Fixed(left, top, EditorTheme.LeftPanelWidth, mainHeight);
        ElementBounds viewport = ElementBounds.Fixed(centerLeft, top, centerWidth, mainHeight);
        ElementBounds properties = ElementBounds.Fixed(rightLeft, top, EditorTheme.RightPanelWidth, mainHeight);
        ElementBounds timeline = ElementBounds.Fixed(left, bottomTop, EditorTheme.WindowWidth - EditorTheme.Padding * 2, EditorTheme.BottomPanelHeight);

        AddPanel(browser, "Animation browser", GetBrowserText());
        AddPanel(viewport, "Viewport", GetViewportText());
        AddPanel(properties, "Properties", GetPropertiesText());
        AddPanel(timeline, "Timeline / dope sheet", GetTimelineText());
    }

    private void ComposeFooter()
    {
        double y = EditorTheme.WindowHeight - EditorTheme.FooterHeight;
        ElementBounds footerBounds = ElementBounds.Fixed(EditorTheme.Padding, y, EditorTheme.WindowWidth - EditorTheme.Padding * 2, EditorTheme.FooterHeight - 4);
        SingleComposer.AddInset(footerBounds, 1);
        SingleComposer.AddDynamicText(_state.StatusText, EditorTheme.MutedFont, footerBounds.FlatCopy().FixedGrow(-8, -2), "status-footer");
    }

    private void AddTabButton(string label, DevToolsTab tab, double x, double y, double width, double height)
    {
        string display = _state.SelectedTab == tab ? $"* {label}" : label;
        SingleComposer.AddButton(display, () => SelectTab(tab), ElementBounds.Fixed(x, y, width, height), EditorTheme.ButtonFont, EnumButtonStyle.Normal, $"tab-{tab}");
    }

    private void AddPanel(ElementBounds bounds, string title, string body)
    {
        SingleComposer.AddInset(bounds, 1);
        SingleComposer.AddDynamicText(title, EditorTheme.HeaderFont, ElementBounds.Fixed(bounds.fixedX + 10, bounds.fixedY + 8, bounds.fixedWidth - 20, 24), $"panel-title-{title}");
        SingleComposer.AddDynamicText(body, EditorTheme.BodyFont, ElementBounds.Fixed(bounds.fixedX + 10, bounds.fixedY + 38, bounds.fixedWidth - 20, bounds.fixedHeight - 46), $"panel-body-{title}");
    }

    private bool SelectTab(DevToolsTab tab)
    {
        _state.SelectedTab = tab;
        _state.SetStatus($"Selected {GetTabLabel(tab)}. This panel will be ported from ImGui in a later slice.");
        ComposeDialog();
        return true;
    }

    private bool SwitchToImGui()
    {
        _manager.SwitchToImGuiDevTools();
        return true;
    }

    private string GetBrowserText()
    {
        return _state.SelectedTab switch
        {
            DevToolsTab.Animations => "Animation list, filters, and source grouping will live here.",
            DevToolsTab.Transforms => "Registered item/block transforms will live here.",
            DevToolsTab.Particles => "Particle effect presets will live here.",
            DevToolsTab.Colliders => "Weapon collider sets will live here.",
            DevToolsTab.Debug => "Runtime debug toggles will live here.",
            DevToolsTab.GenericDisplay => "Generic display target list will live here.",
            _ => "Browser."
        };
    }

    private string GetViewportText()
    {
        return "Player preview, gizmos, onion skins, and motion paths will render here once the tools are ported. For now, switch to ImGui for the functional editor.";
    }

    private string GetPropertiesText()
    {
        return $"Selected tab: {GetTabLabel(_state.SelectedTab)}\nAnimation: {_state.SelectedAnimation}\nKeyframe: {_state.SelectedKeyframe}\nRig part: {_state.SelectedRigPart}\nTool: {_state.SelectedTool}\nCamera: {_state.CameraMode}";
    }

    private static string GetTimelineText()
    {
        return "Timeline controls, marker selection, retiming, insert/delete/duplicate, and range actions will be moved here in later phases.";
    }

    private static string GetTabLabel(DevToolsTab tab)
    {
        return tab switch
        {
            DevToolsTab.GenericDisplay => "Generic Display",
            _ => tab.ToString()
        };
    }
}
#endif
