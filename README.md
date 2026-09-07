# Diet Setup

Initial baseline **1.0.0** for Vintage Story **1.22.6 / .NET 10**. Automated checks use the installed 1.22.6 assemblies. Live singleplayer and dedicated-client gameplay acceptance are still required; newer engine versions are not certified by the dependency minimum.

## Install and configure

Install the same ZIP on client and server. Six diets ship: `base`, `human`, `dwarf`, `orc`, `elf`, `goblin`. A fresh install defaults to `base`. RaceFramework is optional; installing it does not automatically bind races to diets. There are no character-setup sliders.

To enable race bindings, copy `ModConfig-examples/bindings.json.example` from the ZIP to the server's `ModConfig/dietsetup/bindings.json` (remove `.example`):

```json
{
  "schemaVersion": 1,
  "bindings": {
    "rf-orc-positive": "orc",
    "rf-dwarf-positive": "dwarf",
    "rf-goblin-positive": "goblin",
    "rf-elf-positive": "elf",
    "rf-human-positive": "human"
  },
  "default": "base"
}
```

Run `/dietreload`, then `/dietdiag` to verify the selected diet and snapshot revision/hash. The server sends effective diets, tags, bindings, config, and grants to clients on join/reload; clients do not need matching local ModConfig files.

For standalone testing without RaceFramework, use an account with `controlserver` privilege:

```text
/dietassignrules goblin
/dietdiag
/dietfood held
```

`/dietassignrules clear` removes the explicit override and returns to bindings/default. An explicit assignment takes precedence over race traits.

## Food and spoilage

One rule wins: priority, number of required tags, then declaration order. Ordinary preference rules multiply vanilla spoilage loss. A rule requiring `fresh`, `spoiled`, or `rotten`, or carrying a satiety/nutrition spoilage curve, explicitly replaces vanilla loss. This preserves ordinary cooked-food decay and the authored Goblin meat/juice responses. No freshness durations or perish transitions are changed.

Meals resolve every nutritious ingredient. Pie fillings use the pie's baked state and age; baking quality remains a separate multiplier. Filled drinks use the consumed liquid's facts. Tooltip queries never enqueue consumption data.

`Inedible` means zero satiety/nutrition contribution; it does not refuse eating. `Harmful` and `Nourishing` are labels, not automatic damage/healing. Explicit consequence effects require confirmed positive consumption. Damage remains per positive eating operation, not prorated to serving size.

Nutrition before the bar cap is:

```text
base satiety ? effective satiety response ? consumed amount ? nutrition response / 2.5 / capacity
```

Meals additionally retain vanilla ingredient/recipe quantity and pie baking factors. Capacity zero gives no nutrition and excludes that category from the weighted health average. Capacity determines reciprocal gain and health weight together; rule multipliers still change the effort needed to fill a bar. Full nonzero-category bars give the normal maximum nutrition health bonus.

## Commands and reporting

- `/dietdiag`: assigned diet, snapshot and current bars.
- `/dietfood held`: per-ingredient facts for held food, including bowls, pies and drinks.
- `/dietfood target`: first occupied slot of a targeted food container.
- `/dietfood last`: last server consumption, including measured category nutrition deltas and credited satiety inputs. Preview facts explicitly say `not consumed`.
- `/dietshow <id>`: compiled rules/capacities.
- `/diettags` and `/dietresolve <id>`: client-side author checks.
- `/dietfactsqueue`: pending nutrition count; normally zero outside consumption.
- Admin: `/dietreload`, `/dietassignrules`, `/dietdrainsatiety`, `/dietsetnutrition`, `/dietrotintake`.

Include the ZIP version and SHA-256, packaged `build-info.json`, startup build identity, `/dietdiag`, `/dietfood held`, and `/dietfood last` in issue reports. State spoilage, consumed quantity, bowl/pie/drink type, other installed mods, and whether the problem happens in singleplayer or on a dedicated server. Outer bowl/pie verdict labels alone are insufficient.

## Server config and authoring

`ModConfig/dietsetup.json` holds:

