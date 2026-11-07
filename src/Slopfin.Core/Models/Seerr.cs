namespace Slopfin.Core.Models;

// Subset of the Seerr (Overseerr / Jellyseerr lineage) v1 API. JSON is camelCase.

public static class SeerrMediaStatus
{
    public const int Unknown = 1, Pending = 2, Processing = 3, PartiallyAvailable = 4, Available = 5;

    public static string Describe(int? status) => status switch
    {
        Pending => "Requested",
        Processing => "Processing",
        PartiallyAvailable => "Partially available",
        Available => "Available",
        _ => ""
    };
}

public sealed class SeerrMediaInfo
{
    public int? Id { get; set; }
    public int? TmdbId { get; set; }
    public string? MediaType { get; set; }
    public int? Status { get; set; }
}

public sealed class SeerrResult
{
    public int Id { get; set; }
    public string? MediaType { get; set; }  // movie | tv | person
    public string? Title { get; set; }
    public string? Name { get; set; }
    public string? PosterPath { get; set; }
    public string? BackdropPath { get; set; }
    public string? Overview { get; set; }
    public string? ReleaseDate { get; set; }
    public string? FirstAirDate { get; set; }
    public double? VoteAverage { get; set; }
    public SeerrMediaInfo? MediaInfo { get; set; }

    public string DisplayTitle => Title ?? Name ?? "";
    public string? Year
    {
        get
        {
            var d = ReleaseDate ?? FirstAirDate;
            return d is { Length: >= 4 } ? d[..4] : null;
        }
    }
}

public sealed class SeerrPage
{
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int TotalResults { get; set; }
    public List<SeerrResult> Results { get; set; } = new();
}

public sealed class SeerrSeason
{
    public int SeasonNumber { get; set; }
    public string? Name { get; set; }
    public int EpisodeCount { get; set; }
}

public sealed class SeerrDetails
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? Name { get; set; }
    public string? PosterPath { get; set; }
    public string? BackdropPath { get; set; }
    public string? Overview { get; set; }
    public List<SeerrSeason>? Seasons { get; set; }
    public SeerrMediaInfo? MediaInfo { get; set; }
}

public sealed class SeerrUserRef
{
    public int Id { get; set; }
    public string? DisplayName { get; set; }
}

public sealed class SeerrRequest
{
    public int Id { get; set; }
    public int Status { get; set; }   // 1 pending approval, 2 approved, 3 declined, 4 failed, 5 completed
    public string? Type { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public SeerrMediaInfo? Media { get; set; }
    public SeerrUserRef? RequestedBy { get; set; }
}

public sealed class SeerrRequestPage
{
    public List<SeerrRequest> Results { get; set; } = new();
}

public sealed class SeerrCreateRequest
{
    public string MediaType { get; set; } = "movie";
    public int MediaId { get; set; }
    public List<int>? Seasons { get; set; }
}

public sealed class SeerrJellyfinLogin
{
    public string? Username { get; set; }
    public string? Password { get; set; }
}

public sealed class SeerrUser
{
    public int Id { get; set; }
    public string? DisplayName { get; set; }
    public string? Username { get; set; }
    public string? JellyfinUsername { get; set; }
}
