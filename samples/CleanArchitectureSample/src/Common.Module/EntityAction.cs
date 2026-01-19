namespace Common.Module;

public class EntityAction<T>
{
    public T Entity { get; init; } = default!;
    public EntityActionType Action { get; init; } = default!;
}

public enum EntityActionType
{
    Create,
    Update,
    Delete
}
