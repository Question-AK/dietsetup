# Changelog

## 1.2.0 — 7 October 2026

- Food, drinks and meal servings that would give your diet nothing (no satiety, nutrition or other effect) are
  left uneaten, with the message "Your body would take nothing from this." Nothing is used up. Material-permission
  refusals still take priority.
- When a mouthful overfills your stomach and you get less than half of its nutrition, you see "You were too full
  to get much from that."
- The Stats screen shows only the nutrition categories your diet supports, using the server's diet settings.
- Keeps all 1.1.0 nutrition, food-identity and food-mod coverage. Example diets stay inactive: installing this
  version turns on no bindings or balance settings.

Known limitations:

- These messages and the Stats layout have had limited in-game and multiplayer testing.

For Vintage Story 1.22.6.

## 1.1.0 — 4 October 2026

Food tooltips

- Food tooltips now say how a food suits your diet ("A feast to you", "Sits poorly with you") and how its
  freshness changes that ("At its best for you now", "Past its best for you"). The example Elf, Dwarf and
  Goblin diets add lines in each race's voice.

Nutrition

- Diets can opt into `"nutritionModel": "demandNormalised"`: gain is measured against the race's own food
  demand and an optional `nutritionRequirement`, so big eaters are not penalised, and bars decay on the Human
  curve. A bar on a stomach smaller than 1500 × demand (Elf, Goblin) is not emptied early.
- Diets can opt into `"overflowNutrition": "proportional"`: a mouthful that overfills the stomach gives only
  the share of its nutrition whose satiety fitted. A bowl or pie serving already cut to the space keeps all of
  its nutrition. Diets without either field behave as before.
- A mouthful that starts on a full stomach is still eaten where vanilla allows it, but gives no nutrition.
- The nutrition health bonus is capped at 12.5 HP, and a bar above the stomach size counts as full.
- Changing race or stomach size keeps each nutrition bar's share of the stomach. A bar the old diet did not
  weigh starts empty. Saves from earlier builds have oversized bars cut to full.
- When the stomach gets smaller, satiety above the new maximum is removed on the next hunger tick, attack or
  meal. Loading a world removes nothing.

Food identity and food-mod coverage

- A heated pot meal's raw ingredients count as cooked, so a stew's meat is cooked meat. Pies keep the pie's
  state, and mixing-bowl foods stay unheated.
- A heart is meat. When another mod also tags the Cannibalism heart, crab or fish roe, the food keeps Diet
  Setup's identity.
- New source tags and states: Expanded Foods aged meat, sausages, pemmican, fish nuggets, broths and lime eggs;
  Butchering offal, blood, sausages, black pudding and blood bread; Cannibalism hearts, meats and smoked meats;
  Primitive Survival cooked roe; A Culinary Artillery egg portions.
- `config/food-composition.json` declares recipe-based shares for sausages, pemmican, blood sausage, black
  pudding, blood bread and blood dough. These are approximations; each row's note gives the recipe.
- A grant restricted to named diets no longer refuses stews and foods made with that material for other
  diets, where the meal never uses the grant. Food eaten directly is still checked.

Examples and diagnostics

- `ModConfig-examples/candidate-diets/` holds inactive example diets for all six races, including the
  Half-Giant, with documented hunger figures for the Race Framework 1.1.0 races. Orcs and Goblins have
  opt-in raw-meat grants. The bindings example includes `rf-halfgiant-positive`.
- `/dietdiag` shows max health as base, extra points and all modifiers, plus the nutrition bonus.
- Rot-intake documentation and `/dietrotintake` text are corrected: RF Mechanics responds to literal
  `game:rot` and does not read Diet Setup's rot-intake attributes.
- Includes all 1.0.2-dev.1 changes.

Known limitations:

- The example race diets are provisional and inactive; an administrator must copy and select them. Race
  Framework and RF Mechanics stay optional, and installing them does not bind races to diets.
- Food-mod coverage is partial, and the sausage and blood-food compositions are approximations.
- After a race change, the stomach size updates when the world is reloaded.
- Multiplayer reconnect and broad live gameplay testing remain limited.

For Vintage Story 1.22.6.

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

## 1.0.0 - initial baseline, 7 September 2026

- Six configurable diet profiles: base, human, dwarf, elf, orc and goblin.
- Food preferences, nutrition capacities, spoilage responses and ingredient handling for meals, pies and drinks.
- Server-supplied settings, diagnostics and opt-in race bindings; includes the final-item rot-intake fix.
- Hydration integration is not included.
- Includes original-work MIT licensing, third-party notices and source/build identity.

Prepared for client/server deployment before the first ModDB release. Live gameplay and existing-world installation/removal acceptance remain unverified.

## 1.0.1-rc.1 ? candidate, unpublished

- Snapshot of current development for gameplay acceptance; not an approved stable release.
- Include MIT licensing for original work, credits and applicable third-party notices in packages.
- Add safe build/package commands, full source identity and immutable Release ZIPs.
- Document AI-assisted development, existing-save limitations and the development/stable release workflow.
