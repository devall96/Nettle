package io.github.devall96.nettle.tv.vm

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.focus.FocusRequester
import io.github.devall96.nettle.core.api.ApiException
import io.github.devall96.nettle.tv.AppServices
import kotlinx.coroutines.*

/**
 * Port of ViewModelBase: busy state + friendly error capture. State is Compose snapshot state,
 * written on the main thread; network calls are suspending (OkHttp runs them off the main thread).
 */
abstract class ViewModelBase {
    var isBusy by mutableStateOf(false)
    var error by mutableStateOf<String?>(null)

    /** Cancelled when the screen goes away for good. */
    val scope: CoroutineScope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)

    /** Runs work with busy state + friendly error capture; never throws. */
    protected suspend fun run(showBusy: Boolean = true, work: suspend () -> Unit) {
        if (showBusy) isBusy = true
        error = null
        try { work() }
        catch (e: CancellationException) { throw e }
        catch (e: ApiException) { error = e.message }
        catch (e: Exception) { error = "Something went wrong: ${e.message}" }
        finally { if (showBusy) isBusy = false }
    }

    protected fun launch(showBusy: Boolean = true, work: suspend () -> Unit): Job = scope.launch { run(showBusy, work) }

    open fun dispose() { scope.cancel() }
}

/** A screen inside the signed-in shell (port of PageViewModel). */
abstract class Page : ViewModelBase() {
    /** Which rail entry is highlighted while this page is showing. */
    open val navKey: String = ""
    private var loaded = false

    /** The control that had focus when the page was left, so coming back puts focus there again. */
    var lastFocusKey: String? = null
    val focusRequesters = HashMap<String, FocusRequester>()
    fun requester(key: String): FocusRequester = focusRequesters.getOrPut(key) { FocusRequester() }

    fun activate() {
        scope.launch {
            if (!loaded) { loaded = true; load() } else refresh()
            onActivated()
        }
    }

    /** Back pressed: return true if the page handled it itself (e.g. closed a dialog). */
    open fun handleBack(): Boolean = false

    protected open suspend fun load() {}
    protected open suspend fun refresh() {}
    protected open fun onActivated() {}
    open fun onDeactivated() {}
}

/** A Jellyfin image URL helper shared by cards and pages. */
object Img {
    fun of(itemId: String?, type: String, tag: String?, width: Int, quality: Int = 90): String? =
        AppServices.jellyfin.imageUrl(itemId, type, tag, width, quality)
}

/**
 * Every list on the TV: besides what's on screen, it composes up to one more screen's worth of
 * items ahead (and half a screen behind) while the app is idle, so a D-pad move only has to
 * show cards that are already built instead of building them inside that frame.
 */
@OptIn(androidx.compose.foundation.ExperimentalFoundationApi::class)
fun tvListState(): androidx.compose.foundation.lazy.LazyListState =
    androidx.compose.foundation.lazy.LazyListState(androidx.compose.foundation.lazy.layout.LazyLayoutCacheWindow(1f, 0.5f))
