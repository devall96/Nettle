package io.github.devall96.nettle.tv.vm

import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import io.github.devall96.nettle.core.models.SeerrMediaStatus
import io.github.devall96.nettle.core.models.SeerrPage
import io.github.devall96.nettle.core.models.SeerrResult
import io.github.devall96.nettle.tv.AppServices
import kotlinx.coroutines.*
import java.time.OffsetDateTime
import java.time.format.DateTimeFormatter
import java.util.Locale

class SeerrCard(val result: SeerrResult, private val owner: RequestsModel) {
    val title = result.displayTitle
    val subtitle = (if (result.mediaType == "tv") "Series" else "Movie") + (result.year?.let { "  •  $it" } ?: "")
    val posterUrl = AppServices.seerr.imageUrl(result.posterPath, 342)
    var status by mutableStateOf(SeerrMediaStatus.describe(result.mediaInfo?.status))
    var canRequest by mutableStateOf(result.mediaInfo?.status.let { it == null || it == SeerrMediaStatus.UNKNOWN || it == SeerrMediaStatus.PARTIALLY_AVAILABLE })
    var isRequesting by mutableStateOf(false)
    val hasStatus get() = status.isNotEmpty()

    fun request() {
        if (!canRequest || isRequesting) return
        // Series: choose seasons first.
        if (result.mediaType == "tv") owner.openSeasonPicker(this) else owner.scope.launch { submit(null) }
    }

    suspend fun submit(seasons: List<Int>?) {
        isRequesting = true
        try {
            AppServices.seerr.request(result, seasons)
            status = "Requested"
            canRequest = false
            Main.toast(if (!seasons.isNullOrEmpty()) "Requested “$title” (${if (seasons.size == 1) "season " + seasons[0] else "${seasons.size} seasons"})" else "Requested “$title”")
            owner.scope.launch { owner.loadMyRequests() }
        } catch (e: CancellationException) { throw e } catch (e: Exception) {
            Main.toast("Request failed: ${e.message}", 4000)
        } finally { isRequesting = false }
    }
}

class SeasonOption(val number: Int, val name: String, val detail: String, val canSelect: Boolean) {
    var isSelected by mutableStateOf(false)
}

/** Season picker shown before requesting a series. */
class SeasonPicker(private val card: SeerrCard, val seasons: List<SeasonOption>, private val close: () -> Unit, private val scope: CoroutineScope) {
    val title get() = card.title
    val hasSelectable get() = seasons.any { it.canSelect }
    val selectedCount get() = seasons.count { it.isSelected }
    val requestText get() = when (selectedCount) { 0 -> "Choose seasons"; 1 -> "Request 1 season"; else -> "Request $selectedCount seasons" }

    fun selectAll() {
        val all = seasons.filter { it.canSelect }.all { it.isSelected }
        seasons.filter { it.canSelect }.forEach { it.isSelected = !all }
    }

    fun request() {
        if (selectedCount == 0) return
        val picked = seasons.filter { it.isSelected }.map { it.number }
        close()
        scope.launch { card.submit(picked) }
    }

    fun cancel() = close()
}

class SeerrRow(val title: String, val items: List<SeerrCard>)
class MyRequest(val title: String, val posterUrl: String?, val status: String, val whenText: String, val key: Int)

/** Port of RequestsViewModel: discover rows, search, my requests, season picker. */
class RequestsModel : Page() {
    override val navKey = "requests"
    val rows = mutableStateListOf<SeerrRow>()
    val searchResults = mutableStateListOf<SeerrCard>()
    val myRequests = mutableStateListOf<MyRequest>()
    var searchText by mutableStateOf(""); private set
    var isSearching by mutableStateOf(false); private set
    var notConfigured by mutableStateOf(false); private set
    var seasonPicker by mutableStateOf<SeasonPicker?>(null); private set
    var isLoadingSeasons by mutableStateOf(false); private set
    private var searchJob: Job? = null
    val listState = io.github.devall96.nettle.tv.vm.tvListState()
    val rowStates = HashMap<String, LazyListState>()
    val myState = io.github.devall96.nettle.tv.vm.tvListState()

    fun setSearch(text: String) {
        searchText = text
        search(text, 350)
    }

    fun clearSearch() = setSearch("")

