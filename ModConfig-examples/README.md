# Optional server ModConfig examples

Copy the desired `.json.example` file into `ModConfig/dietsetup/` and remove `.example`. No examples are activated automatically. Clients receive effective configuration from the server.

- `bindings.json.example`: all five RaceFramework trait mappings and a neutral base default. Run `/dietreload`, then `/dietdiag`.
- `foodtags.json.example`: replace the listed tags' complete pattern arrays, preserving unspecified tags. Run `/dietreload`.
- `food-overrides.json.example`: grant edibility only to non-food collectibles. Definition changes require a world restart. The master switch suspends existing grants.
  `schemaVersion` 2 adds a per-grant `access` block (`unrestricted`, `listed` with at least one diet id, or `denied`), and requires one on every grant. A diet without permission cannot eat the material at all: the attempt is refused server-side before anything is removed or credited, held items say so in their tooltip, and a bowl, pie, vessel or expanded food holding a denied material is refused whole. Permission is not the `Inedible` verdict, which still means the item is eaten for nothing. Every `listed` diet id must name a diet that actually compiles, or the whole file's grants are withdrawn.

For testing without RaceFramework, an admin can use `/dietassignrules goblin`, `/dietdiag`, and `/dietfood held`. Use `/dietassignrules clear` to restore normal selection.
