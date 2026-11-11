package io.github.devall96.nettle.tv.vm

import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import io.github.devall96.nettle.core.api.ItemQuery
import io.github.devall96.nettle.core.models.*
import io.github.devall96.nettle.tv.AppServices
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.launch
import java.time.OffsetDateTime
import java.time.format.DateTimeFormatter
import java.util.Locale

class SeasonTab(val season: BaseItemDto) {
    val name = season.name ?: "Season ${season.indexNumber}"
}

class EpisodeRow(val item: BaseItemDto) {
    val title = if (item.indexNumber != null) "${item.indexNumber}. ${item.name}" else item.name ?: ""
    val overview = item.overview
    val meta: String = listOfNotNull(
        item.runTimeTicks?.let { Format.runtime(it) },
        item.premiereDate?.let { d -> runCatching { OffsetDateTime.parse(d).format(DateTimeFormatter.ofPattern("MMM d, yyyy", Locale.US)) }.getOrNull() },
    ).joinToString("  •  ")
    val thumbUrl = item.tag("Primary")?.let { Img.of(item.id, "Primary", it, 480) }
    val progress = ((item.userData?.playedPercentage ?: 0.0) / 100.0).toFloat()
    val hasProgress get() = progress > 0.005f && progress < 0.995f
    var isPlayed by mutableStateOf(item.userData?.played == true)

    fun play() = Main.playItem(item, resume = true)

    fun togglePlayed(scope: kotlinx.coroutines.CoroutineScope) {
        isPlayed = !isPlayed
        scope.launch { runCatching { AppServices.jellyfin.setPlayed(item.id, isPlayed) }.onFailure { if (it !is CancellationException) isPlayed = !isPlayed } }
    }
}

class PersonCard(val p: BaseItemPerson) {
    val name = p.name ?: ""
    val role = p.role ?: p.type ?: ""
    val initial = name.take(1)
    val imageUrl = p.primaryImageTag?.let { Img.of(p.id, "Primary", it, 200) }
    /** Opens everything in the libraries this person is in. */
    fun open() { if (!p.id.isNullOrEmpty()) Main.shell?.openPerson(p.id!!, name) }
}

data class MediaVersion(val id: String, val label: String)

/** Port of DetailsViewModel. */
class DetailsModel(private val itemId: String, private val preferSeasonId: String? = null) : Page() {
    var item by mutableStateOf<BaseItemDto?>(null); private set
    var backdropUrl by mutableStateOf<String?>(null); private set
    var logoUrl by mutableStateOf<String?>(null); private set
    var posterUrl by mutableStateOf<String?>(null); private set
    var titleText by mutableStateOf(""); private set
    var subtitleText by mutableStateOf<String?>(null); private set
    var meta by mutableStateOf(""); private set
    var tagline by mutableStateOf<String?>(null); private set
    var overview by mutableStateOf<String?>(null); private set
    var genres by mutableStateOf<String?>(null); private set
    var mediaInfo by mutableStateOf<String?>(null); private set
    var hdrBadge by mutableStateOf<String?>(null); private set
    var hdrWarning by mutableStateOf<String?>(null); private set
    var endsAt by mutableStateOf<String?>(null); private set
    var playLabel by mutableStateOf("Play"); private set
    var isFavorite by mutableStateOf(false); private set
    var isPlayed by mutableStateOf(false); private set
    var progress by mutableStateOf(0f); private set
    var isSeries by mutableStateOf(false); private set
    var isEpisode by mutableStateOf(false); private set
    var selectedSeason by mutableStateOf<SeasonTab?>(null); private set
    var selectedVersion by mutableStateOf<MediaVersion?>(null); private set
    var hasTrailer by mutableStateOf(false); private set