    /** Opens the season picker for a series (seasons already in the library or requested can't be picked). */
    fun openSeasonPicker(card: SeerrCard) {
        scope.launch {
            isLoadingSeasons = true
            try {
                val tv = AppServices.seerr.tv(card.result.id)
                val status = tv.mediaInfo?.seasons?.associate { it.seasonNumber to it.status } ?: emptyMap()
                val options = (tv.seasons ?: emptyList()).filter { it.seasonNumber > 0 }.sortedBy { it.seasonNumber }.map { s ->
                    val st = status[s.seasonNumber] ?: SeerrMediaStatus.UNKNOWN
                    val can = st == SeerrMediaStatus.UNKNOWN || st < SeerrMediaStatus.PENDING
                    var detail = if (s.episodeCount > 0) "${s.episodeCount} episodes" else ""
                    if (!can) detail = SeerrMediaStatus.describe(st) + if (detail.isNotEmpty()) " · $detail" else ""
                    SeasonOption(s.seasonNumber, s.name?.takeIf { it.isNotBlank() } ?: "Season ${s.seasonNumber}", detail, can)
                }
                if (options.isEmpty()) { card.submit(null); return@launch }
                seasonPicker = SeasonPicker(card, options, { seasonPicker = null }, scope)
            } catch (e: CancellationException) { throw e } catch (e: Exception) {
                Main.toast("Couldn't load seasons: ${e.message}", 4000)
            } finally { isLoadingSeasons = false }
        }
    }

    override fun handleBack(): Boolean {
        if (seasonPicker == null) return false
        seasonPicker = null
        return true
    }

    override suspend fun load() {
        AppServices.seerrReady.join()
        notConfigured = !AppServices.seerr.isConfigured
        if (notConfigured) return
        if (searchText.isNotBlank()) search(searchText, 0)
        run {
            coroutineScope {
                val s = AppServices.seerr
                val trending = async { s.trending() }
                val movies = async { s.popularMovies() }
                val tv = async { s.popularTv() }
                val upcoming = async { s.upcomingMovies() }
                rows.clear()
                add("Trending", trending.await())
                add("Popular Movies", movies.await())
                add("Popular Series", tv.await())
                add("Coming Soon", upcoming.await())
            }
            loadMyRequests()
        }
    }

    private fun add(title: String, page: SeerrPage) {
        val items = page.results.filter { it.mediaType == "movie" || it.mediaType == "tv" || it.mediaType == null }.map { SeerrCard(it, this) }
        if (items.isNotEmpty()) rows += SeerrRow(title, items)
    }

    private companion object {
        /** Title and poster per requested title (they don't change), so My Requests isn't 20 lookups every visit. */
        val detailsCache = java.util.concurrent.ConcurrentHashMap<String, io.github.devall96.nettle.core.models.SeerrDetails>()
    }

    suspend fun loadMyRequests() {
        try {
            val mine = AppServices.seerr.myRequests(20)
            val built = coroutineScope {
                mine.filter { it.media?.tmdbId != null }.take(20).map { r ->
                    async {
                        val cacheKey = "${r.media!!.mediaType}:${r.media!!.tmdbId}"
                        val d = detailsCache[cacheKey] ?: runCatching {
                            if (r.media!!.mediaType == "tv") AppServices.seerr.tv(r.media!!.tmdbId!!) else AppServices.seerr.movie(r.media!!.tmdbId!!)
                        }.getOrNull()?.also { detailsCache[cacheKey] = it }
                        val status = when (r.status) { 1 -> "Pending approval"; 3 -> "Declined"; 4 -> "Failed"; else -> SeerrMediaStatus.describe(r.media?.status) }
                        MyRequest(
                            title = d?.title ?: d?.name ?: "Unknown title",
                            posterUrl = AppServices.seerr.imageUrl(d?.posterPath, 185),
                            status = status.ifEmpty { "Approved" },
                            whenText = r.createdAt?.let { runCatching { OffsetDateTime.parse(it).atZoneSameInstant(java.time.ZoneId.systemDefault()).format(DateTimeFormatter.ofPattern("MMM d", Locale.US)) }.getOrNull() } ?: "",
                            key = r.id,
                        )
                    }
                }.awaitAll()
            }
            myRequests.clear()
            myRequests.addAll(built)
        } catch (e: CancellationException) { throw e } catch (_: Exception) { }
    }

    private fun search(text: String, delayMs: Long) {
        searchJob?.cancel()
        searchJob = scope.launch {
            delay(delayMs)
            isSearching = text.isNotBlank()
            searchResults.clear()
            if (!isSearching || notConfigured) return@launch
            run {
                val page = AppServices.seerr.search(text.trim(), 1)
                searchResults.addAll(page.results.filter { it.mediaType == "movie" || it.mediaType == "tv" }.map { SeerrCard(it, this@RequestsModel) })
            }
        }
    }

    fun openSettings() = Main.shell?.navigateRoot(SettingsModel())
}
