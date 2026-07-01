# Overhaullib (OverhaullibLegacy / `overhaulliblegacycompat`) — Memory & Performance Audit

Date: 2026-06-20
Scope: `source/**` of `OverhaullibLegacy`. Focus: memory leaks and per-frame/per-tick
performance, with entity-related code prioritized. No functional/behavior changes are
proposed — every task below preserves current behavior.

> **Status: all tasks (1–7) applied in v1.1.40.** Build clean (0 errors); all 18 tests pass,
> including the performance-contract suite. Deployed to the active Mods folder.

## How to read this

Each task has a **Severity**, the **Evidence** (file:line), **Why it matters**, a concrete
**Fix**, and **Validation**. The project already enforces optimization invariants through
reflection-based contract tests in
[PerformanceOptimizationContractTests.cs](tests/OverhaullibLegacy.Tests/PerformanceOptimizationContractTests.cs);
where useful, each task suggests a matching contract test so the fix can't silently regress.

Severity legend:
- **P1** — unbounded growth that pins large object graphs (entities/animators) for a whole
  session, or compounding O(n) slowdown. Fix first.
- **P2** — bounded-but-leaky caches, or avoidable per-frame GPU/CPU work on every visible entity.
- **P3** — defensive hardening, dead code, micro-allocations.

---

## P1 — Highest priority

### Task 1 — `AnimatorPlayerMap` pins every `ClientAnimator` + `EntityPlayer` for the whole session

**Severity:** P1 (entity memory leak)

