# Changelog

## 1.0.3-rc.1 — local release candidate (not released)

- Retains the current nutrition, raw-meat, compiler, tooltip and food-voice source for independent release review.
- Adds the missing `rf-halfgiant-positive` binding to the supplied bindings example and documents the explicit
  Half-Giant candidate-diet install. Candidate diets remain inactive unless an administrator copies and selects them.
- Corrects the rot-intake documentation and `/dietrotintake` text: current RF Mechanics responds to literal
  `game:rot` and does not consume Diet Setup's rot-intake attributes.

## 1.0.3-nutrition.2 — local Diet Test candidate (not released)

- Diets can opt into `"overflowNutrition": "proportional"`. A mouthful then gives only the share of its nutrition
  whose satiety fitted in the stomach: with 25 space, a 100-satiety meat gives a quarter of its nutrition. One
  fraction covers every part of the mouthful (meal ingredients, ACA rows, base item), whatever their order. A
  bowl or pie serving already cut to the space keeps all of its nutrition. Items eaten, satiety, healing,
  intoxication and refusals are unchanged. Diets without the field keep whole-item credit. The three
  `nutrition-pilot` diets now opt in.
- A demand-normalised bar on a stomach smaller than 1500 × demand (Elf, Goblin) is no longer emptied early:
  when vanilla empties the bar in one update, the reduced decrement is taken from vanilla's unclipped one.
- A heart is meat. Organ rules exclude meat and fish rules exclude shellfish and egg, so when another mod also
  tags the Cannibalism heart organ, crab fish or fish roe fish, the food keeps Diet Setup's identity. Cannibalism
  hearts and meats, Expanded Foods aged meat and sausages, and Butchering sausages and blood bread gain their
  raw, cooked and preserved states, so state rules such as Orc raw and cooked meat reach them.
- New source tags: Expanded Foods aged meat, fish nuggets, broths and lime eggs; Butchering offal and blood;
  Cannibalism smoked meats and sapient blood; Primitive Survival cooked roe; A Culinary Artillery egg portions.
- `config/food-composition.json` declares recipe-based satiety shares for Expanded Foods sausages and pemmican and
  Butchering blood sausage, black pudding, blood bread and blood dough, which record no per-ingredient provenance.
  Each portion resolves as its own source. These are approximations; each row's note gives the recipe and the
  alternative splits.
- `ModConfig-examples/candidate-diets/` holds inactive candidate diets for all six races.

## 1.0.3-nutrition.1 — local Diet Test candidate (not released)

- Diets can opt into `"nutritionModel": "demandNormalised"`: gain is measured against the race's own food
  demand and an optional `nutritionRequirement`, and bars decay on the Human curve. Diets without the field
  behave as before. Inactive Human, Orc and Half-Giant pilot diets are in `ModConfig-examples/nutrition-pilot/`.
- The nutrition health bonus can no longer exceed 12.5. A bar above the stomach size counts as full, not more.
- Changing race or stomach size keeps each nutrition bar's share of the stomach. A Half-Giant with full
  bars who becomes a Human has full Human bars, not five times them. A bar the old diet did not weigh
  starts empty, so switching cannot fill it. Saves from earlier builds have oversized bars cut to full.
- A mouthful that starts on a full stomach is still eaten where vanilla allows it, but it gives no
  nutrition. One decision covers every part of the mouthful (meal ingredients, ACA rows, base item).
  A mouthful that starts just below full still gives the whole item's nutrition; this is not a strict
  daily nutrition budget.
- When the stomach gets smaller, satiety above the new maximum is removed on the next hunger tick,
  attack or meal. Loading a world removes nothing. A larger stomach adds no satiety.
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
