# Candidate racial diets (inactive)

One opt-in diet per race, for Miles to select from. None is active by default, and none is installed in the Diet
Test rig's active configuration. Every file is a whole-file override that extends `base`, opts into
`"nutritionModel": "demandNormalised"` with `"nutritionRequirement": 1.0`, and uses `"overflowNutrition": "proportional"`.

## Selecting one

1. Copy `<race>.json` to the server's `ModConfig/dietsetup/diets/<race>.json`. For Human, Orc and Half-Giant this
   replaces the nutrition pilot. Bindings stay as they are: each `rf-<race>-positive` trait already maps to `<race>`.
2. Orc only: to let Orcs eat raw meat, also copy `food-overrides.json` to `ModConfig/dietsetup/food-overrides.json`
   (merge its three grants by hand if that file already exists). Grant changes need a world restart.
3. Run `/dietreload` (or restart), then `/dietshow <race>` should report `nutritionModel=DemandNormalised` and
   `overflowNutrition=Proportional`.

To undo, delete the copied file (and the grants) and reload. Bars keep their share of the stomach across the switch.

## Settled rules and provisional values

Settled rules follow the 2026-10-02 race directions or carry over from the shipped diet. Provisional values are
first-pass numbers for play testing.

| Race | Settled | Provisional |
|---|---|---|
| Human | Generalist: every capacity 1, no rules, vanilla spoilage. | — |
| Elf | Meat and organ inedible, as shipped. Fish allowed without the fresh bonus, keeping fish, insect and meat distinct. Spoilage is gradual: each spoiled curve starts at the fresh value, so the first spoil tick is no cliff. Preserved food harmful, as shipped. | Capacities F 1.4, V 1.24, G 0.6, P 0.8, D 0 (shipped). Curve anchors (satiety 1.25 → 0.9 → 0.55 → 0.1 at 0/25/50/100% spoil; fish 1.0 → 0.08). Seed and nut nutrition 1.75. Juice 1.25/1.5. Whether the generic spoiled rule should also carry the `harmful` label. |
| Dwarf | Source × preparation interact: preserved roots, greens, fruit, meat and fish rank above their fresh or cooked forms; raw meat stays poor; fungus and roots are favoured. | Capacities F 0.6, V 0.8, G 1.08, P 1.32, D 1.32. Fungus nutrition 2.5. Preserved root 1.75. Fresh leaf and fruit 0.75/0.75. Raw meat 0.5/0.15. Cooked grain 1.33. Alcohol 1.5/1.5. |
| Goblin | Rot unchanged (1.75/1.5, priority 40). Meat, fish and organ curves replace vanilla spoil loss, so moderately spoiled meat keeps full satiety and gains nutrition. Fresh fruit, leaves and roots harmful, as shipped. Insects nourishing. | Fresh meat nutrition 0.6, rising to 1.3 at 40–70% spoil. Organ curve 1.0 → 1.4. Juice curve 0.2 → 1.1 at 85% spoil. Spoiled satiety 1.0 → 0.5. Fresh satiety 0.7. Insects 1.2/1.5. Grain capacity 0.4. |
| Orc | Raw meat usable at full satiety; cooked meat more nutritious; no fruit or vegetable bar, as shipped; large stomach (3750) gives endurance; dairy carries a reduced health share. | Capacities G 0.6, P 1.6, D 0.5. Raw meat nutrition 0.8, cooked 1.25. Organ 1.2. Fish and insects 0.7. Seeds and nuts 0.25/0.2. Raw-meat grant satieties 100/80/50 (red meat, poultry, bushmeat). |
| Half-Giant | Demand (5.31×) is absorbed by demand normalisation, so bar gain per meal is on the Human scale relative to its own hunger. Agricultural and herding preferences. | Capacities F 0.8, V 0.8, G 1.0, P 1.2, D 1.2. Dairy and grain nutrition 1.25. Root 1.1. Cooked meat 1.0, labelled nourishing. |

Food-mod compatibility rules apply to every candidate: organ rules exclude meat (a heart is meat) and fish rules
exclude shellfish and egg.

## Comparison

Numbers come from the regression harness (`CandidateDiets.Compare`), which runs each candidate through the game's
patched spoilage delegate and Diet Setup's resolver on vanilla food codes. Each cell is satiety multiplier ×
nutrition multiplier; under demand normalisation their product is bar gain per unit of base satiety, so cells
compare across races. "—" marks an unsupported bar. Spoil levels are illustrative, not measured shelf lives.

Holding one bar full takes about 9.4% of a day's satiety at bar gain 1.0 (dairy 4.7%), whatever the race's demand.
The last table divides that by the best bar gain each basket offers and adds up the supported bars: under 100%
means a race can keep every bar full on that basket with room to spare.

