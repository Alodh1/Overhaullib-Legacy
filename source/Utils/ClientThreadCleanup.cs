using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace CombatOverhaul.Utils;

internal static class ClientThreadCleanup
{
    public static void DisposeRenderer(ICoreClientAPI? clientApi, IRenderer? renderer, EnumRenderStage stage, string taskCode)
    {
        if (clientApi == null || renderer == null) return;

        Run(clientApi, () =>
        {
            try
            {
                clientApi.Event.UnregisterRenderer(renderer, stage);
            }
            finally
            {
                renderer.Dispose();
            }
        }, taskCode);
    }

    public static void Run(ICoreClientAPI clientApi, Action cleanup, string taskCode)
    {
        if (RuntimeEnv.MainThreadId == Environment.CurrentManagedThreadId)
        {
            RunSafely(clientApi, cleanup, taskCode);
            return;
        }

        try
        {
            clientApi.Event.EnqueueMainThreadTask(() => RunSafely(clientApi, cleanup, taskCode), taskCode);
        }
        catch (Exception exception)
        {
            clientApi.Logger.Warning($"[OverhaullibLegacyCompat] Could not queue client cleanup '{taskCode}' on the main thread: {exception}");
        }
    }

    private static void RunSafely(ICoreClientAPI clientApi, Action cleanup, string taskCode)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            clientApi.Logger.Warning($"[OverhaullibLegacyCompat] Client cleanup '{taskCode}' failed: {exception}");
        }
    }
}
