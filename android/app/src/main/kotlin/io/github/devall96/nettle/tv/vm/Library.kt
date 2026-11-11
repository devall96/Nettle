package io.github.devall96.nettle.tv.vm

import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import io.github.devall96.nettle.core.api.ItemQuery
import io.github.devall96.nettle.core.models.BaseItemDto
import io.github.devall96.nettle.tv.AppServices
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import java.time.Year

data class SortOption(val label: String, val sortBy: String, val order: String)
data class FilterOption(val label: String, val filters: String?)
/** A choice in one of the extra filters (genre, decade, quality); value null = no filter. */
data class ChoiceOption(val label: String, val value: String?)

/**
 * Library grid with paging (port of LibraryViewModel). Also used for folders / collections and
 * for a person's filmography (everything they appear in).
 */
class LibraryModel(private val view: BaseItemDto, private val folderMode: Boolean = false, private val personId: String? = null) : Page() {
    companion object {
        private const val PAGE_SIZE = 120
        private const val RANDOM_BATCH = 300

        /** Everything in the user's libraries that this person is in. */
        fun forPerson(personId: String, name: String) =
            LibraryModel(BaseItemDto(id = personId, name = name, type = "Person"), personId = personId).apply { sort = sortOptions[2] }

        val sortOptions = listOf(
            SortOption("Name", "SortName", "Ascending"),
            SortOption("Date added", "DateCreated,SortName", "Descending"),
            SortOption("Release date", "PremiereDate,ProductionYear,SortName", "Descending"),
            SortOption("Rating", "CommunityRating,SortName", "Descending"),
            SortOption("Runtime", "Runtime,SortName", "Ascending"),
            SortOption("Random", "Random", "Ascending"),
        )
        val filterOptions = listOf(
            FilterOption("All", null), FilterOption("Unwatched", "IsUnplayed"),
            FilterOption("In progress", "IsResumable"), FilterOption("Favorites", "IsFavorite"),
        )
        val qualityOptions = listOf(ChoiceOption("Any quality", null), ChoiceOption("4K", "4k"), ChoiceOption("HD", "hd"), ChoiceOption("SD", "sd"))
        val decadeOptions: List<ChoiceOption> = buildList {
            add(ChoiceOption("Any year", null))
            var d = Year.now().value / 10 * 10
            while (d >= 1920) { add(ChoiceOption("${d}s", (d until d + 10).joinToString(","))); d -= 10 }
            add(ChoiceOption("Before 1920", (1880 until 1920).joinToString(",")))
        }
        /** "#" then A–Z, shown when sorted by name. */
        val letters = listOf("#") + ('A'..'Z').map { it.toString() }
    }


    val viewId: String get() = view.id
    val title: String get() = view.name ?: "Library"
    override val navKey = if (personId != null) "person:$personId" else "lib:" + view.id

    private val cards = ArrayList<MediaCard>()
    val rows = mutableStateListOf<List<MediaCard>>()
    var countText by mutableStateOf(""); private set
    val genreOptions = mutableStateListOf(ChoiceOption("All genres", null))

    var sort by mutableStateOf(sortOptions[0]); private set
    var filter by mutableStateOf(filterOptions[0]); private set
    var genre by mutableStateOf(genreOptions[0]); private set
    var decade by mutableStateOf(decadeOptions[0]); private set
    var quality by mutableStateOf(qualityOptions[0]); private set

    fun changeSort(v: SortOption) { sort = v; reload() }
    fun changeFilter(v: FilterOption) { filter = v; reload() }
    fun changeGenre(v: ChoiceOption) { genre = v; reload() }
    fun changeDecade(v: ChoiceOption) { decade = v; reload() }
    fun changeQuality(v: ChoiceOption) { quality = v; reload() }

    val showLetters get() = sort.sortBy == "SortName"
    /** Genre / decade / quality filters make sense for movie, show and video libraries (and people). */
    val showExtraFilters get() = !folderMode || view.type == "BoxSet"
    val isLandscape get() = personId == null && (view.collectionType == "homevideos" || folderMode && view.type == "Folder")
    val cardWidth get() = if (isLandscape) 300 else 164

    var columns = 6; private set
    private var total = Int.MAX_VALUE
    /** Items fetched from the server so far (some may be hidden, e.g. music), the next page starts here. */
    private var fetched = 0
    private var loadingMore = false
    private var loadJob: Job? = null
    private var generation = 0

    val listState = io.github.devall96.nettle.tv.vm.tvListState()
    /** Row to bring to the top (A–Z jump), consumed by the screen. */
    var scrollToRow by mutableStateOf<Int?>(null)

    override suspend fun load() {
        scope.launch { loadGenres() }
        reloadNow()
    }

    private val includeTypes: String?
        get() = if (personId != null) "Movie,Series" else if (folderMode) null else when (view.collectionType) {
            "movies" -> "Movie"
            "tvshows" -> "Series"
            "boxsets" -> "BoxSet"
            "homevideos" -> "Video,Photo"
            else -> null
        }

    private suspend fun loadGenres() {
        if (!showExtraFilters) return
        runCatching { AppServices.jellyfin.getGenres(if (personId == null) view.id else null, includeTypes) }
            .getOrNull()?.forEach { genreOptions += ChoiceOption(it, it) }
    }

