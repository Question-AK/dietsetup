# Optional server ModConfig examples

Copy the desired `.json.example` file into `ModConfig/dietsetup/` and remove `.example`. No examples are activated automatically. Clients receive effective configuration from the server.

- `bindings.json.example`: all five RaceFramework trait mappings and a neutral base default. Run `/dietreload`, then `/dietdiag`.
- `foodtags.json.example`: replace the listed tags' complete pattern arrays, preserving unspecified tags. Run `/dietreload`.
- `food-overrides.json.example`: grant edibility only to non-food collectibles. Definition changes require a world restart. The master switch suspends existing grants.
  `schemaVersion` 2 adds a per-grant `access` block (`unrestricted`, `listed` with at least one diet id, or `denied`), and requires one on every grant. A diet without permission cannot eat the material at all: the attempt is refused server-side before anything is removed or credited, held items say so in their tooltip, and a bowl, pie, vessel or expanded food holding a denied material is refused whole. Permission is not the `Inedible` verdict, which still means the item is eaten for nothing. Every `listed` diet id must name a diet that actually compiles, or the whole file's grants are withdrawn.

For testing without RaceFramework, an admin can use `/dietassignrules goblin`, `/dietdiag`, and `/dietfood held`. Use `/dietassignrules clear` to restore normal selection.

## Demand-normalised nutrition pilot (inactive)

`nutrition-pilot/` holds Human, Orc and Half-Giant diets that opt into `"nutritionModel": "demandNormalised"` with `"nutritionRequirement": 1.0` and `"overflowNutrition": "proportional"`. Each is the shipped diet (Half-Giant: the `8e2760c` rig diet) with only those three fields added, so capacities, health shares, rules and permissions are unchanged; the Orc pilot also carries the shipped Orc's organ and fish excludes. Diets without the field keep the legacy model.

- Gain is normalised to racial food demand (PlayerModelLib `saturationLossFactor`) and divided by the requirement; capacity sets only each bar's health share and whether it is supported.
- Decay follows the Human 1500-point curve for every opted-in race, independent of the requirement and the racial hunger factor. Loss delays, dairy's half rate, cold, idle, sprint and calendar scaling are kept.
- A diet-level `nutritionRequirement` can be overridden per category (`"categories": { "Protein": { "nutritionRequirement": 1.5 } }`). Values must be finite and positive. A requirement on a legacy diet is refused.
- A mouthful that overflows the stomach credits only the share of its nutrition that fitted (`/dietfood last` shows `overflow=proportional` and the fraction). Delete the `overflowNutrition` line to test whole-item credit instead.

To enable on a test server, copy the three files into `ModConfig/dietsetup/diets/` (replacing any `halfgiant.json` already there), make sure `bindings.json` maps `rf-halfgiant-positive` to `halfgiant`, then restart or run `/dietreload`. `/dietshow orc` reports `nutritionModel=DemandNormalised`, and `/dietdiag` adds the player's demand factor. Remove the files and reload to return to the legacy diets. Existing bars keep their share of the stomach across the switch.

## Candidate racial diets (not active)

`candidate-diets/` holds an opt-in candidate diet for each of the six races. They are proposals for selection, not defaults; see [candidate-diets/README.md](candidate-diets/README.md).
