package io.github.devall96.nettle.core.models

import kotlinx.serialization.Serializable

// Subset of the Seerr (Overseerr / Jellyseerr lineage) v1 API. JSON is camelCase.

object SeerrMediaStatus {
    const val UNKNOWN = 1
    const val PENDING = 2
    const val PROCESSING = 3
    const val PARTIALLY_AVAILABLE = 4
    const val AVAILABLE = 5

    fun describe(status: Int?): String = when (status) {
        PENDING -> "Requested"
        PROCESSING -> "Processing"
        PARTIALLY_AVAILABLE -> "Partially available"
        AVAILABLE -> "Available"
        else -> ""
    }
}

@Serializable
data class SeerrMediaInfo(
    val id: Int? = null,
    val tmdbId: Int? = null,
    val mediaType: String? = null,
    val status: Int? = null,
    /** TV only: per-season availability. */
    val seasons: List<SeerrSeasonStatus>? = null,
)

@Serializable
data class SeerrSeasonStatus(val seasonNumber: Int = 0, val status: Int = 0)

@Serializable
data class SeerrResult(
    val id: Int = 0,
    /** movie | tv | person */
    val mediaType: String? = null,
    val title: String? = null,
    val name: String? = null,
    val posterPath: String? = null,
    val backdropPath: String? = null,
    val overview: String? = null,
    val releaseDate: String? = null,
    val firstAirDate: String? = null,
    val voteAverage: Double? = null,
    val mediaInfo: SeerrMediaInfo? = null,
) {
    val displayTitle: String get() = title ?: name ?: ""
    val year: String? get() = (releaseDate ?: firstAirDate)?.takeIf { it.length >= 4 }?.substring(0, 4)
}

@Serializable
data class SeerrPage(
    val page: Int = 0,
    val totalPages: Int = 0,
    val totalResults: Int = 0,
    val results: List<SeerrResult> = emptyList(),
)

@Serializable
data class SeerrSeason(val seasonNumber: Int = 0, val name: String? = null, val episodeCount: Int = 0)

@Serializable
data class SeerrDetails(
    val id: Int = 0,
    val title: String? = null,
    val name: String? = null,
    val posterPath: String? = null,
    val backdropPath: String? = null,
    val overview: String? = null,
    val seasons: List<SeerrSeason>? = null,
    val mediaInfo: SeerrMediaInfo? = null,
)

@Serializable
data class SeerrUserRef(val id: Int = 0, val displayName: String? = null)

@Serializable
data class SeerrRequest(
    val id: Int = 0,
    /** 1 pending approval, 2 approved, 3 declined, 4 failed, 5 completed */
    val status: Int = 0,
    val type: String? = null,
    /** ISO-8601. */
    val createdAt: String? = null,
    val media: SeerrMediaInfo? = null,
    val requestedBy: SeerrUserRef? = null,
)

@Serializable
data class SeerrRequestPage(val results: List<SeerrRequest> = emptyList())

@Serializable
data class SeerrCreateRequest(val mediaType: String = "movie", val mediaId: Int = 0, val seasons: List<Int>? = null)

@Serializable
data class SeerrJellyfinLogin(val username: String? = null, val password: String? = null)

@Serializable
data class SeerrUser(
    val id: Int = 0,
    val displayName: String? = null,
    val username: String? = null,
    val jellyfinUsername: String? = null,
)
