using System;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Plugin.Rewind.Data;

/// <summary>
/// The plugin-owned database.
/// </summary>
public class RewindDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RewindDbContext"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public RewindDbContext(DbContextOptions<RewindDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Gets the play records.
    /// </summary>
    public DbSet<PlayRecord> Plays => Set<PlayRecord>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Timestamps are stored as UTC ticks: compact, exactly ordered, and compared as integers.
        var utc = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, long>(
            value => value.ToUniversalTime().Ticks,
            value => new DateTime(value, DateTimeKind.Utc));
        var utcOrNull = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, long?>(
            value => value.HasValue ? value.Value.ToUniversalTime().Ticks : null,
            value => value.HasValue ? new DateTime(value.Value, DateTimeKind.Utc) : null);

        modelBuilder.Entity<PlayRecord>(entity =>
        {
            entity.ToTable("Plays");
            entity.HasKey(play => play.Id);
            entity.Property(play => play.StartedAt).HasConversion(utc);
            entity.Property(play => play.LastSeenAt).HasConversion(utc);
            entity.Property(play => play.UpdatedAt).HasConversion(utc);
            entity.Property(play => play.EndedAt).HasConversion(utcOrNull);
            entity.Property(play => play.Status).HasConversion<string>();
            entity.Property(play => play.Source).HasConversion<string>();

            entity.HasIndex(play => new { play.UserId, play.StartedAt, play.Id });
            entity.HasIndex(play => new { play.UserId, play.Seq });
            entity.HasIndex(play => new { play.UserId, play.ItemType });
            entity.HasIndex(play => new { play.UserId, play.ItemId });
            entity.HasIndex(play => new { play.UserId, play.SeriesId });
            entity.HasIndex(play => play.SessionKey);
            entity.HasIndex(play => new { play.Status, play.LastSeenAt });
            entity.HasIndex(play => play.Seq).IsUnique();
        });
    }
}
