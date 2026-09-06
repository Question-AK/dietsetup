namespace dietsetup.Diet;

public static class DietProfileRegistry
{
    public static int PeekNutritionMultiplierQueueCount(long entityId) =>
        DietConsumption.Current is { } operation && operation.Entity.EntityId == entityId ? operation.Pending.Count : 0;

    public static void RemoveNutritionMultiplierQueue(long entityId)
    {
        if (DietConsumption.Current is { } operation && operation.Entity.EntityId == entityId) operation.Pending.Clear();
    }
}
