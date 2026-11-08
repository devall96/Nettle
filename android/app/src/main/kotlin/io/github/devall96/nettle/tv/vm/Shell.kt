package io.github.devall96.nettle.tv.vm

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.graphics.vector.ImageVector
import io.github.devall96.nettle.core.models.BaseItemDto
import io.github.devall96.nettle.core.models.Music
import io.github.devall96.nettle.tv.AppServices
import io.github.devall96.nettle.tv.theme.Icons
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class NavItem(val key: String, val label: String, val icon: () -> ImageVector, val go: () -> Unit)

/** The signed-in experience: navigation rail + page stack (port of ShellViewModel). */
class Shell : ViewModelBase() {
    private val back = ArrayDeque<Page>()
    var current by mutableStateOf<Page?>(null); private set
    var isAccountMenuOpen by mutableStateOf(false)
    var canGoBack by mutableStateOf(false); private set
    /** Bumped when the account's name / picture may have changed. */
    var accountVersion by mutableStateOf(0); private set

    val home = HomeModel()

    val navTop = listOf(
        NavItem("home", "Home", { Icons.Home }) { navigateRoot(home) },
        NavItem("search", "Search", { Icons.Search }) { navigateRoot(SearchModel()) },
    )
    val navLibraries = mutableStateListOf<NavItem>()
    val navBottom = listOf(
        NavItem("livetv", "Live TV", { Icons.LiveTv }) { navigateRoot(LiveTvModel()) },
        NavItem("requests", "Requests", { Icons.Request }) { navigateRoot(RequestsModel()) },
        NavItem("settings", "Settings", { Icons.Settings }) { navigateRoot(SettingsModel()) },
    )

    val userName get() = AppServices.session.current?.userName ?: ""
    val userInitial get() = userName.take(1).uppercase().ifEmpty { "?" }
    val serverName get() = AppServices.session.current?.serverName ?: ""
    val avatarUrl get() = AppServices.jellyfin.userImageUrl(AppServices.session.current?.userId, AppServices.session.current?.avatarTag, 120)
    val versionText get() = "build ${AppServices.version}"

    fun initialize(prepared: HomeModel.Prepared? = null) {
        // The last visit's libraries and home rows appear at once; fresh ones replace them.
        var fresh = false
        fun showCached(c: HomeModel.Prepared?) {
            if (fresh || c == null || c.snapshot.views.isEmpty()) return
            setNavLibraries(c.snapshot.views)
            home.showCached(c)
        }
        showCached(prepared)
        navigateRoot(home)
        // Signed in from the sign-in screens: the cache is read in the background instead.
        if (prepared == null) scope.launch {
            val id = AppServices.session.current?.id
            showCached(withContext(Dispatchers.Default) { runCatching { AppServices.loadHome(id)?.let { HomeModel.prepare(it) } }.getOrNull() })
        }
        scope.launch {
            try {
                val views = AppServices.jellyfin.getViews()
                fresh = true
                setNavLibraries(views.items)
                home.setLibraries(views.items)
            } catch (e: Exception) {
                if (e is kotlinx.coroutines.CancellationException) throw e
                home.error = home.error ?: e.message
            }
        }
    }

    private fun setNavLibraries(views: List<BaseItemDto>) {
        val shown = views.filter { it.collectionType != "livetv" && it.collectionType != "playlists" && !Music.isHidden(it) }
        if (navLibraries.map { it.key } == shown.map { "lib:" + it.id }) return
        navLibraries.clear()
        for (v in shown) {
            val icon: () -> ImageVector = when (v.collectionType) {
                "movies" -> { { Icons.Movie } }
                "tvshows" -> { { Icons.Tv } }
                else -> { { Icons.Library } }
            }
            navLibraries += NavItem("lib:" + v.id, v.name ?: "Library", icon) { navigateRoot(LibraryModel(v)) }
        }
    }

    /** Name / picture may have changed on the server since the account was saved. */
    fun refreshAccount() { accountVersion++ }

    fun goHome() = navigateRoot(home)

    /** Everything in the libraries with this person in it (actor, director…). */
    fun openPerson(personId: String, name: String) = navigate(LibraryModel.forPerson(personId, name))

    private fun show(page: Page) {
        val old = current
        if (old === page) return
        old?.onDeactivated()
        current = page
        canGoBack = back.isNotEmpty()
        page.activate()
    }

    /** Top-level destination from the rail: resets the back stack. */
    fun navigateRoot(page: Page) {
        val cur = current
        if (cur != null && cur::class == page::class && page !is LibraryModel && page !is HomeModel) return
        if (cur is LibraryModel && page is LibraryModel && cur.viewId == page.viewId) return
        back.forEach { if (it !== home && it !== page) it.dispose() }
        back.clear()
        if (cur != null && cur !== home && cur !== page) { cur.onDeactivated(); cur.dispose() }
        show(page)
    }

    fun navigate(page: Page) {
        current?.let { back.addLast(it) }
        show(page)
    }

    /** Back: closes the page's own popups first, then steps back, then Home, then leaves the app. */
    fun goBack(): Boolean {
        if (isAccountMenuOpen) { isAccountMenuOpen = false; return true }
        val cur = current
        if (cur?.handleBack() == true) return true
        if (back.isNotEmpty()) {
            val prev = back.removeLast()
            cur?.onDeactivated()
            if (cur !== home) cur?.dispose()
            current = prev
            canGoBack = back.isNotEmpty()
            prev.activate()
            return true
        }
        if (cur !== home) { navigateRoot(home); return true }
        return false
    }

    fun openItem(item: BaseItemDto) {
        when (item.type) {
            "CollectionFolder", "UserView" -> navigate(LibraryModel(item))
            "Folder", "BoxSet", "Playlist" -> navigate(LibraryModel(item, folderMode = true))
            "Season" -> navigate(DetailsModel(item.seriesId ?: item.id, preferSeasonId = item.id))
            else -> navigate(DetailsModel(item.id))
        }
    }

    fun refreshCurrent() { current?.activate() }

    fun openSearchWith(query: String) = navigateRoot(SearchModel().apply { updateQuery(query) })

    fun toggleAccountMenu() { isAccountMenuOpen = !isAccountMenuOpen }
    fun switchUser() { isAccountMenuOpen = false; Main.switchUser() }

    fun signOut() {
        isAccountMenuOpen = false
        val acc = AppServices.session.current
        AppServices.socket.stop()
        AppServices.scope.launch {
            if (acc != null) {
                AppServices.session.signOutAndForget(acc)
                AppServices.forgetHome(acc.id)
            }
            Main.showAccountPicker()
        }
    }

    override fun dispose() {
        back.forEach { it.dispose() }
        current?.dispose()
        home.dispose()
        super.dispose()
    }
}
