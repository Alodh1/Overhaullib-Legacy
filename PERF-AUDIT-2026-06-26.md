# Overhaullib — Performance Audit & Task List (2026-06-26)

Scope: `source/**` of `OverhaullibLegacy`. Focus: **per-frame / per-render-pass CPU on the
client hot paths** (animation pose application, held-item rendering, collider rendering). No
functional/behavior changes are proposed — every task preserves current behavior.

> **Status: all 5 tasks below applied 2026-06-26.** Build clean (0 errors); all 22 tests pass,
> including the performance-contract suite. No behavior changes.

> This is a **follow-up** to [MEMORY-PERF-AUDIT.md](MEMORY-PERF-AUDIT.md) (2026-06-20), whose 7
> tasks (the real memory leaks — `AnimatorPlayerMap`, bag take-out subscriptions, wearable-light
> cache, the collider shader rebind, etc.) were applied in v1.1.40 and are **confirmed still in
> place** in this pass. None of the issues below are those.

## Summary

The animation **core** (`Composer`, `Animator`, `Frames`/`PlayerFrameComposition`,
`Animation.Interpolate`) is genuinely well-optimized: struct-based switch dispatch, scratch lists,
accumulator composition, early-outs when idle. The collider math and projectile system reuse scratch
buffers. The architecture is per-entity-behavior driven — there is **no** central O(all-entities)
per-tick loop, and the `AllOnlinePlayers` loops that exist are all on 1 s+ server timers.

What remains are **avoidable per-frame costs in the render hot paths**. They are individually small
but they run for *every visible entity / every held item / every render pass (opaque + each shadow
cascade) every frame*, so their total cost scales with on-screen player/entity count — exactly the
"benchmark against a crowd" scenario. There is **no new P1** here; the big leaks were the P1s and
they are already fixed.

Severity legend (same as the prior audit):
- **P2** — avoidable per-frame GPU/CPU work on every visible entity/item; scales with crowd size.
- **P3** — micro-allocations / reflection / dead per-frame work; cheap individually, easy wins.

Each task is independent and behavior-preserving; they can land as separate commits/PRs. Where
useful, a matching contract test (in the style of
[PerformanceOptimizationContractTests.cs](tests/OverhaullibLegacy.Tests/PerformanceOptimizationContractTests.cs))
is suggested so the fix can't silently regress.

---

## P2 — Avoidable per-frame work on every visible entity/item

### Task 1 — Reflection `FieldInfo.GetValue` on every animated held-item render

**Severity:** P2 (per held-item, per render pass, per frame, for every player holding an animated item)

