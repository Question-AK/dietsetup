# Optional server ModConfig examples

Copy the desired `.json.example` file into `ModConfig/dietsetup/` and remove `.example`. No examples are activated automatically. Clients receive effective configuration from the server.

- `bindings.json.example`: all five RaceFramework trait mappings and a neutral base default. Run `/dietreload`, then `/dietdiag`.
- `foodtags.json.example`: replace the listed tags' complete pattern arrays, preserving unspecified tags. Run `/dietreload`.
- `food-overrides.json.example`: grant edibility only to non-food collectibles. Definition changes require a world restart. The master switch suspends existing grants.
  `schemaVersion` 2 adds a per-grant `access` block (`unrestricted`, `listed` with at least one diet id, or `denied`), and requires one on every grant. This build parses and validates it but **refuses the whole file**, because nothing enforces a permission at consumption time yet; keep production files on `schemaVersion` 1.

For testing without RaceFramework, an admin can use `/dietassignrules goblin`, `/dietdiag`, and `/dietfood held`. Use `/dietassignrules clear` to restore normal selection.
