# Player damage resistance stats

Combat Overhaul reads these character stats for piercing, slashing, and blunt damage. A content mod can supply them through traits in `assets/<modid>/config/traits.json`; no damage event handler is required.

## Stat codes

| Scope | Percentage factor | Tier reduction |
| --- | --- | --- |
| Full body | `playerDamageFactor` | `playerDamageTierReduction` |
| Slashing | `playerSlashingDamageFactor` | `playerSlashingDamageTierReduction` |
| Piercing | `playerPiercingDamageFactor` | `playerPiercingDamageTierReduction` |
| Blunt | `playerBluntDamageFactor` | `playerBluntDamageTierReduction` |
| Head | `playerHeadDamageFactor` | `playerHeadDamageTierReduction` |
| Face | `playerFaceDamageFactor` | `playerFaceDamageTierReduction` |
| Neck | `playerNeckDamageFactor` | `playerNeckDamageTierReduction` |
| Torso | `playerTorsoDamageFactor` | `playerTorsoDamageTierReduction` |
| Arms | `playerArmsDamageFactor` | `playerArmsDamageTierReduction` |
| Hands | `playerHandsDamageFactor` | `playerHandsDamageTierReduction` |
| Legs | `playerLegsDamageFactor` | `playerLegsDamageTierReduction` |
| Feet | `playerFeetDamageFactor` | `playerFeetDamageTierReduction` |

Percentage attributes use normal Vintage Story factor semantics: `-0.25` means 25% less damage and `0.25` means 25% more damage. Full-body, damage-type, and body-zone factors multiply.

Tier-reduction attributes use positive integers: `1` removes one incoming damage tier. Full-body, damage-type, and body-zone reductions add, then the resulting damage tier is clamped to zero. Tier reduction happens before blocking and armor; percentage factors apply to the damage remaining after blocking and armor.

Arms, hands, legs, and feet cover both left and right body parts. These stats apply only to CO's processed physical damage types: slashing, piercing, and blunt.

## Trait example

```json
[
  {
    "code": "warded",
    "type": "positive",
    "attributes": {
      "playerDamageFactor": -0.1,
      "playerDamageTierReduction": 1
    }
  },
  {
    "code": "thickPlateTraining",
    "type": "positive",
    "attributes": {
      "playerSlashingDamageFactor": -0.25,
      "playerSlashingDamageTierReduction": 1,
      "playerTorsoDamageFactor": -0.2,
      "playerTorsoDamageTierReduction": 1
    }
  }
]
```

The first trait reduces all processed physical damage by 10% and its tier by one. For a slashing torso hit, the second trait multiplies damage by `0.75 * 0.8 = 0.6` and reduces its tier by two.
