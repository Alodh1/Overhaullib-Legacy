using CombatOverhaul.Implementations;
using CombatOverhaul.Utils;
using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace CombatOverhaul.Integration;

internal static class HandbookGroupedWeaponPatches
{
    private static ICoreClientAPI? _api;
    private static readonly object LoggedTooltipFailuresLock = new();
    private static readonly HashSet<string> LoggedTooltipFailures = new(StringComparer.Ordinal);

    public static void Patch(string harmonyId, ICoreClientAPI api)
    {
        MethodInfo? method = AccessTools.Method(typeof(ModSystemSurvivalHandbook), "onCreatePagesAsync");
        MethodInfo? createdByMethod = AccessTools.Method(typeof(CollectibleBehaviorHandbookTextAndExtraInfo), "addCreatedByInfo");
        if (method == null) return;

        _api = api;
        Harmony harmony = new(harmonyId);
        harmony.Patch(method, postfix: new HarmonyMethod(AccessTools.Method(typeof(HandbookGroupedWeaponPatches), nameof(OnCreatePagesAsyncPostfix))));

        if (createdByMethod != null)
        {
            harmony.Patch(createdByMethod, transpiler: new HarmonyMethod(AccessTools.Method(typeof(HandbookGroupedWeaponPatches), nameof(AddCreatedByInfoTranspiler))));
        }
    }

    public static void Unpatch(string harmonyId)
    {
        MethodInfo? method = AccessTools.Method(typeof(ModSystemSurvivalHandbook), "onCreatePagesAsync");
        if (method != null)
        {
            new Harmony(harmonyId).Unpatch(method, HarmonyPatchType.Postfix, harmonyId);
        }

        MethodInfo? createdByMethod = AccessTools.Method(typeof(CollectibleBehaviorHandbookTextAndExtraInfo), "addCreatedByInfo");
        if (createdByMethod != null)
        {
            new Harmony(harmonyId).Unpatch(createdByMethod, HarmonyPatchType.Transpiler, harmonyId);
        }

        _api = null;
    }

    private static IEnumerable<CodeInstruction> AddCreatedByInfoTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo? original = AccessTools.Method(typeof(ItemStack), nameof(ItemStack.Satisfies), [typeof(ItemStack)]);
        MethodInfo? replacement = AccessTools.Method(typeof(HandbookGroupedWeaponPatches), nameof(SatisfiesForGroupedHandbookPage));

        foreach (CodeInstruction instruction in instructions)
        {
            if (original != null && replacement != null && instruction.Calls(original))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
            }

