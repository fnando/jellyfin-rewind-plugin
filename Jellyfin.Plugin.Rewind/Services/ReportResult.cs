using Jellyfin.Plugin.Rewind.Data;

namespace Jellyfin.Plugin.Rewind.Services;

/// <summary>
/// The outcome of a <see cref="ReportedPlay"/> and the record it resolved to.
/// </summary>
/// <param name="Outcome">Whether the record is new.</param>
/// <param name="Play">The new or existing record.</param>
public sealed record ReportResult(ReportOutcome Outcome, PlayRecord Play);