Demand uses each race's stomach and PlayerModelLib hunger factor (Human 1500/1.0, Elf 1050/1.0, Dwarf 1950/1.0,
Goblin 1050/1.15, Orc 3750/1.0, Half-Giant 7965/5.31) and a daily Human demand of 2765 satiety.

### Demand

| Race | Stomach | Daily demand (satiety) | Days on a full stomach | Supported bars |
|---|---|---|---|---|
| Human | 1500 | 2765 | 0.54 | Fruit, Vegetable, Grain, Protein, Dairy |
| Elf | 1050 | 2765 | 0.38 | Fruit, Vegetable, Grain, Protein |
| Dwarf | 1950 | 2765 | 0.71 | Fruit, Vegetable, Grain, Protein, Dairy |
| Goblin | 1050 | 3180 | 0.33 | Fruit, Vegetable, Grain, Protein, Dairy |
| Orc | 3750 | 2765 | 1.36 | Grain, Protein, Dairy |
| Half-Giant | 7965 | 14682 | 0.54 | Fruit, Vegetable, Grain, Protein, Dairy |

### Home foods: satiety × nutrition

| Food | Spoil | Bar | Human | Elf | Dwarf | Goblin | Orc | Half-Giant |
|---|---|---|---|---|---|---|---|---|
| redmeat-cooked | 0% | Protein | 1.00 × 1.00 | 0.00 × 0.00 | 1.00 × 1.00 | 1.00 × 0.60 | 1.00 × 1.25 | 1.00 × 1.00 |
| fish-cooked | 0% | Protein | 1.00 × 1.00 | 1.00 × 1.00 | 1.00 × 1.00 | 1.00 × 0.60 | 1.00 × 0.70 | 1.00 × 1.00 |
| bread-spelt-perfect | 0% | Grain | 1.00 × 1.00 | 1.25 × 1.25 | 1.00 × 1.33 | 0.70 × 1.00 | 1.00 × 1.00 | 1.00 × 1.25 |
| vegetable-carrot | 0% | Vegetable | 1.00 × 1.00 | 1.25 × 1.25 | 1.00 × 1.50 | 0.25 × 0.40 | 0.25 × — | 1.00 × 1.10 |
| vegetable-cabbage | 0% | Vegetable | 1.00 × 1.00 | 1.25 × 1.25 | 0.75 × 0.75 | 0.25 × 0.40 | 0.25 × — | 1.00 × 1.00 |
| fruit-apple | 0% | Fruit | 1.00 × 1.00 | 1.25 × 1.25 | 0.75 × 0.75 | 0.25 × 0.40 | 0.25 × — | 1.00 × 1.00 |
| cheese-cheddar-1slice | 0% | Dairy | 1.00 × 1.00 | 1.25 × — | 1.00 × 1.00 | 0.70 × 1.00 | 1.00 × 1.00 | 1.00 × 1.25 |
| juiceportion-apple | 0% | Fruit | 1.00 × 1.00 | 1.25 × 1.50 | 0.75 × 0.75 | 1.00 × 0.20 | 0.25 × — | 1.00 × 1.00 |

### Travel foods: satiety × nutrition

| Food | Spoil | Bar | Human | Elf | Dwarf | Goblin | Orc | Half-Giant |
|---|---|---|---|---|---|---|---|---|
| redmeat-cured | 10% | Protein | 0.90 × 1.00 | 0.00 × 0.00 | 0.90 × 1.50 | 1.00 × 0.87 | 0.90 × 1.00 | 0.90 × 1.00 |
| pickledvegetable-carrot | 10% | Vegetable | 0.90 × 1.00 | 0.68 × 0.60 | 0.90 × 1.75 | 0.23 × 0.40 | 0.23 × — | 0.90 × 1.10 |
| legume-soybean | 10% | Protein | 0.90 × 1.00 | 0.90 × 1.75 | 0.90 × 1.00 | 0.95 × 1.00 | 0.23 × 0.20 | 0.90 × 1.00 |
| bread-spelt-perfect | 30% | Grain | 0.70 × 1.00 | 0.83 × 0.70 | 0.70 × 1.33 | 0.85 × 1.00 | 0.70 × 1.00 | 0.70 × 1.25 |
| fruit-apple | 30% | Fruit | 0.70 × 1.00 | 0.83 × 0.70 | 0.53 × 0.75 | 0.17 × 0.40 | 0.17 × — | 0.70 × 1.00 |
| cheese-cheddar-1slice | 20% | Dairy | 0.80 × 1.00 | 0.97 × — | 0.80 × 1.00 | 0.90 × 1.00 | 0.80 × 1.00 | 0.80 × 1.25 |

### Scarcity foods: satiety × nutrition

