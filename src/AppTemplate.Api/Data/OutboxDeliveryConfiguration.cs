using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Api.Data;

internal sealed class OutboxDeliveryConfiguration : IEntityTypeConfiguration<OutboxDelivery>
{
    public void Configure(EntityTypeBuilder<OutboxDelivery> builder)
    {
        builder.ToTable("OutboxDeliveries");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedOnAdd();
        builder.HasOne(d => d.HistoryEntry).WithMany().HasForeignKey(d => d.HistoryEntryId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(d => d.Subscriber).IsRequired().HasMaxLength(200);
        builder.Property(d => d.AggregateType).IsRequired().HasMaxLength(200);
        builder.Property(d => d.AggregateId).IsRequired().HasMaxLength(200);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.LastError).HasMaxLength(OutboxDelivery.LastErrorMaxLength);

        // The claim's ordering check, then the claim itself.
        builder.HasIndex(d => new { d.Subscriber, d.AggregateType, d.AggregateId, d.Id });
        builder.HasIndex(d => new { d.Status, d.NextAttemptAt });
    }
}
