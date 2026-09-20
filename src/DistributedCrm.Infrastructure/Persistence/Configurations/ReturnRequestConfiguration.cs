using DistributedCrm.Domain.Entities;
using DistributedCrm.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DistributedCrm.Infrastructure.Persistence.Configurations;

public class ReturnRequestConfiguration : IEntityTypeConfiguration<ReturnRequest>
{
    public void Configure(EntityTypeBuilder<ReturnRequest> builder)
    {
        builder.ToTable("return_requests", t =>
            t.HasCheckConstraint(
                "ck_return_requests_status",
                "status IN ('Pending', 'Processing', 'Approved', 'Rejected', 'Cancelled')"));

        builder.HasKey(r => r.Id);

        builder.Property(r => r.OrderReference)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(r => r.CustomerEmail)
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(r => r.Item)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(r => r.Reason)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(r => r.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .IsRequired();

        builder.Property<uint>("xmin")
            .IsRowVersion()
            .HasColumnName("xmin");
    }
}