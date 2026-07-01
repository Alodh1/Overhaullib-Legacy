using CombatOverhaul.Utils;
using HarmonyLib;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace CombatOverhaul.Integration;

internal static class HandbookLinkPatches
{
    public static void Patch(string harmonyId)
    {
        MethodInfo? method = GetPageCodeMethod();
        if (method == null)
        {
            return;
        }

        Harmony harmony = new(harmonyId);
        harmony.Patch(method, postfix: new HarmonyMethod(AccessTools.Method(typeof(HandbookLinkPatches), nameof(GetPageCodeForStackPostfix))));
    }

    public static void Unpatch(string harmonyId)
    {
        MethodInfo? method = GetPageCodeMethod();
        if (method == null)
        {
            return;
        }

        new Harmony(harmonyId).Unpatch(method, HarmonyPatchType.Postfix, harmonyId);
    }

    private static MethodInfo? GetPageCodeMethod()
    {
        return AccessTools.Method(
            typeof(CollectibleBehaviorHandbookTextAndExtraInfo),
            "getPageCodeForStack",
            [typeof(ICoreClientAPI), typeof(ItemStack)]
        );
    }

    private static void GetPageCodeForStackPostfix(ICoreClientAPI capi, ItemStack stack, ref string __result)
    {
        if (capi == null || stack?.Collectible == null || string.IsNullOrEmpty(__result))
        {
            return;
        }

        ItemStack[]? handbookStacks = ObjectCacheUtil.TryGet<ItemStack[]>(capi, "handbookallstacks");
        if (handbookStacks == null || handbookStacks.Length == 0)
        {
            return;
        }

        if (HandbookContainsPageCode(capi, handbookStacks, __result))
        {
            return;
        }

        ItemStack? matchingPageStack = handbookStacks.FirstOrDefault(candidate =>
            candidate?.Collectible != null &&
            candidate.Satisfies(stack)
        );

        if (matchingPageStack != null)
        {
            __result = GetPageCode(capi, matchingPageStack);
        }
    }

    private static bool HandbookContainsPageCode(ICoreClientAPI capi, IEnumerable<ItemStack> handbookStacks, string pageCode)
    {
        return handbookStacks.Any(stack =>
            stack?.Collectible != null &&
            GetPageCode(capi, stack) == pageCode
        );
    }

    private static string GetPageCode(ICoreClientAPI capi, ItemStack stack)
    {
        return stack.Collectible.GetCollectibleInterface<IHandBookPageCodeProvider>()?.HandbookPageCodeForStack(capi.World, stack)
            ?? GuiHandbookItemStackPage.PageCodeForStack(stack);
    }
}
