package io.github.devall96.nettle.tv.vm

import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import io.github.devall96.nettle.core.api.ApiException
import io.github.devall96.nettle.core.api.ItemQuery
import io.github.devall96.nettle.core.models.SeerrMediaStatus
import io.github.devall96.nettle.core.models.SeerrResult
import io.github.devall96.nettle.core.services.Log
import io.github.devall96.nettle.tv.AppServices
import kotlinx.coroutines.*

/** A Seerr search hit that isn't in the library yet; opens it on the Requests page. */
class SeerrSuggestion(val r: SeerrResult) {
    val title = r.displayTitle
    val posterUrl = AppServices.seerr.imageUrl(r.posterPath, 342)
    val status = SeerrMediaStatus.describe(r.mediaInfo?.status).ifEmpty { "Not in your library" }
    fun open() = Main.shell?.navigateRoot(RequestsModel().apply { setSearch(r.displayTitle) })
}

/** Port of SearchViewModel: one row per kind, plus Seerr matches that aren't in the library. */
class SearchModel : Page() {
    override val navKey = "search"
    val results = mutableStateListOf<CardRow>()
    val seerrSuggestions = mutableStateListOf<SeerrSuggestion>()
    var query by mutableStateOf(""); private set
    var noResults by mutableStateOf(false); private set
    val seerrAvailable get() = AppServices.seerr.isConfigured
    private var job: Job? = null
    val listState = io.github.devall96.nettle.tv.vm.tvListState()
    val rowStates = HashMap<String, LazyListState>()
    val seerrState = io.github.devall96.nettle.tv.vm.tvListState()

    private val kinds = listOf(
        Triple("Movies", "Movie", CardShape.Poster),
        Triple("Shows", "Series", CardShape.Poster),
        Triple("Episodes", "Episode", CardShape.Landscape),
        Triple("Collections", "BoxSet", CardShape.Poster),
        Triple("Videos", "Video", CardShape.Landscape),
    )

    fun updateQuery(q: String) {
        if (q == query) return
        query = q
        search(q, 280)
    }

    override suspend fun load() { if (query.isNotBlank()) search(query, 0) }

    private fun search(text: String, delayMs: Long) {
        job?.cancel()
        job = scope.launch {
            delay(delayMs)
            if (text.isBlank()) {
                results.clear(); seerrSuggestions.clear(); noResults = false
                return@launch
            }
            val term = text.trim()
            launch { searchSeerr(term) }
            run {
                // One query per kind, in parallel: a single mixed query lets a common word fill the
                // whole limit with episodes and push every movie and show out of the results.
                val built = ArrayList<CardRow>()
                var failure: ApiException? = null
                var failed = 0
                coroutineScope {
                    val ds = kinds.map { k ->
                        k to async {
                            try { AppServices.jellyfin.getItems(ItemQuery(searchTerm = term, includeItemTypes = k.second, recursive = true, sortBy = null, sortOrder = null, limit = 24)) }
                            catch (e: ApiException) { failure = e; failed++; null }
                        }
                    }
                    for ((k, d) in ds) {
                        val res = d.await() ?: continue // one kind failing shouldn't hide the others
                        val items = MediaCard.visible(res.items.filter { it.type == k.second }).map { MediaCard(it, k.third, true) }
                        if (items.isNotEmpty()) built += CardRow(k.first, items)
                    }
                }
                if (failed == kinds.size && failure != null) throw failure!! // server unreachable: show the error
                results.clear()
                results.addAll(built)
                noResults = results.isEmpty()
            }
        }
    }

    /** Seerr's matches that aren't available yet (only when Seerr is set up). */
    private suspend fun searchSeerr(term: String) {
        if (!AppServices.seerr.isConfigured) return
        try {
            val page = AppServices.seerr.search(term, 1)
            val hits = page.results.filter { (it.mediaType == "movie" || it.mediaType == "tv") && it.mediaInfo?.status != SeerrMediaStatus.AVAILABLE }
                .take(12).map { SeerrSuggestion(it) }
            seerrSuggestions.clear()
            seerrSuggestions.addAll(hits)
        } catch (e: CancellationException) { throw e } catch (e: Exception) { Log.info("Seerr search failed: ${e.message}") }
    }

    fun requestIt() = Main.shell?.navigateRoot(RequestsModel().apply { setSearch(query) })
}