**Evidence:**
- [AnimationPatches.cs:189-212](source/Integration/AnimationPatches.cs#L189) — `AnimatorPlayerMap`
  holds `private readonly Dictionary<ClientAnimator, EntityPlayer> _mapping`. Entries are only
  ever added (`Add`) or wiped wholesale (`Clear`).
- [AnimationPatches.cs:85-87](source/Integration/AnimationPatches.cs#L85) — `Clear()` is called
  only from `Unpatch` (mod teardown / session end).
- Populated every client frame for every player:
  [Patches.cs:322-333](source/Integration/Patches.cs#L322) (`CreateColliders` prefix on
  `AnimationManager.OnClientFrame`) and
  [Animatable.cs:218-221](source/Animations/ItemsAnimations/Animatable.cs#L218).

**Why it matters:** `_mapping` is keyed by `ClientAnimator` and strongly references the
`EntityPlayer`. A player keeps one `ClientAnimator` while loaded, but the game creates a **new**
`ClientAnimator` each time their renderer is rebuilt (entering/leaving view range, model reloads,
respawns). The previous animator and the `EntityPlayer` it points at are never removed — they live
until `Unpatch`. On any populated server, players repeatedly cross view range over a session, so
this dictionary grows without bound and pins whole `EntityPlayer` graphs (inventory, behaviors,
animators). This is the single largest entity-related leak in the library.

**Fix:** Replace the backing store with a `ConditionalWeakTable<ClientAnimator, EntityPlayer>`.
The key is weakly held, so when a `ClientAnimator` becomes unreachable the entry (and its hold on
the `EntityPlayer`) is collected automatically — no despawn hook needed. Keep the public surface
(`Add`, `Get`, `Clear`) identical:
- `Add`: preserve the current "skip if same animator already maps to same entity id" dedupe using
  `TryGetValue`; otherwise `AddOrUpdate`.
- `Get`: `TryGetValue` (still O(1), still safe in the `OnFrameInvoke` hot path at
  [AnimationPatches.cs:112](source/Integration/AnimationPatches.cs#L112)).
- `Clear`: replace the table instance (`ConditionalWeakTable` has no `Clear` on the targeted
  framework) — in `Unpatch`, `Animators = null` already drops it.

**Defensive add (cheap):** In `AnimationPatches.Unpatch`, also clear the entity-id-keyed statics so
a within-process world reload starts clean even if some despawn callbacks were skipped:
`AnimationBehaviors.Clear(); ActiveEntities.Clear();`
([AnimationPatches.cs:22-25](source/Integration/AnimationPatches.cs#L22)). These are normally
cleaned per-entity via `ThirdPersonAnimationsBehavior.OnEntityDespawn`/`DisposeCore`
([ThirdPersonAnimationsBehavior.cs:89-93](source/Animations/ThirdPersonAnimationsBehavior.cs#L89),
[:527-553](source/Animations/ThirdPersonAnimationsBehavior.cs#L527)), so this is insurance, not the
primary fix.

**Validation:** Add a contract test asserting `AnimatorPlayerMap`'s backing field is a
`ConditionalWeakTable<,>` (mirrors the style of
`AnimationPatches_DispatchesBeforeFrameDirectlyByEntityId`). Manual: join a busy area, repeatedly
walk players in/out of range, confirm managed heap / `EntityPlayer` instance count stabilizes
instead of climbing.

---

### Task 2 — `ItemSlotTakeOutOnly` leaks a permanent `SlotModified` subscription on the hotbar every bag reload

**Severity:** P1 (inventory leak + compounding per-modification CPU)

**Evidence:**
- [BagSlots.cs:68-85](source/Framework/Inventory/Bags/BagSlots.cs#L68) — constructor subscribes
  **lambdas** to two long-lived inventories and never stores or removes them:
  - `inventory.SlotModified += index => EnqueueTryEmpty(inventory, index);`
  - `hotbar.SlotModified += index => EnqueueTryEmpty(hotbar, index);` (the player hotbar — lives the
    whole session).
- These slots are recreated on **every** bag (re)build:
  [BagBehavior.cs:527](source/Framework/Inventory/Bags/BagBehavior.cs#L527),
  [:540](source/Framework/Inventory/Bags/BagBehavior.cs#L540),
  [:659](source/Framework/Inventory/Bags/BagBehavior.cs#L659),
  [:679](source/Framework/Inventory/Bags/BagBehavior.cs#L679) — driven by `GetOrCreateSlots`, which
  runs from the patched `ReloadBagInventory` ([Patches.cs:428-442](source/Integration/Patches.cs#L428))
  whenever bag contents change or the player equips/unequips a tool bag.

**Why it matters:** Lambda subscriptions can't be removed (no delegate reference is kept, and nothing
tries). Each tool-bag rebuild adds another live lambda to the hotbar's `SlotModified`, each capturing
a now-dead `ItemSlotTakeOutOnly` (and its `ItemStack`). Over a session the hotbar's invocation list
grows without bound — that is the memory leak. It also degrades runtime: **every** hotbar slot change
now fans out to all accumulated dead lambdas, each running `EnqueueTryEmpty`, so cost grows roughly
O(reloads) per hotbar edit. Tool-bag/quiver users get progressive slowdown plus heap growth.

**Fix (behavior-preserving):** Stop subscribing per-slot to long-lived external inventories with
un-removable lambdas. Options, in order of preference:
1. Move the "auto-empty take-out slots" responsibility to a single owner (the bag behavior or
   `ToolBagSystem`) that subscribes **once per inventory** and dispatches to the current take-out
   slots, instead of one subscription per slot per rebuild.
2. If keeping it per-slot: store the handlers as named delegates in fields and unsubscribe them when
   the slot is discarded. Because `ItemSlot` has no disposal hook, give the owning inventory/behavior
   a teardown path (called from `ReloadBagInventory` before it replaces the slot array) that walks
   the old `ItemSlotTakeOutOnly` slots and removes their handlers.
3. Minimum viable: before each `ReloadBagInventory` rebuild, detach handlers registered by the
   previous generation of take-out slots so the hotbar's invocation list cannot grow across reloads.

Keep the take-out/auto-empty behavior itself unchanged — only the subscription lifecycle changes.

**Validation:** Unit/integration test that rebuilds a player's bag inventory N times and asserts the
hotbar `SlotModified` invocation-list length does not grow with N. Manual: equip a tool bag, churn
hotbar/bag items for a while, watch for a growing `SlotModified` delegate count / rising frame cost
on hotbar edits.

---

## P2 — Bounded leaks & avoidable per-frame work

### Task 3 — `_wearableLightHsvCache` is never evicted per entity (and is globally wiped, defeating itself)

**Severity:** P2 (slow unbounded growth between clears; cache-effectiveness bug)

**Evidence:**
- [Patches.cs:35](source/Integration/Patches.cs#L35) — `Dictionary<long, WearableLightHsvCacheEntry>`
  keyed by entity id.
- Entries added for every player whose `LightHsv` getter runs:
  [Patches.cs:549-564](source/Integration/Patches.cs#L549).
- Only removals are full `Clear()` calls in `Patch`/`Unpatch`
  ([Patches.cs:46-47](source/Integration/Patches.cs#L46),
  [:129](source/Integration/Patches.cs#L129)) and on **any** bag reload / slot save
  ([Patches.cs:432](source/Integration/Patches.cs#L432),
  [:467](source/Integration/Patches.cs#L467)).

**Why it matters:** Two coupled issues. (a) There is no per-entity eviction on despawn/disconnect, so
the dictionary accumulates one entry per distinct entity id seen between clears (small per entry, but
unbounded). (b) The clears are global — one player editing a bag wipes the cached light for **all**
players, so on a busy server the 250 ms cache rarely survives, undercutting the optimization that
`HarmonyPatches_CachesWearableLightHsvAggregation` exists to protect.

**Fix:** Evict by entity instead of globally.
- Add a per-entity invalidation entry point and call it from the wearable-light item's own change
  path / `OnEntityDespawn` rather than calling `_wearableLightHsvCache.Clear()` from
  `ReloadBagInventory`/`SaveSlotIntoBag`. At minimum, change those two call sites to remove only the
  affected owner's entry (`_wearableLightHsvCache.Remove(ownerEntityId)`), which both bounds growth
  for that owner and stops nuking everyone else's cache.
- Optionally add a cheap periodic prune (drop entries whose `ExpiresAtMs` is far in the past) so ids
  for departed entities don't linger.

**Validation:** Extend `HarmonyPatches_CachesWearableLightHsvAggregation` to assert the bag-reload
paths call `Remove` (entity-scoped) rather than `Clear`. Manual: many players with/without wearable
lights, confirm dictionary size tracks currently-loaded entities, not cumulative.

---

### Task 4 — `CollidersEntityBehavior.Render` rebinds the active shader every frame for every visible entity, even with debug rendering off

**Severity:** P2 (per-entity-per-frame GPU state churn)

**Evidence:** [CollidersEntityBehavior.cs:176-200](source/Colliders/CollidersEntityBehavior.cs#L176).
`Render` runs from the `DoRender3DOpaque`/`DoRender3DOpaquePlayer` prefixes
([AnimationPatches.cs:175-225](source/Integration/AnimationPatches.cs#L175)) for every shape-rendered
entity, on opaque and shadow passes. It unconditionally does `currentShader?.Stop()` … `currentShader?.Use()`
around the collider loop, but the shader stop/use is only needed for the debug `RenderLine` path
(`collider.Render`), which only runs when the static `RenderColliders` flag is on (default off).

**Why it matters:** During normal play (`RenderColliders == false`) every visible entity still pays
two shader rebinds per frame for no visual output. With many entities on screen at 60+ fps this is
pure wasted GL state churn.

**Fix:** Gate the shader stop/use on `RenderColliders`. The renderer-assignment loop
(`collider.Renderer ??= renderer; collider.HasRenderer = true;`) must still run (the colliders need a
renderer reference for `Transform`), so keep that loop but only `Stop()`/`Use()` the shader when
`RenderColliders` is true and there is something to draw. Example shape:
```csharp
if (!HasOBBCollider || !entity.Alive) return;
foreach ((string id, ShapeElementCollider collider) in Colliders)
{
    if (!collider.HasRenderer) { collider.Renderer ??= renderer; collider.HasRenderer = true; }
}
if (!RenderColliders) return;
IShaderProgram? currentShader = api.Render.CurrentActiveShader;
currentShader?.Stop();
foreach ((string id, ShapeElementCollider collider) in Colliders)
    if (CollidersTypes.TryGetValue(id, out ColliderTypes value)) collider.Render(api, entityPlayer, _colliderColors[value]);
currentShader?.Use();
```
(Behavior identical: same renderer assignment, same debug output when the toggle is on.)

**Validation:** Contract test asserting `Render` reads `RenderColliders` before any
`IShaderProgram.Stop`/`Use` call (IL-walk like the existing tests). Manual: confirm debug collider
overlay still toggles correctly via `DebugWindowManager`.

---

## P3 — Hardening, dead code, micro-allocations

### Task 5 — `OnModelChanged` subscriptions are never detached (defensive)

**Severity:** P3 (same-entity today; hardening against PlayerModelLib behavior reuse)

**Evidence:**
- [CollidersEntityBehavior.cs:747-756](source/Colliders/CollidersEntityBehavior.cs#L747) —
  `skinBehavior.OnModelChanged += ReloadCollidersForCustomModel;`
- [PlayerDamageModel.cs:508](source/Framework/DamageSystems/PlayerDamageModel.cs#L508) —
  `skinBehavior.OnModelChanged += ReloadConfigForCustomModel;`

Neither behavior overrides `OnEntityDespawn`/`Dispose` to unsubscribe. The `PlayerSkinBehavior` lives
on the same entity, so today both are collected together — not a cross-entity leak. But if
PlayerModelLib ever pools or outlives skin behaviors, these become real leaks.

**Fix:** Override `OnEntityDespawn` in both behaviors to remove the handler
(`skinBehavior.OnModelChanged -= …`) and null the cached reference. `CollidersEntityBehavior` has no
despawn override at all today — add one.

**Validation:** Manual model-swap + despawn cycle; assert no duplicate `ReloadCollidersForCustomModel`
invocations after re-attach.

### Task 6 — Remove dead per-call-allocating pose helpers and unused fields

**Severity:** P3 (dead code; removes confusing allocation hot-spots)

**Evidence (appear unreferenced — confirm with a solution-wide search before deleting):**
- [Animatable.cs:226-313](source/Animations/ItemsAnimations/Animatable.cs#L226) —
  `ApplyExternalItemFrame` / `ApplyItemFrameToPoses`. Only the definitions are referenced; the path
  allocates `new HashSet<int>()` ([:267](source/Animations/ItemsAnimations/Animatable.cs#L267)) and
  `Mat4f.Create()` per recursion level.
- [AnimationPatches.cs:332-430](source/Integration/AnimationPatches.cs#L332) —
  `ApplyCurrentPlayerFrame` / `ApplyPlayerFrameToPoses`, same allocation shape
  ([:362](source/Integration/AnimationPatches.cs#L362)).
- [ItemInInventoryBehavior.cs:27](source/Inputs/ItemInInventoryBehavior.cs#L27) —
  `_reportedEntities` is only `.Clear()`'d ([ModSystems.cs:181](source/ModSystems.cs#L181)); never
  read or appended.

**Why it matters:** These are not on a live hot path, so they're not a current perf cost — but they
look like one and invite "optimize the wrong thing." Removing them clarifies that live per-pose work
goes through `behavior.OnFrame` (which is allocation-free).

**Fix:** Confirm each is unreferenced (grep the whole solution incl. dependent mods), then delete. If
any is intended to be reachable, instead fix the allocation (reuse two shared scratch `float[16]` for
the traversal and a reusable/`Clear()`-ed `HashSet<int>` field) before keeping it.

**Validation:** Build + full test run; the existing contract suite still passes.

### Task 7 — Minor: avoid the per-second `JsonObject` allocation in the animation-behavior watchdog

**Severity:** P3 (trivial steady-state allocation)

**Evidence:** [ModSystems.cs:1019-1055](source/ModSystems.cs#L1019). `EnsureOwnPlayerAnimationBehaviors`
runs every 1 s ([ModSystems.cs:290](source/ModSystems.cs#L290)) and allocates
`JsonObject emptyAttributes = new(new JObject());` even when all three behaviors already exist (the
common case).

**Fix:** Early-return before allocating when
`GetBehavior<FirstPersonAnimationsBehavior>()`, `<ThirdPersonAnimationsBehavior>()`, and
`<WearableStatsBehavior>()` are all non-null; or hoist `emptyAttributes` to a cached field. Negligible
on its own — bundle into Task 6 cleanup.

---

## Notes on what was checked and found healthy

These were inspected and are already well-optimized — no action needed, listed so the audit's
coverage is clear:

- **Projectile collision** ([ProjectileSystem.cs](source/Framework/RangedSystems/ProjectileSystem.cs))
  — scratch lists, candidate-id fast path, late-projectile cache with prune + size cap.
- **Shape collider math** ([EntityCollider.cs](source/Colliders/EntityCollider.cs)) — reuses
  `_vertexScratch`/`_axesScratch`/`_halfSizeScratch`/`_transformScratch`; struct-only vector math.
- **Animation compose** ([Composer.cs](source/Animations/AnimationSystem/Composer.cs)) — scratch
  lists, early-out when idle.
- **Melee attack tick** ([MeleeAttack.cs](source/Framework/MeleeSystems/MeleeAttack.cs)) — scratch
  buffers for collisions/packets.
- **Lifecycle cleanup** for `WearableStatsBehavior`
  ([ArmorStatsBehavior.cs](source/Framework/ArmorSystems/ArmorStatsBehavior.cs)),
  `FirstPersonAnimationsBehavior` / `ThirdPersonAnimationsBehavior`, `ActionListener`
  ([ActionListener.cs](source/Inputs/ActionListener.cs)), `FueledItemSystem`
  ([FueledItemSystem.cs](source/FueledItemSystem.cs)), `ImpaleSystem`, and the renderers/tick
  listeners in `ModSystems` — all unsubscribe their events/listeners on dispose/despawn.

## Suggested order of work

1. Task 1 (animator map) and Task 2 (take-out slot subscriptions) — the two true leaks.
2. Task 3 (wearable-light cache eviction) and Task 4 (collider shader rebind) — cheap, measurable.
3. Tasks 5–7 — hardening and cleanup, batchable into one PR.

Each task is independent and behavior-preserving; they can land as separate commits/PRs.
