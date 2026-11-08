package io.github.devall96.nettle.tv.vm

import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import io.github.devall96.nettle.core.models.BaseItemDto
import io.github.devall96.nettle.core.models.HomeSnapshot
import io.github.devall96.nettle.core.models.Music
import io.github.devall96.nettle.tv.AppServices
import kotlinx.coroutines.*
import java.util.Locale

class HeroItem(val item: BaseItemDto) {
    val title = item.name ?: ""
    val overview = item.overview
    val backdropUrl = item.backdropImageTags?.firstOrNull()?.let { Img.of(item.id, "Backdrop", it, 1920, 85) }
    val logoUrl = item.tag("Logo")?.let { Img.of(item.id, "Logo", it, 700) }
    val hasLogo get() = logoUrl != null
    val meta: String = buildList {
        item.productionYear?.let { add(it.toString()) }
        item.officialRating?.let { add(it) }
        item.communityRating?.let { add("★ " + "%.1f".format(Locale.US, it)) }
        if (item.isSeries) add("Series") else if (item.runTimeTicks != null) add(Format.runtime(item.runTimeTicks))
        item.genres?.takeIf { it.isNotEmpty() }?.let { add(it.take(3).joinToString(", ")) }
    }.joinToString("   •   ")

    fun play() { if (item.isPlayable) Main.playItem(item, resume = true) else Main.shell?.openItem(item) }
    fun info() = Main.shell?.openItem(item)
}

/** Port of HomeViewModel: spotlight carousel + rows, painted instantly from the last visit's cache. */
class HomeModel : Page() {
    override val navKey = "home"
    val rows = mutableStateListOf<CardRow>()
    val heroes = mutableStateListOf<HeroItem>()
    var hero by mutableStateOf<HeroItem?>(null); private set
    var heroIndex by mutableStateOf(0); private set
    var isEmpty by mutableStateOf(false); private set
    private var libraries: List<BaseItemDto>? = null
    private var showingCache = false
    private var heroTimer: Job? = null

    /** Scroll positions survive leaving and coming back. */
    val listState = io.github.devall96.nettle.tv.vm.tvListState()
    val rowStates = HashMap<String, LazyListState>()

    fun setLibraries(libs: List<BaseItemDto>) {
        libraries = libs
        scope.launch { reload(quiet = showingCache) }
    }

    /** Paints the last visit's home screen at once (from the cache), before fresh data arrives. */
    fun showCached(prepared: Prepared) {
        if (rows.isNotEmpty() || prepared.snapshot.views.isEmpty()) return
        if (libraries == null) libraries = prepared.snapshot.views
        showingCache = true
        render(prepared, newHeroes = true)
    }

    /** Home's rows built from a snapshot: pure work, done off the main thread. */
    class Prepared(val snapshot: HomeSnapshot, val rows: List<CardRow>, val heroPool: List<BaseItemDto>)

    companion object {
        /** Libraries that get a "Recently added" row: video ones the user hasn't hidden from it in Jellyfin. */
        private fun latestLibraries(libs: List<BaseItemDto>): List<BaseItemDto> {
            val hidden = AppServices.session.latestExcludes.map { it.lowercase() }.toSet()
            return libs.filter { l ->
                l.collectionType in setOf("movies", "tvshows", "homevideos", null, "mixed", "boxsets") && l.id.lowercase() !in hidden && !Music.isHidden(l)
            }
        }

        fun prepare(s: HomeSnapshot): Prepared {
            val fresh = ArrayList<CardRow>()
            val resume = MediaCard.visible(s.resume)
            if (resume.isNotEmpty()) fresh += CardRow("Continue Watching", resume.map { MediaCard(it, CardShape.Landscape, true) })
            val nextUp = MediaCard.visible(s.nextUp)
            if (nextUp.isNotEmpty()) fresh += CardRow("Next Up", nextUp.map { MediaCard(it, CardShape.Landscape, true) })
            val heroPool = ArrayList<BaseItemDto>()
            for (lib in latestLibraries(s.views)) {
                val items = MediaCard.visible(s.latest[lib.id] ?: continue)
                if (items.isEmpty()) continue
                fresh += CardRow("Recently Added in " + lib.name, items.map { MediaCard(it, CardShape.Poster, true) })
                heroPool += items.filter { (it.isMovie || it.isSeries) && !it.backdropImageTags.isNullOrEmpty() }.take(4)
            }
            fresh += CardRow("My Libraries", MediaCard.visible(s.views).filter { it.collectionType != "livetv" }.map { MediaCard(it, CardShape.Landscape) })
            return Prepared(s, fresh, heroPool)
        }
    }

    override suspend fun load() { if (libraries != null) reload(quiet = showingCache) }
    override suspend fun refresh() = reload(quiet = true)

    // Reduce motion: the banner doesn't rotate by itself (arrows still work).
    override fun onActivated() { startHeroTimer() }
    override fun onDeactivated() { heroTimer?.cancel() }

    private fun startHeroTimer() {
        heroTimer?.cancel()
        if (AppServices.settings.current.reduceMotion) return
        heroTimer = scope.launch { while (isActive) { delay(9000); nextHero() } }
    }

    private suspend fun reload(quiet: Boolean = false) {
        val libs = libraries ?: return
        run(showBusy = !quiet && rows.isEmpty()) {
            val jf = AppServices.jellyfin
            coroutineScope {
                val resumeD = async { jf.getResume(16) }
                val nextUpD = async { jf.getNextUp(16) }
                val latestD = latestLibraries(libs).map { l -> l to async { runCatching { jf.getLatest(l.id, 20) }.getOrNull() } }
                val latest = HashMap<String, List<BaseItemDto>>()
                for ((lib, d) in latestD) d.await()?.let { latest[lib.id] = it } // one library failing doesn't hide the rest
                val snapshot = HomeSnapshot(views = libs, resume = resumeD.await().items, nextUp = nextUpD.await().items, latest = latest)
                val prepared = withContext(Dispatchers.Default) { prepare(snapshot) }
                render(prepared, newHeroes = !quiet || heroes.isEmpty() || showingCache)
                showingCache = false
                withContext(Dispatchers.IO) { AppServices.saveHome(AppServices.session.current?.id, snapshot) }
            }
        }
    }

    private fun render(prepared: Prepared, newHeroes: Boolean) {
        val fresh = prepared.rows
        val heroPool = prepared.heroPool

        // Update in place so unchanged rows keep their scroll position and composition.
        if (rows.map { it.signature } != fresh.map { it.signature }) {
            for (i in fresh.indices) {
                if (i < rows.size) { if (rows[i].signature != fresh[i].signature) rows[i] = fresh[i] } else rows += fresh[i]
            }
            while (rows.size > fresh.size) rows.removeAt(rows.size - 1)
        }
        if (newHeroes) {
            heroes.clear()
            heroes.addAll(heroPool.shuffled().take(8).map { HeroItem(it) })
            heroIndex = 0
            hero = heroes.firstOrNull()
        }
        isEmpty = fresh.size <= 1 && heroes.isEmpty()
    }

    fun nextHero() {
        if (heroes.isEmpty()) return
        heroIndex = (heroIndex + 1) % heroes.size
        hero = heroes[heroIndex]
    }

    fun prevHero() {
        if (heroes.isEmpty()) return
        heroIndex = (heroIndex - 1 + heroes.size) % heroes.size
        hero = heroes[heroIndex]
        startHeroTimer()
    }

    /** User moved through the banner: restart the 9 s timer so it doesn't jump while reading. */
    fun touchHero() = startHeroTimer()
}