    val seasons = mutableStateListOf<SeasonTab>()
    val episodes = mutableStateListOf<EpisodeRow>()
    val cast = mutableStateListOf<PersonCard>()
    val similar = mutableStateListOf<MediaCard>()
    val versions = mutableStateListOf<MediaVersion>()
    /** Extras stored with the item (behind the scenes, featurettes…); they play directly. */
    val extras = mutableStateListOf<MediaCard>()
    private var trailers: List<BaseItemDto> = emptyList()
    private var playTarget: BaseItemDto? = null
    private var seasonLoad = 0

    val hasLogo get() = logoUrl != null
    val hasProgress get() = progress > 0.005f && progress < 0.995f
    val hasVersions get() = versions.size > 1

    val listState = io.github.devall96.nettle.tv.vm.tvListState()
    val castState = io.github.devall96.nettle.tv.vm.tvListState()
    val extrasState = io.github.devall96.nettle.tv.vm.tvListState()
    val similarState = io.github.devall96.nettle.tv.vm.tvListState()
    val seasonState = io.github.devall96.nettle.tv.vm.tvListState()

    override suspend fun load() = guarded { loadCore(refresh = false) }
    /** "Try again" after the item itself failed to load. */
    fun retry() { scope.launch { load() } }
    // Coming back (e.g. from the player): update watched state and progress, keep the picked season.
    override suspend fun refresh() = guarded(showBusy = false) { loadCore(refresh = true) }

    private suspend fun loadCore(refresh: Boolean) = coroutineScope {
        val jf = AppServices.jellyfin
        val it = jf.getItem(itemId)
        val similarD = if (it.isEpisode || (refresh && similar.isNotEmpty())) null else async { runCatching { jf.getSimilar(it.id, 16) }.getOrNull() }
        // Local trailers and extras only (files on the server; online trailers are never fetched).
        val trailersD = if (refresh || it.isEpisode) null else async { jf.getLocalTrailers(it.id) }
        val extrasD = if (refresh) null else async { jf.getSpecialFeatures(it.id) }
        item = it
        isSeries = it.isSeries
        isEpisode = it.isEpisode

        // Images: episodes borrow series art for the backdrop and logo.
        var bdId: String? = it.id
        var bdTag = it.backdropImageTags?.firstOrNull()
        if (bdTag == null && it.parentBackdropItemId != null) { bdId = it.parentBackdropItemId; bdTag = it.parentBackdropImageTags?.firstOrNull() }
        backdropUrl = bdTag?.let { t -> Img.of(bdId, "Backdrop", t, 1920, 85) }
        logoUrl = it.tag("Logo")?.let { t -> Img.of(it.id, "Logo", t, 700) }
            ?: it.parentLogoImageTag?.let { t -> Img.of(it.parentLogoItemId, "Logo", t, 700) }
        posterUrl = if (it.isEpisode) it.tag("Primary")?.let { t -> Img.of(it.id, "Primary", t, 640) }
        else it.tag("Primary")?.let { t -> Img.of(it.id, "Primary", t, 400) }

        titleText = if (it.isEpisode) it.seriesName ?: it.name ?: "" else it.name ?: ""
        subtitleText = if (it.isEpisode) "S${it.parentIndexNumber} • E${it.indexNumber}  ${it.name}" else if (it.originalTitle != it.name) it.originalTitle else null
        tagline = it.taglines?.firstOrNull()
        overview = it.overview
        genres = it.genres?.takeIf { g -> g.isNotEmpty() }?.joinToString("  ·  ")

        val m = ArrayList<String>()
        it.productionYear?.let { y ->
            val endYear = it.endDate?.take(4)?.toIntOrNull()
            m += if (it.isSeries && endYear != null && endYear != y) "$y–$endYear" else y.toString()
        }
        it.officialRating?.let { r -> m += r }
        it.communityRating?.let { r -> m += "★ " + "%.1f".format(Locale.US, r) }
        it.criticRating?.let { r -> m += "Critics ${r.toInt()}%" }
        if (!it.isSeries && it.runTimeTicks != null) m += Format.runtime(it.runTimeTicks)
        if (it.isSeries && it.status == "Continuing") m += "Continuing"
        meta = m.joinToString("    ")

        isFavorite = it.userData?.isFavorite == true
        isPlayed = it.userData?.played == true
        progress = ((it.userData?.playedPercentage ?: 0.0) / 100.0).toFloat()
        endsAt = if (it.isSeries) null else Format.endsAt(it.runTimeTicks, it.userData?.playbackPositionTicks ?: 0).ifEmpty { null }

        buildMediaInfo(it)

        if (!refresh) {
            cast.clear()
            cast.addAll((it.people ?: emptyList()).filter { p -> p.type in setOf("Actor", "Director", "GuestStar") }.take(24).map { p -> PersonCard(p) })
        }

        if (it.isSeries) loadSeries(it, refresh)
        else {
            playTarget = it
            val pos = it.userData?.playbackPositionTicks ?: 0
            playLabel = if (pos > 0) "Resume " + Format.clock(pos / 10_000) else "Play"
        }

        trailersD?.let { d -> trailers = d.await().filter { t -> t.isPlayable || t.type == "Trailer" }; hasTrailer = trailers.isNotEmpty() }
        extrasD?.let { d ->
            extras.clear()
            extras.addAll(MediaCard.visible(d.await()).map { x -> MediaCard(x, CardShape.Landscape, playOnOpen = true) })
        }
        similarD?.let { d ->
            d.await()?.let { sim ->
                similar.clear()
                similar.addAll(MediaCard.visible(sim.items).map { s -> MediaCard(s, CardShape.Poster) })
            }
        }
    }

