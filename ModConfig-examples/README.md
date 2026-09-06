# Optional server ModConfig examples

Copy the desired `.json.example` file into `ModConfig/dietsetup/` and remove `.example`. No examples are activated automatically. Clients receive effective configuration from the server.

- `bindings.json.example`: all five RaceFramework trait mappings and a neutral base default. Run `/dietreload`, then `/dietdiag`.
- `foodtags.json.example`: replace the listed tags' complete pattern arrays, preserving unspecified tags. Run `/dietreload`.
- `food-overrides.json.example`: grant edibility only to non-food collectibles. Definition changes require a world restart. The master switch suspends existing grants.

For testing without RaceFramework, an admin can use `/dietassignrules goblin`, `/dietdiag`, and `/dietfood held`. Use `/dietassignrules clear` to restore normal selection.