            yield return instruction;
        }
    }

    public static bool SatisfiesForGroupedHandbookPage(ItemStack? candidate, ItemStack? pageStack)
    {
        if (candidate == null || pageStack == null) return false;
        if (candidate.Satisfies(pageStack)) return true;

        return TextureAttributeHandbook.MatchesIgnoringTextureAttributes(pageStack, candidate);
    }

    private static void OnCreatePagesAsyncPostfix(ref List<GuiHandbookPage> __result)
    {
        ICoreClientAPI? api = _api;
        if (api == null || __result.Count == 0) return;

        Dictionary<string, List<ItemStack>> stacksByGroup = new(StringComparer.Ordinal);
        Dictionary<string, HashSet<string>> seenStacksByGroup = new(StringComparer.Ordinal);
        Dictionary<GuiHandbookPage, string> groupByPage = new();

        foreach (GuiHandbookPage page in __result)
        {
            if (page is not GuiHandbookItemStackPage stackPage) continue;
            if (!TextureAttributeHandbook.ShouldUseGroupedHandbookPage(stackPage.Stack)) continue;

            string groupPageCode = TextureAttributeHandbook.VisualAttributePageCodeForStack(stackPage.Stack);

            groupByPage[page] = groupPageCode;

            if (!stacksByGroup.TryGetValue(groupPageCode, out List<ItemStack>? stacks))
            {
                stacks = new List<ItemStack>();
                stacksByGroup[groupPageCode] = stacks;
                seenStacksByGroup[groupPageCode] = new HashSet<string>(StringComparer.Ordinal);
            }

            string concretePageCode = GuiHandbookItemStackPage.PageCodeForStack(stackPage.Stack);
            if (seenStacksByGroup[groupPageCode].Add(concretePageCode))
            {
                stacks.Add(stackPage.Stack);
            }
        }

        if (groupByPage.Count == 0) return;

        HashSet<string> insertedGroups = new(StringComparer.Ordinal);
        List<GuiHandbookPage> pages = new(__result.Count);

        foreach (GuiHandbookPage page in __result)
        {
            if (!groupByPage.TryGetValue(page, out string? groupPageCode))
            {
                pages.Add(page);
                continue;
            }

            if (!stacksByGroup.TryGetValue(groupPageCode, out List<ItemStack>? stacks) || stacks.Count == 0) continue;
            if (stacks.Count == 1)
            {
                pages.Add(page);
                continue;
            }

            if (!insertedGroups.Add(groupPageCode)) continue;

            pages.Add(new CyclingWeaponHandbookPage(api, groupPageCode, stacks)
            {
                Visible = page.Visible
            });
        }

        __result = pages;
    }

    private static void LogTooltipFailureOnce(ICoreClientAPI? capi, ItemStack? stack, Exception exception)
    {
        string code = stack?.Collectible?.Code?.ToString() ?? "<unknown>";
        lock (LoggedTooltipFailuresLock)
        {
            if (!LoggedTooltipFailures.Add(code)) return;
        }

        capi?.Logger?.Warning(
            "Skipping unsafe handbook tooltip text for grouped page stack {0}: {1}: {2}",
            code,
            exception.GetType().Name,
            exception.Message);
    }

    private sealed class CyclingWeaponHandbookPage : GuiHandbookPage
    {
        private readonly string _pageCode;
        private readonly string _displayName;
        private readonly DummySlot _dummySlot;
        private ElementBounds? _scissorBounds;
        private LoadedTexture? _texture;

        public List<ItemStack> Stacks { get; }
        public string TextCacheTitle { get; }
        public string TextCacheAll { get; }

        public override string PageCode => _pageCode;
        public override string CategoryCode => "stack";
        public override bool IsDuplicate => false;
        public override float SearchWeightOffset => 0f;

        public CyclingWeaponHandbookPage(ICoreClientAPI capi, string pageCode, IEnumerable<ItemStack> stacks)
        {
            _pageCode = pageCode;
            Stacks = stacks.Select(stack => stack.Clone()).ToList();

            _displayName = Stacks.Count > 0 ? Stacks[0].GetName() : pageCode;
            _dummySlot = Stacks.Count > 0 ? new DummySlot(Stacks[0]) : new DummySlot();
            TextCacheTitle = StringUtil.ToSearchFriendly(_displayName);
            TextCacheAll = StringUtil.ToSearchFriendly(
                _displayName
                + " "
                + string.Join(" ", Stacks.Select(stack => stack.GetName()))
                + " "
                + GetSafeDescription(capi, Stacks.FirstOrDefault()));
        }

        public override void RenderListEntryTo(ICoreClientAPI capi, float dt, double x, double y, double cellWidth, double cellHeight)
        {
            if (Stacks.Count == 0) return;

            float iconSize = (float)GuiElement.scaled(25.0);
            float leftPadding = (float)GuiElement.scaled(10.0);

            if (_texture == null)
            {
                RecomposeLabel(capi);
            }

            int index = (int)(capi.ElapsedMilliseconds / 1000 % Stacks.Count);
            _dummySlot.Itemstack = Stacks[index];

            _scissorBounds ??= ElementBounds.FixedSize(50.0, 50.0);
            _scissorBounds.ParentBounds = capi.Gui.WindowBounds;
            _scissorBounds.fixedX = (leftPadding + x - iconSize / 2f) / RuntimeEnv.GUIScale;
            _scissorBounds.fixedY = (y - iconSize / 2f) / RuntimeEnv.GUIScale;
            _scissorBounds.CalcWorldBounds();

            if (_scissorBounds.InnerWidth <= 0 || _scissorBounds.InnerHeight <= 0) return;

            capi.Render.PushScissor(_scissorBounds, true);
            capi.Render.RenderItemstackToGui(_dummySlot, x + leftPadding + iconSize / 2f, y + iconSize / 2f, 100.0, iconSize, -1, true, false, false);
            capi.Render.PopScissor();

            if (_texture != null)
            {
                capi.Render.Render2DTexturePremultipliedAlpha(
                    _texture.TextureId,
                    x + iconSize + GuiElement.scaled(25.0),
                    y + iconSize / 4f - GuiElement.scaled(3.0),
                    _texture.Width,
                    _texture.Height,
                    50f,
                    null);
            }
        }

        public override void ComposePage(GuiComposer detailViewGui, ElementBounds textBounds, ItemStack[] allstacks, ActionConsumable<string> openDetailPageFor)
        {
            RichTextComponentBase[] pageText = GetPageText(detailViewGui.Api, allstacks, openDetailPageFor);
            Vintagestory.API.Client.GuiComposerHelpers.AddRichtext(detailViewGui, pageText, textBounds, "richtext");
        }

        public override PageText GetPageText()
        {
            return new PageText
            {
                Title = TextCacheTitle,
                Text = TextCacheAll
            };
        }

        public override void Dispose()
        {
            _texture?.Dispose();
            _texture = null;
        }

        private RichTextComponentBase[] GetPageText(ICoreClientAPI capi, ItemStack[] allStacks, ActionConsumable<string> openDetailPageFor)
        {
            if (Stacks.Count == 0) return Array.Empty<RichTextComponentBase>();

            _dummySlot.Itemstack = Stacks[0];
            try
            {
                return Stacks[0].Collectible.GetBehavior<CollectibleBehaviorHandbookTextAndExtraInfo>()?.GetHandbookInfo(_dummySlot, capi, allStacks, openDetailPageFor)
                    ?? Array.Empty<RichTextComponentBase>();
            }
            catch (Exception exception)
            {
                LogTooltipFailureOnce(capi, Stacks[0], exception);
                string fallbackText = GetSafeDescription(capi, Stacks[0]);
                return string.IsNullOrWhiteSpace(fallbackText)
                    ? Array.Empty<RichTextComponentBase>()
                    : VtmlUtil.Richtextify(capi, fallbackText, CairoFont.WhiteSmallText(), null);
            }
        }

        private void RecomposeLabel(ICoreClientAPI capi)
        {
            _texture?.Dispose();
            _texture = new TextTextureUtil(capi).GenTextTexture(_displayName, CairoFont.WhiteSmallText(), null);
        }

        private string GetSafeDescription(ICoreClientAPI capi, ItemStack? stack)
        {
            if (stack == null) return "";

            _dummySlot.Itemstack = stack;
            try
            {
                return stack.GetDescription(capi.World, _dummySlot, false);
            }
            catch (Exception exception)
            {
                LogTooltipFailureOnce(capi, stack, exception);
                return "";
            }
        }
    }
}
