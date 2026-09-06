using System;

namespace dietsetup;

internal static class DietMealFactsContext
{
    // Singleplayer renders client tooltips while the server can be consuming food on another thread.
    [ThreadStatic] public static bool DisplayOnly;
}