    fun playTrailer() {
        if (trailers.isEmpty()) return
        if (trailers.size == 1) Main.playItem(trailers[0], resume = false) else Main.playQueue(trailers)
    }

    /** Every episode of every season, in random order. */
    fun shuffleSeries() {
        val series = item?.takeIf { it.isSeries } ?: return
        launch(showBusy = false) {
            val res = AppServices.jellyfin.getItems(ItemQuery(parentId = series.id, includeItemTypes = "Episode", recursive = true,
                sortBy = "Random", isMissing = false, sortOrder = "Ascending", limit = 500))
            val eps = res.items.filter { it.isEpisode }
            if (eps.isEmpty()) Main.toast("No episodes to shuffle") else Main.playQueue(eps)
        }
    }

    private fun buildMediaInfo(it: BaseItemDto) {
        val keep = selectedVersion?.id
        versions.clear()
        (it.mediaSources ?: emptyList()).forEach { s -> if (s.id != null) versions += MediaVersion(s.id!!, s.name ?: s.container ?: "Version") }
        selectedVersion = versions.firstOrNull { v -> v.id == keep } ?: versions.firstOrNull()
        showMediaInfo(it)
    }

    /** Picking another version (e.g. 4K vs 1080p) updates the format line and HDR badge to match it. */
    fun selectVersion(v: MediaVersion) {
        selectedVersion = v
        item?.let { showMediaInfo(it) }
    }

    private fun showMediaInfo(it: BaseItemDto) {
        val sources = it.mediaSources ?: emptyList()
        val src = sources.firstOrNull { s -> s.id == selectedVersion?.id } ?: sources.firstOrNull()
        val streams = src?.mediaStreams ?: it.mediaStreams ?: emptyList()
        val v = streams.firstOrNull { s -> s.type == "Video" }
        val a = streams.firstOrNull { s -> s.type == "Audio" && s.isDefault } ?: streams.firstOrNull { s -> s.type == "Audio" }
        val parts = ArrayList<String>()
        if (v != null) {
            VideoFormat.resolution(v.width)?.let { r -> parts += r }
            v.codec?.let { c -> parts += c.uppercase() }
            val (label, warning) = VideoFormat.hdrLabel(v.videoRangeType, AppServices.displaySupportsDolbyVision)
            hdrBadge = label
            hdrWarning = warning
        } else { hdrBadge = null; hdrWarning = null }
        if (a != null) {
            parts += a.codec?.uppercase() ?: "AUDIO"
            a.channelLayout?.let { l -> parts += l }
        }
        val subs = streams.count { s -> s.type == "Subtitle" }
        if (subs > 0) parts += if (subs == 1) "1 subtitle" else "$subs subtitles"
        mediaInfo = if (parts.isNotEmpty()) parts.joinToString("  ·  ") + "  ·  Direct Play" else null
    }