    /** The current filters as a query (no paging). Music is never included. */
    private fun buildQuery() = ItemQuery(
        parentId = if (personId == null) view.id else null,
        personIds = personId,
        includeItemTypes = includeTypes,
        excludeItemTypes = if (includeTypes == null) "MusicAlbum,Audio,MusicArtist,MusicVideo,AudioBook" else null,
        recursive = personId != null || includeTypes != null,
        sortBy = sort.sortBy,
        sortOrder = sort.order,
        filters = filter.filters,
        genres = genre.value,
        years = decade.value,
        is4K = if (quality.value == "4k") true else null,
        isHd = when (quality.value) { "hd" -> true; "sd" -> false; else -> null },
    )

    fun setColumnCount(c: Int) {
        val n = maxOf(1, c)
        if (n == columns && rows.isNotEmpty()) return
        columns = n
        rows.clear()
        appendRows(0)
    }

    private fun reload() { loadJob?.cancel(); loadJob = scope.launch { reloadNow() } }

    private suspend fun reloadNow() {
        generation++
        cards.clear()
        rows.clear()
        total = Int.MAX_VALUE
        fetched = 0
        guarded { loadPage(generation) }
    }

    /** [upTo]: load at least this many items in one request (A–Z jump far down the list). */
    private suspend fun loadPage(gen: Int, upTo: Int = 0) {
        if (cards.size >= total) return
        // The server reshuffles on every request, so random is one larger batch with no further pages.
        val random = sort.sortBy == "Random"
        val first = cards.isEmpty() && fetched == 0
        val limit = if (random) RANDOM_BATCH else maxOf(PAGE_SIZE, upTo - fetched + PAGE_SIZE / 2)
        // The count is known from the first page; later pages skip the server's second (counting) query.
        val q = buildQuery().copy(startIndex = fetched, limit = limit, enableTotalRecordCount = first)
        val res = AppServices.jellyfin.getItems(q)
        if (gen != generation) return
        if (first) total = res.totalRecordCount
        else if (res.items.size < limit) total = fetched + res.items.size // reached the end
        fetched += res.items.size
        val shape = if (isLandscape) CardShape.Landscape else CardShape.Poster
        val start = cards.size
        cards.addAll(MediaCard.visible(res.items).map { MediaCard(it, shape) })
        appendRows(start)
        countText = when (total) { 0 -> "Nothing matches these filters"; 1 -> "1 item"; else -> "%,d items".format(total) }
        if (random && cards.size < total) {
            countText = "%,d of %,d items, picked at random".format(cards.size, total)
            total = cards.size
        }
        if (res.items.isEmpty()) total = cards.size
    }

    private fun appendRows(fromIndex: Int) {
        // Re-flow the last partial row, then add new rows.
        val rowStart = (fromIndex / columns) * columns
        while (rows.size > rowStart / columns) rows.removeAt(rows.size - 1)
        var i = rowStart
        while (i < cards.size) { rows += cards.subList(i, minOf(cards.size, i + columns)).toList(); i += columns }
    }

    /** Infinite scroll trigger from the screen. */
    fun loadMore() {
        if (loadingMore || isBusy || cards.size >= total) return
        loadingMore = true
        val gen = generation
        scope.launch { try { guarded(showBusy = false) { loadPage(gen) } } finally { loadingMore = false } }
    }

    /** Scrolls to the first title starting with [letter] (sorted by name). */
    fun jumpTo(letter: String) {
        if (!showLetters) return
        val gen = generation
        launch(showBusy = false) {
            var index = 0
            if (letter != "#") {
                // How many titles sort before this letter = where it starts.
                index = AppServices.jellyfin.getItems(buildQuery().copy(nameLessThan = letter, limit = 0)).totalRecordCount
            }
            if (sort.order == "Descending") index = maxOf(0, total - index - 1)
            // Everything up to that letter in one request (not page after page).
            if (fetched <= index && cards.size < total && gen == generation) loadPage(gen, upTo = index + 1)
            if (gen != generation || cards.isEmpty()) return@launch
            scrollToRow = minOf(index, cards.size - 1) / maxOf(1, columns)
        }
    }

    // ===================== Play all / Shuffle =====================

    /** The playable things under this page: episodes for shows, otherwise the videos themselves. */
    private val playableTypes: String
        get() = if (personId != null) "Movie,Episode" else when (view.collectionType) {
            "movies" -> "Movie"
            "tvshows" -> "Episode"
            "homevideos" -> "Video"
            else -> "Movie,Episode,Video"
        }

    fun playAll() = playQueue(shuffle = false)
    fun shuffle() = playQueue(shuffle = true)

    private fun playQueue(shuffle: Boolean) = launch(showBusy = false) {
        var q = buildQuery().copy(includeItemTypes = playableTypes, excludeItemTypes = null, recursive = true, isMissing = false)
        q = if (shuffle) q.copy(sortBy = "Random", sortOrder = "Ascending", limit = 300)
        else {
            // Shows play in series order; everything else in the order on screen.
            val s = if (playableTypes == "Episode") q.copy(sortBy = "SeriesSortName,ParentIndexNumber,IndexNumber", sortOrder = "Ascending")
            else if (q.sortBy == "Random") q.copy(sortBy = "SortName", sortOrder = "Ascending") else q
            s.copy(limit = 500)
        }
        val items = AppServices.jellyfin.getItems(q).items.filter { it.isPlayable && !it.isFolder }
        if (items.isEmpty()) Main.toast("Nothing to play here") else Main.playQueue(items)
    }
}
