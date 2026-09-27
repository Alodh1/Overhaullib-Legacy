# Projectile motion API

Combat Overhaul projectile entities discover server-side entity behaviors that implement
`IProjectileMotionModifier`. Modifiers run in ascending `Priority` order immediately before
normal entity and projectile physics. Existing projectile entities are unchanged when they
do not include a modifier behavior.

The built-in motion behaviors must be placed in the projectile entity's `server.behaviors`
array and require the entity class `CombatOverhaul:Projectile`. Motion is server-authoritative;
the normal CO projectile physics and swept collision handling still perform terrain and entity
collision, damage, and penetration.

## Fixed oval boomerang flight

Add `CombatOverhaul:OvalFlight` to the projectile entity's `server.behaviors` array:

```json
{
  "code": "CombatOverhaul:OvalFlight",
  "MaximumDistance": 8.0,
  "LateralRadius": 5.0,
  "FlightDurationSeconds": 1.2,
  "Direction": 1,
  "CatchAfterProgress": 0.5,
  "CatchCollisionPadding": 0.1
}
```

`OvalFlight` records the launch position and horizontal launch direction, then follows a
fixed horizontal oval relative to them. It does not steer toward or otherwise follow the
owner. With the values above, the path:

- starts at the launch point;
- reaches the first side tip at 4 blocks forward and 5 blocks to the right;
- reaches its maximum forward distance at 8 blocks;
- reaches the other side tip at 4 blocks forward and 5 blocks to the left; and
- completes the loop at the launch point after 1.2 seconds.

The oval is 10 blocks wide from side to side and 8 blocks deep, so its major axis is lateral.
This makes it suitable for striking targets standing side by side in front of the thrower.
`Direction` controls which half is flown first: positive starts to the right, while negative
starts to the left. A value of zero is treated as positive.

| Property | Default | Meaning |
| --- | ---: | --- |
| `MaximumDistance` | `8` | Maximum forward displacement from the launch point, in blocks. Negative values are clamped to zero. |
| `LateralRadius` | `5` | Distance from the centerline to either side tip, in blocks. Negative values are clamped to zero. |
| `FlightDurationSeconds` | `1.2` | Time to complete the loop. Values below `0.1` seconds are clamped to `0.1`. |
| `Direction` | `1` | Positive flies the right half first; negative flies the left half first. |
| `CatchAfterProgress` | `0.5` | Earliest normalized loop progress at which owner collision can catch the projectile. Clamped to `0` through `1`. |
| `CatchCollisionPadding` | `0.1` | Extra padding added around the owner collision box, in addition to the projectile collider radius. Negative values are clamped to zero. |

### Catch behavior

Catching is based on swept collision between the projectile's movement segment and the
owner's current collision box. Proximity alone does not catch it, and the trajectory is not
changed to home toward the owner. A successful catch clones the original projectile stack
into the owner's inventory and despawns the projectile. If the inventory cannot accept the
stack, the catch does not occur.

If the owner moves away from the fixed return path, the projectile misses them. If it reaches
the end without a successful catch, it stops at the launch point.

### Damage and pass-through

`OvalFlight` changes only the projectile's motion. CO's regular swept projectile collision
continues to apply damage around the entire outbound and returning path. Passing through
multiple targets must be enabled by the projectile's normal CO stats, such as sufficient
`PenetrationDistance`; it is not enabled by `OvalFlight` itself.

For a stable horizontal oval, the accompanying `CombatOverhaul:ProjectilePhysics` behavior
normally uses zero air drag and gravity:

```json
{
  "code": "CombatOverhaul:ProjectilePhysics",
  "groundDragFactor": 1,
  "airDragFactor": 0,
  "gravityFactor": 0
}
```

## Curved and owner-seeking flight

Use `CombatOverhaul:CurvedFlight` when the projectile should curve and optionally steer
toward its owner, rather than follow a fixed boomerang loop:

```json
{
  "code": "CombatOverhaul:CurvedFlight",
  "HorizontalTurnRate": 1.2,
  "Direction": 1,
  "StartAfterSeconds": 0,
  "ReturnAfterSeconds": 0.8,
  "ReturnAcceleration": 12,
  "MaximumReturnSpeed": 14,
  "CatchRadius": 1.2,
  "ReturnTargetHeight": 1.2
}
```

`HorizontalTurnRate` is measured in radians per second. Positive `Direction` curves right;
negative curves left. `StartAfterSeconds` delays the outbound curve. When
`ReturnAfterSeconds` is zero or positive, the behavior begins steering toward the owner's
current position at that flight time. A negative value disables the owner-seeking return and
retains only the sideways arc.

`ReturnAcceleration` limits how quickly the velocity turns toward the owner.
`MaximumReturnSpeed` sets the return speed when positive; zero preserves the current speed.
`ReturnTargetHeight` offsets the target above the owner's feet. `CatchRadius` is a proximity
catch radius and zero disables catching. This differs intentionally from `OvalFlight`'s
collision-only catch.

## Automatically starting a shape animation

Declare the animation under `client.animations`, then add `CombatOverhaul:AutoAnimation` to
the projectile entity's `client.behaviors` array:

```json
{
  "code": "CombatOverhaul:AutoAnimation",
  "Animation": "spin",
  "StopWhenStuck": true,
  "MinimumSpeed": 0.01
}
```

| Property | Default | Meaning |
| --- | ---: | --- |
| `Animation` | `""` | Code of the animation declared under `client.animations`. An empty value disables the behavior. |
| `StopWhenStuck` | `true` | Stops the animation after a CO projectile becomes stuck. |
| `MinimumSpeed` | `0` | Stops the animation below this speed. Zero disables the speed check. |

This behavior supports projectile entities derived from base `Entity`, which do not process
`EntityAgent` default-animation triggers. Animation remains client-side and adds no network
traffic. For continuous spinning, the referenced shape animation must also be configured to
repeat, for example with `"onAnimationEnd": "Repeat"`.

## Custom modifiers

Implement `IProjectileMotionModifier` on an entity behavior and register the behavior class
normally. `ProjectileMotionContext` exposes the CO `ProjectileEntity`, its current
`EntityPos`, elapsed `FlightSeconds`, world, shooter, and owner. Change only
`context.Position.Motion`; the existing projectile physics and swept collision systems will
consume the resulting velocity. Motion modifiers are invoked only by the server.
