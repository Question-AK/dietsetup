# Changelog

## Unreleased

## 1.0.2-beta.1 — first public-beta candidate

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