**Evidence:**
- [AnimationPatches.cs:27](source/Integration/AnimationPatches.cs#L27) — cached
  `_lightrgbsField = typeof(EntityShapeRenderer).GetField("lightrgbs", …)`.
- [AnimationPatches.cs:270](source/Integration/AnimationPatches.cs#L270) — inside the
  `RenderHeldItem` prefix: `Vec4f? lightrgbs = (Vec4f?)_lightrgbsField?.GetValue(__instance);`
  This prefix runs for **both hands** of **every** entity whose held item has an `Animatable`
  behavior, on the opaque pass **and** every shadow cascade.
- Same method, [AnimationPatches.cs:263-268](source/Integration/AnimationPatches.cs#L263):
  `slot.Itemstack.Item.Textures.First()` (LINQ `.First()` allocates an enumerator over the texture
  dictionary every call) plus an atlas-position lookup, also per held-item-render.

**Why it matters:** `FieldInfo.GetValue` is a reflection invoke (boxing-free here since `Vec4f` is a
reference type, but still ~tens-to-hundreds of ns of reflection dispatch each call). The `FieldInfo`
is already cached, but the *invoke* is paid every frame. With a handful of armed players on screen at
60+ fps across opaque + shadow passes this is thousands of reflection calls per second for a value
that can be read with a compiled accessor in ~1 ns.

**Fix (behavior-preserving):** Build a compiled getter **once** and reuse it:
```csharp
// once, next to _lightrgbsField:
private static readonly Func<EntityShapeRenderer, Vec4f?>? _getLightRgbs = BuildLightRgbsGetter();
// where BuildLightRgbsGetter() emits an Expression/IL accessor for the private 'lightrgbs' field,
// falling back to null (and to the existing reflection path) if the field can't be resolved.
```
Then `Vec4f? lightrgbs = _getLightRgbs?.Invoke(__instance);`. Keep the null-fallback so a vanilla
field rename degrades exactly as today. While here, replace `Textures.First()` with a manual
`foreach`/first-entry read (no enumerator allocation), or cache the chosen texture name per item id.

**Validation:** Contract test asserting `RenderHeldItem` does not call `FieldInfo.GetValue` (IL-walk,
like the existing IL-based contract tests). Manual: several players with animated weapons on screen,
confirm held items still light correctly in first/third person and that frame time drops vs. before.

---

### Task 2 — Collider render path does a per-entity behavior scan + dead renderer-assignment loop every pass

**Severity:** P2 (per visible entity, per render pass — opaque + each shadow cascade — every frame)

**Evidence:**
- [AnimationPatches.cs:184](source/Integration/AnimationPatches.cs#L184) (`DoRender3DOpaque` prefix)
  and [AnimationPatches.cs:227](source/Integration/AnimationPatches.cs#L227)
  (`DoRender3DOpaquePlayer` prefix) both call
  `__instance.entity?.GetBehavior<CollidersEntityBehavior>()` **unconditionally** for every
  shape-rendered entity, on every invocation. These prefixes fire on the opaque pass and on each
  shadow pass. `Entity.GetBehavior<T>()` is a linear scan of the entity's behavior list with a type
  check per element.
- [CollidersEntityBehavior.cs:191-198](source/Colliders/CollidersEntityBehavior.cs#L191) — `Render`
  iterates **all** colliders every call to assign `collider.Renderer`/`HasRenderer`. After the first
  successful assignment this loop does nothing but re-check `HasRenderer == true` on every collider,
  every pass, forever. (The shader rebind below it is already correctly gated on `RenderColliders`
  per the prior audit's Task 4 — that part is fine.)

**Why it matters:** During normal play (`RenderColliders == false`), for a fully-initialized entity
the entire `Render` call is wasted: a `GetBehavior` list-scan to find the behavior, then a full
collider loop that finds every renderer already assigned, then an early return. Multiply by
(visible entities) × (opaque + N shadow cascades) × (frames/sec). It is pure overhead that grows
with crowd size.

**Fix (behavior-preserving):**
1. **Cache the behavior lookup.** Resolve `CollidersEntityBehavior` once per renderer/entity instead
   of every pass — e.g. a `ConditionalWeakTable<EntityShapeRenderer, CollidersEntityBehavior>` in
   `AnimationPatches` (weak key, auto-collected with the renderer), or store the reference on first
   resolution. Keep the `try/catch` + one-shot error log.
2. **Short-circuit the assignment loop.** Track a `bool _renderersAssigned` on the behavior; once the
   colliders have their renderer, skip the loop. Set it false again wherever `Colliders` is rebuilt
   (`ProcessCollidersForCustomModel`, `ApplyConfig`/reload). Then `Render` can early-return when
   `_renderersAssigned && !RenderColliders` after the cheap alive/first-person guards.

**Validation:** Contract test asserting the render prefixes resolve the behavior through the cache
(not a raw `GetBehavior` each call), mirroring the existing IL-based assertions. Manual: toggle the
debug collider overlay via `DebugWindowManager` and confirm it still appears/updates; confirm normal
play frame time improves with many entities on screen.

---

### Task 3 — Held-item `Animatable` behavior is re-resolved every frame while animating

**Severity:** P2/P3 (per animating entity, every frame an animation is active)

**Evidence:**
- [ThirdPersonAnimationsBehavior.cs:241](source/Animations/ThirdPersonAnimationsBehavior.cs#L241) —
  in `OnBeforeFrame`, whenever `_composer.AnyActiveAnimations()`:
  `_animatable = (entity as EntityAgent)?.RightHandItemSlot?.Itemstack?.Item?.GetCollectibleBehavior(typeof(Animatable), true) as Animatable;`
- [FirstPersonAnimationsBehavior.cs:411](source/Animations/FirstPersonAnimationsBehavior.cs#L411) —
  identical pattern in `AdvanceAnimationFrame`.
- The same per-render resolution also happens in
  [AnimationPatches.cs:248](source/Integration/AnimationPatches.cs#L248) (`RenderHeldItem`).

**Why it matters:** `GetCollectibleBehavior(type, withInheritance: true)` walks the collectible's
behavior list every frame for every animating entity. The result only changes when the held item
changes — and both behaviors **already track** the main-hand item id (`_mainHandItemId`) for exactly
that purpose. Re-resolving it every frame is redundant work in the per-frame animation advance.

**Fix (behavior-preserving):** Resolve `_animatable` only when the tracked item id changes (in the
existing `MainHandItemChanged` / `InHandItemChanged` paths, or guarded by a cached
`_animatableItemId`), and reuse the cached reference on subsequent frames. Null it when the hand
empties. Behavior is identical — the same `Animatable` instance is used, just looked up on item
change instead of every frame.

**Validation:** Manual: swap weapons mid-animation (including detached-anchor / switch-arms items)
and confirm attachment behavior is unchanged. Optionally a unit test that drives N frames with a
fixed item and asserts the resolve count is 1, not N.

---

## P3 — Micro-allocations & cheap wins in the per-frame path

### Task 4 — Per-pose `Enum.TryParse` fallback in first-person `ApplyFrame`

**Severity:** P3 (per non-extended pose, per frame, local player only)

**Evidence:** [FirstPersonAnimationsBehavior.cs:472](source/Animations/FirstPersonAnimationsBehavior.cs#L472)
— `if (!Enum.TryParse(pose.ForElement.Name, out element))`. The transpiler normally replaces poses
with `ExtendedElementPose` (which carries a pre-resolved `ElementNameEnum`), so this is a fallback —
but it runs once **per pose that isn't extended, every frame**, and the in-code comment already flags
it ("Cant cache ElementPose because they are new each frame"). `Enum.TryParse` does a name-string
match over the enum members each call.

**Why it matters:** Negligible per call, but it's on the local player's per-pose-per-frame path and
is trivially avoidable. Not a crowd-scaling cost (FP is one entity), hence P3.

**Fix:** Resolve the enum off `pose.ForElement.Name` through the same ordinal
`Dictionary<string, EnumAnimatedElement>` cache that `ExtendedElementPose.ResolveElementName` already
uses ([ElementPose.cs:35](source/Integration/Transpilers/ElementPose.cs#L35)), instead of
`Enum.TryParse`. (Mirror the third-person behavior, which simply `return`s for non-extended poses.)

**Validation:** Build + existing test run; manual FP animation smoke test.

### Task 5 — Micro-allocations in the per-frame interpolation path

**Severity:** P3 (per active animation / per item-pose, per frame)

**Evidence:**
- [Animation.cs:118](source/Animations/AnimationSystem/Animation.cs#L118) and
  [Animation.cs:244](source/Animations/AnimationSystem/Animation.cs#L244) —
  `ItemKeyFrames.Any()` on a `List<ItemKeyFrame>`. `Enumerable.Any` boxes the list enumerator via
  `IEnumerable<T>`; this is on the per-frame `Interpolate` / `InterpolateItemFrame` path. Replace
  with `ItemKeyFrames.Count != 0`.
- [Frames.cs:519-537](source/Animations/AnimationSystem/Frames.cs#L519) — `ItemFrame.TryGetElement(string)`
  falls back to an O(n) `string.Equals` scan when `_elementIndexes == null`. Composed/interpolated
  item frames are built through the private constructor
  ([Frames.cs:421-428](source/Animations/AnimationSystem/Frames.cs#L421)) which sets
  `_elementIndexes = null` (see `Compose` [:516](source/Animations/AnimationSystem/Frames.cs#L516) and
  `Interpolate` [:464](source/Animations/AnimationSystem/Frames.cs#L464)), so every held-item-frame
  `Apply` does the linear scan. Element counts are small so this is minor, but the index map could be
  carried through `Compose`/`Interpolate` (the element name array is already shared/stable) to keep
  `Apply` O(1).

**Why it matters:** Pure steady-state allocation / avoidable scanning on the per-frame compose path.
Small, but free to fix and keeps the otherwise allocation-free animation core clean.

**Fix:** Use `.Count != 0` instead of `.Any()`; optionally reconstruct/propagate `_elementIndexes`
for composed/interpolated `ItemFrame`s (or skip the map for tiny frames and document the linear scan
as intentional).

**Validation:** Build + full test run; the existing animation/compose tests still pass.

---

## Verified healthy (checked, no action needed)

Listed so the audit's coverage is explicit:

- **Animation compose/interpolate core** — `Composer.Compose`
  ([Composer.cs](source/Animations/AnimationSystem/Composer.cs)) uses scratch lists + early-out;
  `PlayerFrameComposition` ([Frames.cs](source/Animations/AnimationSystem/Frames.cs)) is a struct
  accumulator; `Animation.Interpolate` is loop-based (LINQ confined to constructors, JSON, and
  `#if DEBUG` editor code).
- **Held-item render** ([Animatable.cs:77](source/Animations/ItemsAnimations/Animatable.cs#L77)) —
  reuses the `ItemModelMat` field, no per-frame `Matrixf` allocation.
- **Colliders tick** ([CollidersEntityBehavior.OnGameTick](source/Colliders/CollidersEntityBehavior.cs#L125))
  — throttled to 30 fps and skips recalculation via a transform-signature hash.
- **Per-tick behaviors** — `WearableStatsBehavior.OnGameTick`
  ([ArmorStatsBehavior.cs:48](source/Framework/ArmorSystems/ArmorStatsBehavior.cs#L48)) early-returns
  after init (stat updates are event-driven); `PlayerDamageModelBehavior.OnGameTick`
  ([PlayerDamageModel.cs:141](source/Framework/DamageSystems/PlayerDamageModel.cs#L141)) early-returns
  client-side and only reads watched attributes; `ActionsManagerPlayerBehavior.OnGameTick`
  ([ActionsManagerPlayerBehavior.cs:135](source/Inputs/ActionsManagerPlayerBehavior.cs#L135)) is
  local-player-only and caches tick listeners by item id.
- **LINQ-heavy damage code** (`PlayerDamageModel.GetZone/GetMultiplier/GetDamageReductionFactor`,
  melee `GetHeldItemInfo`) runs on damage events / tooltips, **not** per frame.
- **No central per-frame/per-tick loop over all entities or players** — the `AllOnlinePlayers` loops
  (`FueledItemSystem`, `VanitySystem.Resync`) are on 1 s / 60 s server timers; melee/projectile use
  `GetEntitiesAround` only during an active swing/flight.
- `ConditionalWeakTable.TryGetValue` in `AnimatorPlayerMap.Get` (the per-pose dispatch in
  `OnFrameInvoke`) is a lock-free read on modern .NET — fine to keep on the hot path.

## Suggested order of work

1. **Task 1** (held-item reflection) and **Task 2** (collider render scan) — the two that scale with
   on-screen player/entity count; highest payoff in a crowd.
2. **Task 3** (cache `Animatable`) — same theme, smaller, batches with Task 1.
3. **Tasks 4–5** — local-player / micro cleanups, batchable into one PR.
