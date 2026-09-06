# Third-party notices

Original work is licensed under the root MIT license. You may fork, modify and redistribute that work while retaining its copyright and permission notice. Third-party material remains under its own terms; the MIT grant does not relicense it.

## Vintage Story — Anego Studios

The game's API, hunger behavior and food/meal systems are external dependencies. No game DLLs are bundled. The published game source is proprietary readable source, not MIT: [upstream terms](https://github.com/anegostudios/vssurvivalmod/blob/master/license.txt), reproduced in [licenses/VintageStory-source.txt](licenses/VintageStory-source.txt).

The existing licensing exception for `src/DietNutrientHealthBoostPatch.cs` is retained here: any mirrored or adapted vanilla hunger/health logic remains under Vintage Story's mod-source terms. The current implementation uses an independent weighted-category helper and does not retain the old control flow verbatim; moving the notice out of LICENSE does not relicense any inherited game expression.

The eating/meal integration in `src/DietMealEffectFirePatch.cs`, `src/DietMealContentNutritionPatch.cs`, `src/DietPieNutritionPatch.cs` and related hunger/spoilage patches replaces or intercepts game behavior. Any adapted vanilla-source portions remain under those same terms. Independent diet rules, configuration, packet handling and other original implementation remain MIT. Merely calling or patching a game API does not make the entire mod game-owned.

The source terms permit game-mod adaptations subject to the original-work requirement. See CREDITS.md for food-mod research acknowledgements; those mods are not bundled.
