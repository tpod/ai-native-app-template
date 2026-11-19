using AppTemplate.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AppTemplate.Api.Tests.Fixtures.Notes;

public sealed class NoteModelContributor : IModelContributor
{
    public string SchemaSql => """
        CREATE TABLE "Notes" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_Notes" PRIMARY KEY,
            "Text" TEXT NOT NULL,
            "Locked" INTEGER NOT NULL,
            "Version" INTEGER NOT NULL
        );
        """;

    public void Configure(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Note>(b =>
        {
            b.ToTable("Notes");
            b.HasKey(n => n.Id);
            b.Property(n => n.Id)
                .HasConversion(new ValueConverter<NoteId, Guid>(v => v.Value, v => NoteId.FromTrusted(v)))
                .ValueGeneratedNever();
            b.Property(n => n.Text)
                .HasConversion(new ValueConverter<NoteText, string>(v => v.Value, v => NoteText.FromTrusted(v)))
                .IsRequired()
                .HasMaxLength(NoteText.MaxLength);
            b.Property(n => n.Version).IsConcurrencyToken();
            b.Ignore(n => n.DomainEvents);
        });
}