    private suspend fun loadSeries(series: BaseItemDto, refresh: Boolean) = coroutineScope {
        val jf = AppServices.jellyfin
        val nextD = async { runCatching { jf.getNextUp(1, series.id).items.firstOrNull() }.getOrNull() }
        val s = jf.getSeasons(series.id)
        val keepSeasonId = if (refresh) selectedSeason?.season?.id else null
        if (seasons.map { it.season.id } != s.items.map { it.id }) {
            seasons.clear()
            seasons.addAll(s.items.map { SeasonTab(it) })
        }
        val next = nextD.await()
        val initial = seasons.firstOrNull { keepSeasonId != null && it.season.id == keepSeasonId }
            ?: seasons.firstOrNull { it.season.id == preferSeasonId }
            ?: seasons.firstOrNull { next != null && it.season.id == next.seasonId }
            ?: seasons.firstOrNull { (it.season.indexNumber ?: 0) > 0 }
            ?: seasons.firstOrNull()
        if (initial != null) loadSeason(initial)
        playTarget = next ?: episodes.firstOrNull()?.item
        playTarget?.let { t ->
            val pos = t.userData?.playbackPositionTicks ?: 0
            val label = "S${t.parentIndexNumber} E${t.indexNumber}"
            playLabel = if (pos > 0) "Resume $label" else if (next != null) "Play $label" else "Play"
        }
    }

    fun selectSeason(tab: SeasonTab) { scope.launch { loadSeason(tab) } }

    private suspend fun loadSeason(tab: SeasonTab) {
        selectedSeason = tab
        val it = item ?: return
        // Only the latest pick may fill the list: quick moves across tabs can answer out of order.
        val version = ++seasonLoad
        try {
            val eps = AppServices.jellyfin.getEpisodes(it.id, tab.season.id)
            if (version != seasonLoad) return
            if (episodes.map { e -> e.item.id } == eps.items.map { e -> e.id }) {
                eps.items.forEachIndexed { i, e -> episodes[i] = EpisodeRow(e) }
            } else {
                episodes.clear()
                episodes.addAll(eps.items.map { e -> EpisodeRow(e) })
            }
        } catch (e: CancellationException) { throw e } catch (e: Exception) { if (version == seasonLoad) error = e.message }
    }

    fun play() {
        val t = playTarget ?: return
        Main.playItem(t, resume = true, mediaSourceId = if (isSeries) null else selectedVersion?.id)
    }

    fun playFromStart() {
        val t = playTarget ?: return
        Main.playItem(t, resume = false, mediaSourceId = if (isSeries) null else selectedVersion?.id)
    }

    fun toggleFavorite() {
        val it = item ?: return
        isFavorite = !isFavorite
        scope.launch { runCatching { AppServices.jellyfin.setFavorite(it.id, isFavorite) }.onFailure { e -> if (e !is CancellationException) isFavorite = !isFavorite } }
    }

    fun togglePlayed() {
        val it = item ?: return
        isPlayed = !isPlayed
        scope.launch {
            try {
                AppServices.jellyfin.setPlayed(it.id, isPlayed)
                if (isSeries) selectedSeason?.let { s -> loadSeason(s) }
            } catch (e: CancellationException) { throw e } catch (_: Exception) { isPlayed = !isPlayed }
        }
    }

    fun goToSeries() {
        val it = item ?: return
        if (it.seriesId != null) Main.shell?.navigate(DetailsModel(it.seriesId!!, it.seasonId))
    }
}