- `EnableDietSystem` (true): disables diet math, effects, rot intake and this mod's grants when false. `/dietreload` updates it without restarting; vanilla full-stomach nutrition guards are restored.
- `EnableRotIntakeTracking` (true): additional switch for rot tracking while the diet system is enabled.
- `RotIntakePerBite` (0.08), `RotIntakeCap` (1.0), `IntakeHalfLifeHours` (`{"rot":48.0}`): finite accumulator values; half-lives must be positive. The rot half-life must match the consuming RFMechanics configuration.
- `CapacityFloor` (0.05): finite positive floor for nonzero capacities.
- `RecordLastConsumption` (true): stores one last consumption report per connected player.

`TagMultiplierFloor` and `NutritionMultiplierQueueCap` are obsolete and removed. Trait tag-multiplier stats are unsupported. Queues now live only for a consumption operation and are never truncated by a cap.

Whole-file overrides go in `ModConfig/dietsetup/diets/<file>.json`. Unknown authoring fields, invalid numbers, structural nulls, duplicate curve positions, and positions outside 0..1 are rejected with named errors. Rules support only `trigger: "onEat"` (also the default). `drainRate` is reserved and rejected rather than silently ignored. Unmatched fallback multipliers multiply vanilla spoilage; a neutral 1.0 fallback is vanilla passthrough.

Duplicate IDs refuse all conflicting files, even within one domain or among three or more domains. One explicit ModConfig override can resolve an asset collision; duplicate ModConfig overrides are refused too. Inheritance merges categories, replaces fallback as a whole, and appends rules, with cycle/depth checks.

Tag examples and edibility grant examples are included in `ModConfig-examples`. Grants apply only to items without existing nutrition properties. Grant-definition edits require a world restart; the master switch can suspend/re-enable existing grants immediately. Disabled grants restore only properties owned by DietSetup.

## Development and limitations

Build with `dotnet build -c Release`. Set `VINTAGE_STORY` to the game install. Builds do not deploy by default. `Package.ps1 -Dll bin/Release/Mods/dietsetup.dll -Configuration Release` creates a local archive and SHA-256 sidecar. An existing version with different content, including DLL bytes, is refused. Public dev builds must have distinct prerelease versions. Explicit deployment paths are optional.

The hydration patch formerly shipped for Hydrate or Diedrate is removed from game assets. Hydration compatibility is not supported or claimed in this release. Elf alcohol policy, serving-scaled damage, and additional third-party food tags remain deferred. Harmony's meal interception and version-sensitive hunger guard require retesting after game upgrades. A patch-installation failure rolls back this owner's patches and fails startup.

Automated regression checks cover game-assembly eating adapters and simulated client packet handling. They do not replace live tests of dedicated-client join/reload/reconnect, grants, near/full stomach, final items, mixed meals and pies, or gameplay balance. This baseline still requires live gameplay acceptance; a version number does not certify those checks.

## Rot intake integration

DietSetup writes `dietsetup:intake:rot` and `dietsetup:intake:rot:updatedHours` to player WatchedAttributes. The first is a bounded unitless intake; the second is the world-calendar timestamp. Consumers calculate exponential half-life decay from that timestamp. Fresh food contributes zero; failed/cancelled/zero-serving consumption contributes zero. These keys are the RFMechanics integration contract.


## Development and AI use

**I've been a developer for about five years, and for the last two years I've worked closely with advanced AI models.** C# is a language I have much less experience with, so I use LLM coding tools to supplement my knowledge of the language and Vintage Story's modding API.

AI has a substantial role in this project, including generated code, debugging, research and documentation. I direct development, playtest as work progresses, and handle release decisions and maintenance. Not every situation has been tested. The source is available for inspection, contributions and forks.

## Existing worlds

I have not tested adding this mod to an existing world. No new-world requirement is currently known, but compatibility is not guaranteed. **Installing on an existing save is at your own risk. I am not responsible for problems, lost progress or save damage resulting from doing so.** Back up your save and test a separate copy. Keep the original backup: removing the mod does not necessarily undo saved nutrition or other changes.

## Source permissions and release workflow

Original work is MIT-licensed. Fork, modify and redistribute it while retaining the license notices. Third-party material remains under its own terms; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and [CREDITS.md](CREDITS.md). See [RELEASING.md](RELEASING.md) for the development/stable branch workflow and clean candidate build commands.
