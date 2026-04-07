namespace Foundatio.Mediator.Tests.Fixtures;

// ── Endpoint test entities ──────────────────────────────────────────────

public record TestItem(string Id, string Name, decimal Price);

// ── Endpoint test messages ──────────────────────────────────────────────

public record GetTestItem(string ItemId);
public record GetTestItems();
public record CreateTestItem(string Name, decimal Price);
public record DeleteTestItem(string ItemId);

// ── Unversioned handler (serves all API versions) ──────────────────────

[HandlerEndpointGroup("Items")]
public class TestItemHandler
{
    private static readonly List<TestItem> Items =
    [
        new("item-1", "Widget", 9.99m),
        new("item-2", "Gadget", 19.99m),
    ];

    /// <summary>Get a single item by ID.</summary>
    [HandlerAllowAnonymous]
    public Result<TestItem> Handle(GetTestItem query)
    {
        var item = Items.FirstOrDefault(i => i.Id == query.ItemId);
        return item is not null ? item : Result.NotFound($"Item {query.ItemId} not found");
    }

    /// <summary>List all items.</summary>
    [HandlerAllowAnonymous]
    public Result<List<TestItem>> Handle(GetTestItems query) => Items.ToList();

    /// <summary>Create a new item (requires Admin role).</summary>
    [HandlerAuthorize(Roles = ["Admin"])]
    public Result<TestItem> Handle(CreateTestItem command)
    {
        var item = new TestItem(Guid.NewGuid().ToString(), command.Name, command.Price);
        return item;
    }

    /// <summary>Delete an item.</summary>
    [HandlerAuthorize]
    public Result Handle(DeleteTestItem command)
    {
        var item = Items.FirstOrDefault(i => i.Id == command.ItemId);
        return item is not null ? Result.NoContent() : Result.NotFound($"Item {command.ItemId} not found");
    }
}

// ── Versioned handlers ─────────────────────────────────────────────────

public record TestWidgetFull(string Id, string Name, string Description, decimal Price);
public record TestWidgetDto(string Id, string Name, decimal Price);

public record GetTestWidget(string WidgetId);
public record GetTestWidgets();

/// <summary>
/// Unversioned widget handler — serves as the fallback for all API versions
/// when no version-specific handler exists.
/// </summary>
[HandlerEndpointGroup("Widgets")]
public class TestWidgetHandler
{
    private static readonly List<TestWidgetFull> Widgets =
    [
        new("w-1", "Alpha", "The alpha widget", 10.00m),
        new("w-2", "Beta", "The beta widget", 20.00m),
    ];

    [HandlerAllowAnonymous]
    public Result<TestWidgetFull> Handle(GetTestWidget query)
    {
        var w = Widgets.FirstOrDefault(w => w.Id == query.WidgetId);
        return w is not null ? w : Result.NotFound($"Widget {query.WidgetId} not found");
    }

    [HandlerAllowAnonymous]
    public Result<List<TestWidgetFull>> Handle(GetTestWidgets query) => Widgets.ToList();
}

// ── All-versioned handlers (no unversioned fallback) ───────────────────

public record TestGadgetV1(string Id, string Name, string Sku);
public record TestGadgetV2(string Id, string Name);

public record GetTestGadget(string GadgetId);

/// <summary>
/// V1 gadget handler — all gadget handlers are explicitly versioned,
/// so there is no unversioned fallback. Tests the "walk declared versions
/// backwards" logic when an invalid version is requested.
/// </summary>
[HandlerEndpointGroup("Gadgets", ApiVersion = "1")]
public class TestGadgetV1Handler
{
    [HandlerAllowAnonymous]
    [HandlerEndpoint(Route = "{gadgetId}")]
    public Result<TestGadgetV1> Handle(GetTestGadget query)
        => new TestGadgetV1(query.GadgetId, "Gadget-V1", "SKU-001");
}

/// <summary>
/// V2 gadget handler — simplified DTO without Sku.
/// </summary>
[HandlerEndpointGroup("Gadgets", ApiVersion = "2")]
public class TestGadgetV2Handler
{
    [HandlerAllowAnonymous]
    [HandlerEndpoint(Route = "{gadgetId}")]
    public Result<TestGadgetV2> Handle(GetTestGadget query)
        => new TestGadgetV2(query.GadgetId, "Gadget-V2");
}

/// <summary>
/// Version 2 widget handler — returns a simplified DTO.
/// </summary>
[HandlerEndpointGroup("Widgets", ApiVersion = "2")]
public class TestWidgetV2Handler
{
    private static readonly List<TestWidgetDto> Widgets =
    [
        new("w-1", "Alpha", 10.00m),
        new("w-2", "Beta", 20.00m),
    ];

    [HandlerAllowAnonymous]
    [HandlerEndpoint(Route = "{widgetId}")]
    public Result<TestWidgetDto> Handle(GetTestWidget query)
    {
        var w = Widgets.FirstOrDefault(w => w.Id == query.WidgetId);
        return w is not null ? w : Result.NotFound($"Widget {query.WidgetId} not found");
    }

    [HandlerAllowAnonymous]
    [HandlerEndpoint(Route = "")]
    public Result<List<TestWidgetDto>> Handle(GetTestWidgets query) => Widgets.ToList();
}
