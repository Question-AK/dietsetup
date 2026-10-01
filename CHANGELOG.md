# Changelog

## Unreleased

- The nutrition health bonus can no longer exceed 12.5. A bar above the stomach size counts as full, not more.
- Changing race or stomach size keeps each nutrition bar's share of the stomach. A Half-Giant with full
  bars who becomes a Human has full Human bars, not five times them. A bar the old diet did not weigh
  starts empty, so switching cannot fill it. Saves from earlier builds have oversized bars cut to full.
- `/dietdiag` shows observed MaxHealth as base, `maxhealthExtraPoints` and all modifiers, and the nutrition
  bonus computed from the current bars. It no longer prints `(not set)` from a property 1.22 never writes.

## 1.0.2-dev.1 — development candidate

- Add the approved Cannibalism meat, Primitive Survival fish-egg/caviar, and crabmeat mappings.
- Give Primitive Survival crabmeat a distinct `shellfish` identity while preserving existing
  generic freshness/fallback responses and upstream Protein nutrition categories.
- Clarify that blood is already a supported Protein source identity; no blood-food classifications
  or dietary rules changed in this candidate.

- Refused and interrupted mouthfuls no longer leave hunger, nutrition or health behind: an eat that
  does not happen now grants nothing, including through A Culinary Artillery's expanded foods.
- Admins can restrict a granted material to named diets. A diet without permission cannot eat it at
  all -- held food, drink in a vessel, a meal or pie containing it, and an expanded food made with it
  are each refused before anything is removed or credited, with a short in-game message. Held items
  say so in their tooltip. This is separate from the Inedible rating, which still means the item is
  eaten for nothing.
- A grant file naming a diet that fails to compile now withdraws its grants instead of leaving a
  material nobody can ever eat, and a grant table that arrives unreadable is refused whole rather
  than dropping the unreadable row.
- `/dietfood` now reports what each portion of a composite food actually contributed instead of its
  share of the whole. A portion a diet refuses reads zero rather than its slice of the total, and the
  portion rows sum back to the composite. No nourishment calculation changed.

## 1.0.1-rc.1 ? candidate, unpublished

- Snapshot of current development for gameplay acceptance; not an approved stable release.
- Include MIT licensing for original work, credits and applicable third-party notices in packages.
- Add safe build/package commands, full source identity and immutable Release ZIPs.
- Document AI-assisted development, existing-save limitations and the development/stable release workflow.
