using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Api.Data;

internal sealed class HistoryEntryConfiguration : IEntityTypeConfiguration<HistoryEntry>
{
    public void Configure(EntityTypeBuilder<HistoryEntry> builder)
    {
        builder.ToTable("History");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedOnAdd();
        builder.Property(h => h.AggregateType).IsRequired().HasMaxLength(200);
        builder.Property(h => h.AggregateId).IsRequired().HasMaxLength(200);
        builder.Property(h => h.EventType).IsRequired().HasMaxLength(200);
        builder.Property(h => h.Payload).IsRequired();
        builder.Property(h => h.Action).IsRequired().HasMaxLength(200);
        builder.Property(h => h.Channel).IsRequired().HasMaxLength(20);
        builder.Property(h => h.Client).HasMaxLength(Actions.ActionCaller.ClientMaxLength);
        builder.Property(h => h.Reason).HasMaxLength(HistoryEntry.ReasonMaxLength);
        builder.Property(h => h.TraceId).HasMaxLength(64);
        builder.HasIndex(h => new { h.AggregateType, h.AggregateId, h.Id });
    }
}
