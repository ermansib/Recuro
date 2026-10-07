using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Recuro.BuildingBlocks.Infrastructure.Persistence;

namespace Recuro.Requisition.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef</c> build the model without starting the API.</summary>
internal sealed class DesignTimeRequisitionDbContextFactory : IDesignTimeDbContextFactory<RequisitionDbContext>
{
    public RequisitionDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<RequisitionDbContext>();
        RecuroNpgsql.Configure(builder, "Host=localhost;Database=recuro_requisition", typeof(RequisitionDbContext).Assembly.GetName().Name!);
        return new RequisitionDbContext(builder.Options, RecuroNpgsql.DesignTimeTenant, NoDomainEvents.Instance);
    }
}
