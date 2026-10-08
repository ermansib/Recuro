using Recuro.BuildingBlocks.Domain;
using Recuro.Vendor.Domain.Vendors;
using VendorEntity = Recuro.Vendor.Domain.Vendors.Vendor;

namespace Recuro.Vendor.UnitTests;

public sealed class VendorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly VendorActor HrHead = new("u-2", "K. Iyer");
    private static readonly FeeBand Band = new(5m, 8.33m);
    private static readonly EmpanelmentGates AllGates = new(true, true, true, true, true, true);
    private static readonly VendorDocuments AllDocuments = new("doc://nda", "doc://agreement", "doc://privacy");

    private static VendorEntity NewVendor(EmpanelmentGates? gates = null, VendorDocuments? documents = null, FeeTerms? fee = null) =>
        VendorEntity.Register(
            "ABC Search",
            VendorType.Consultant,
            fee ?? new FeeTerms(FeeKind.PercentOfCtc, 8.33m, null, "v1"),
            gates ?? AllGates,
            documents ?? AllDocuments,
            HrHead,
            Now);

    [Fact]
    public void A_registered_vendor_is_pending()
    {
        var vendor = NewVendor();

        Assert.Equal(VendorStatus.Pending, vendor.Status);
        Assert.False(vendor.IsActive);
        Assert.Empty(vendor.DomainEvents);
    }

    [Fact]
    public void Empanelment_is_blocked_while_any_of_the_six_gates_is_unchecked()
    {
        var vendor = NewVendor(new EmpanelmentGates(true, false, true, false, true, true), new VendorDocuments("doc://nda", null, "doc://p"));

        var result = vendor.Empanel(Band, HrHead, Now);

        Assert.Equal("empanelment_gates_pending", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.StartsWith("Empanelment blocked — 2 of 6 §15 gates pending, 1 document(s) missing.", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(["gates.trackRecord", "gates.nda", "documents.agreementRef"], result.Error.Fields.Select(f => f.Field));
        Assert.Equal(VendorStatus.Pending, vendor.Status);
    }

    [Fact]
    public void HR_Head_approval_with_every_gate_makes_the_vendor_active()
    {
        var vendor = NewVendor();

        Assert.True(vendor.Empanel(Band, HrHead, Now).IsSuccess);

        Assert.Equal(VendorStatus.Active, vendor.Status);
        Assert.Equal("K. Iyer", vendor.ApprovedBy);
        Assert.Single(vendor.DomainEvents.OfType<VendorEmpanelledDomainEvent>());
        Assert.Equal(ErrorType.Conflict, vendor.Empanel(Band, HrHead, Now).Error!.Type);
    }

    [Theory]
    [InlineData(4.5, false)]
    [InlineData(5, true)]
    [InlineData(8.33, true)]
    [InlineData(9, false)]
    public void Percentage_fees_outside_the_band_need_a_justification(double percent, bool inBand)
    {
        var vendor = NewVendor(fee: new FeeTerms(FeeKind.PercentOfCtc, (decimal)percent, null, "v1"));

        var result = vendor.Empanel(Band, HrHead, Now);

        Assert.Equal(inBand, result.IsSuccess);
        if (!inBand)
        {
            Assert.Equal("justification_required", result.Error!.Fields[0].Code);
        }
    }

    [Fact]
    public void Fixed_fees_per_case_are_not_held_to_the_percentage_band()
    {
        Assert.True(Band.Contains(new FeeTerms(FeeKind.FixedPerCase, 950m, null, "v1")));
    }

    [Fact]
    public void De_empanelment_needs_a_reason_and_switches_an_active_vendor_off()
    {
        var vendor = NewVendor();
        Assert.Equal(ErrorType.Conflict, vendor.DeEmpanel("SLA below benchmark", HrHead, Now).Error!.Type);
        vendor.Empanel(Band, HrHead, Now);

        Assert.Equal("reason_required", vendor.DeEmpanel(" ", HrHead, Now).Error!.Fields[0].Code);
        Assert.True(vendor.DeEmpanel("SLA below benchmark two quarters running", HrHead, Now).IsSuccess);

        Assert.Equal(VendorStatus.Off, vendor.Status);
        Assert.Single(vendor.DomainEvents.OfType<VendorDeEmpanelledDomainEvent>());
        Assert.Equal("vendor_not_pending", vendor.UpdateEmpanelment(AllGates, AllDocuments, vendor.Fee, Now).Error!.Code);
    }
}
