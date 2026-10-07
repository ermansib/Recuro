using Recuro.Admin.Domain.Screens;

namespace Recuro.Admin.UnitTests;

public class ScreenConfigurationTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static ScreenDefinition Mrf(bool canDisable = true) => ScreenDefinition.Create(
        "mrf",
        "S-02",
        "Requisitions",
        "New manpower requisition",
        "Raise demand",
        canDisable,
        1,
        [
            new FieldDefinition("grade", "Grade", FieldDataType.Select, DefaultRequired: true, IsLocked: true, SortOrder: 10),
            new FieldDefinition("location", "Location", FieldDataType.Select, DefaultRequired: true, IsLocked: false, SortOrder: 20),
            new FieldDefinition("band", "Salary band", FieldDataType.Select, DefaultRequired: false, IsLocked: false, SortOrder: 30),
        ]);

    private static ScreenChanges Changes(params TenantFieldSetting[] fields) => new(true, null, null, fields);

    [Fact]
    public void Resolve_without_overrides_uses_product_defaults()
    {
        var screen = ScreenLayout.Resolve(Mrf(), null);

        Assert.True(screen.IsEnabled);
        Assert.Equal("New manpower requisition", screen.Title);
        Assert.Equal(["grade", "location", "band"], screen.Fields.Select(f => f.Key));
        Assert.All(screen.Fields, f => Assert.True(f.IsVisible));
        Assert.Equal("Grade", screen.Fields[0].Label);
    }

    [Fact]
    public void Update_then_resolve_applies_labels_order_visibility_and_header()
    {
        var config = TenantScreenConfiguration.CreateFor(TenantId, "mrf");
        var result = config.Update(Mrf(), new ScreenChanges(
            true,
            "Raise a hiring request",
            "  ",
            [
                new TenantFieldSetting("band", null, IsVisible: false, IsRequired: false, SortOrder: 5),
                new TenantFieldSetting("location", "Branch", IsVisible: true, IsRequired: false, SortOrder: 1),
            ]));

        Assert.True(result.IsSuccess);
        var screen = ScreenLayout.Resolve(Mrf(), config);
        Assert.Equal("Raise a hiring request", screen.Title);
        Assert.Equal("Raise demand", screen.Subtitle);
        Assert.Equal(["location", "band", "grade"], screen.Fields.Select(f => f.Key));
        Assert.Equal("Branch", screen.Fields[0].Label);
        Assert.Equal("Location", screen.Fields[0].DefaultLabel);
        Assert.False(screen.Fields[0].IsRequired);
        Assert.False(screen.Fields[1].IsVisible);
    }

    [Fact]
    public void Locked_fields_cannot_be_hidden()
    {
        var config = TenantScreenConfiguration.CreateFor(TenantId, "mrf");

        var result = config.Update(Mrf(), Changes(new TenantFieldSetting("grade", null, IsVisible: false, IsRequired: false, SortOrder: 1)));

        Assert.Equal("screen.lockedField", result.Error!.Code);
    }

    [Fact]
    public void Required_fields_must_be_visible()
    {
        var config = TenantScreenConfiguration.CreateFor(TenantId, "mrf");

        var result = config.Update(Mrf(), Changes(new TenantFieldSetting("band", null, IsVisible: false, IsRequired: true, SortOrder: 1)));

        Assert.Equal("screen.requiredHidden", result.Error!.Code);
    }

    [Fact]
    public void Unknown_and_duplicate_fields_are_rejected()
    {
        var config = TenantScreenConfiguration.CreateFor(TenantId, "mrf");

        Assert.Equal("screen.unknownField", config.Update(Mrf(), Changes(new TenantFieldSetting("salary", null, true, false, 1))).Error!.Code);
        Assert.Equal("screen.duplicateField", config.Update(Mrf(), Changes(
            new TenantFieldSetting("band", null, true, false, 1),
            new TenantFieldSetting("band", null, true, false, 2))).Error!.Code);
    }

    [Fact]
    public void Core_screens_cannot_be_turned_off()
    {
        var config = TenantScreenConfiguration.CreateFor(TenantId, "mrf");

        var result = config.Update(Mrf(canDisable: false), new ScreenChanges(false, null, null, []));

        Assert.Equal("screen.cannotDisable", result.Error!.Code);
    }

    [Fact]
    public void Labels_longer_than_the_limit_are_rejected()
    {
        var config = TenantScreenConfiguration.CreateFor(TenantId, "mrf");
        var label = new string('x', TenantScreenConfiguration.TextMaxLength + 1);

        var result = config.Update(Mrf(), Changes(new TenantFieldSetting("band", label, true, false, 1)));

        Assert.Equal(ScreenErrors.TextTooLong, result.Error);
    }
}