| Food | Spoil | Bar | Human | Elf | Dwarf | Goblin | Orc | Half-Giant |
|---|---|---|---|---|---|---|---|---|
| redmeat-raw | 40% | Protein | 0.60 × 1.00 | 0.00 × 0.00 | 0.30 × 0.15 | 1.00 × 1.30 | 0.60 × 0.80 | 0.60 × 1.00 |
| redmeat-cooked | 50% | Protein | 0.50 × 1.00 | 0.00 × 0.00 | 0.50 × 1.00 | 1.00 × 1.30 | 0.50 × 1.25 | 0.50 × 1.00 |
| insect-grub | 0% | Protein | 1.00 × 1.00 | 1.25 × 1.25 | 1.00 × 1.00 | 1.20 × 1.50 | 1.00 × 0.70 | 1.00 × 1.00 |
| mushroom-fieldmushroom-normal | 0% | Vegetable | 1.00 × 1.00 | 1.25 × 1.25 | 1.00 × 2.50 | 1.00 × 1.00 | 0.25 × — | 1.00 × 1.00 |
| vegetable-carrot | 50% | Vegetable | 0.50 × 1.00 | 0.55 × 0.42 | 0.50 × 1.50 | 0.12 × 0.40 | 0.12 × — | 0.50 × 1.10 |
| fruit-apple | 50% | Fruit | 0.50 × 1.00 | 0.55 × 0.42 | 0.38 × 0.75 | 0.12 × 0.40 | 0.12 × — | 0.50 × 1.00 |
| juiceportion-apple | 85% | Fruit | 0.15 × 1.00 | 0.23 × 0.21 | 0.11 × 0.75 | 1.00 × 1.10 | 0.04 × — | 0.15 × 1.00 |

### Sustainable condition

| Race | Scenario | Mean satiety multiplier | Best bar gain F / V / G / P / D | Daily share needed to hold every supported bar |
|---|---|---|---|---|
| Human | home | 1.00 | 1.00 / 1.00 / 1.00 / 1.00 / 1.00 | 42% |
| Human | travel | 0.82 | 0.70 / 0.90 / 0.70 / 0.90 / 0.80 | 54% |
| Human | scarcity | 0.61 | 0.50 / 1.00 / none / 1.00 / none | 38% + no grain/dairy food |
| Elf | home | 1.21 | 1.88 / 1.56 / 1.56 / 1.00 / — | 26% |
| Elf | travel | 0.84 | 0.58 / 0.41 / 0.58 / 1.57 / — | 62% |
| Elf | scarcity | 0.77 | 0.23 / 1.56 / none / 1.56 / — | 53% + no grain food |
| Dwarf | home | 0.91 | 0.56 / 1.50 / 1.33 / 1.00 / 1.00 | 44% |
| Dwarf | travel | 0.79 | 0.39 / 1.57 / 0.93 / 1.35 / 0.80 | 53% |
| Dwarf | scarcity | 0.54 | 0.28 / 2.50 / none / 1.00 / none | 47% + no grain/dairy food |
| Goblin | home | 0.64 | 0.20 / 0.10 / 0.70 / 0.60 / 0.70 | 177% |
| Goblin | travel | 0.68 | 0.07 / 0.09 / 0.85 / 0.95 / 0.90 | 265% |
| Goblin | scarcity | 0.78 | 1.10 / 1.00 / none / 1.80 / none | 23% + no grain/dairy food |
| Orc | home | 0.62 | — / — / 1.00 / 1.25 / 1.00 | 22% |
| Orc | travel | 0.50 | — / — / 0.70 / 0.90 / 0.80 | 30% |
| Orc | scarcity | 0.38 | — / — / none / 0.70 / none | 13% + no grain/dairy food |
| Half-Giant | home | 1.00 | 1.00 / 1.10 / 1.25 / 1.00 / 1.25 | 39% |
| Half-Giant | travel | 0.82 | 0.70 / 0.99 / 0.88 / 0.90 / 1.00 | 49% |
| Half-Giant | scarcity | 0.61 | 0.50 / 1.00 / none / 1.00 / none | 38% + no grain/dairy food |

### Reading the comparison

- Human is the reference: 42% at home, 54% travelling.
- Elf thrives at home (26%) and struggles on preserved travel food (62%); spoiling food declines gradually.
- Dwarf matches Human overall. Its fruit bar is the costly one (fresh fruit 0.75 × 0.75), and preserved roots and
  fungus carry the vegetable bar.
- Goblin cannot hold every bar on human home or travel food (177%, 265%): fresh plants give 0.25 × 0.4. It thrives
  on insects, fungus, spoiling meat and aged juice (23%). That is the intended niche, but a Goblin in a mixed
  settlement will run fruit and vegetable bars low unless it seeks those foods.
- Orc needs only three bars and holds them easily (22% at home); it trades variety for a 1.36-day stomach.
- Half-Giant tracks Human (39% at home) once its 5.31× demand is normalised.
