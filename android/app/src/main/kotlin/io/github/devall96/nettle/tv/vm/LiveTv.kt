package io.github.devall96.nettle.tv.vm

import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import io.github.devall96.nettle.core.models.NtvChannel
import io.github.devall96.nettle.core.models.NtvProgram
import io.github.devall96.nettle.tv.AppServices
import io.github.devall96.nettle.tv.player.PlayerModel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.time.LocalDate
import java.time.format.DateTimeFormatter
import java.util.Locale

/** One guide cell: a program, or a gap with no listing. Times in epoch ms. */
class GuideCell(val row: GuideRow, val program: NtvProgram?, val startMs: Long, val endMs: Long) {
    val isGap get() = program == null
    val title get() = program?.title ?: ""
    /** Formatted once, the first time the cell is drawn (formatting dates is slow on a TV box). */
    val time: String by lazy(LazyThreadSafetyMode.NONE) {
        program?.let { "${PlayerModel.local(it.start).format(Fmt.hm)} – ${PlayerModel.local(it.stop).format(Fmt.hma)}" } ?: ""
    }
    val key = "${row.index}:$startMs"
}

/** Shared formatters: DateTimeFormatter.ofPattern parses its pattern on every call. */
object Fmt {
    val hm: DateTimeFormatter = DateTimeFormatter.ofPattern("h:mm", Locale.US)
    val hma: DateTimeFormatter = DateTimeFormatter.ofPattern("h:mm a", Locale.US)
    val ehma: DateTimeFormatter = DateTimeFormatter.ofPattern("EEE h:mm a", Locale.US)
    val emd: DateTimeFormatter = DateTimeFormatter.ofPattern("EEE MMM d", Locale.US)
    val eemd: DateTimeFormatter = DateTimeFormatter.ofPattern("EEEE, MMM d", Locale.US)
}

class GuideRow(val channel: NtvChannel, val index: Int) {
    val name = channel.name
    val number = channel.number
    val logoUrl = channel.logoUrl
    val initials = if (channel.name.length >= 2) channel.name.substring(0, 2).uppercase() else channel.name
    /** Every cell from the guide start to its end (programs + gaps), in time order. */
    var cells: List<GuideCell> = emptyList()

    /** Cells overlapping [from, to) (binary search; the full list can span days). */
    fun cellsIn(from: Long, to: Long): List<GuideCell> {
        val c = cells
        var lo = 0
        var hi = c.size
        while (lo < hi) { val mid = (lo + hi) ushr 1; if (c[mid].endMs <= from) lo = mid + 1 else hi = mid }
        val out = ArrayList<GuideCell>()
        var i = lo
        while (i < c.size && c[i].startMs < to) { out += c[i]; i++ }
        return out
    }
}

/**
 * NostalgiaTV live guide (port of LiveTvViewModel): a cable-style grid covering everything
 * NostalgiaTV publishes, scrolled horizontally. Rows are virtualized; within each row only the
 * cells around the visible time are composed, so the guide stays fast however far ahead
 * NostalgiaTV schedules. Live channels only, never On Demand.
 */
class LiveTvModel : Page() {
    companion object {
        const val PX_PER_MINUTE = 7f
        const val CHANNEL_COLUMN = 200f
    }

    override val navKey = "livetv"
    val rows = mutableStateListOf<GuideRow>()
    private var channels: List<NtvChannel> = emptyList()
    var notConfigured by mutableStateOf(false); private set
    var windowStart by mutableLongStateOf(0L); private set
    var totalMinutes by mutableStateOf(180); private set
    /** Horizontal scroll of the schedule, in Nettle units from the guide start. */
    var scrollX by mutableFloatStateOf(0f)
    var nowMs by mutableLongStateOf(System.currentTimeMillis()); private set
    var dayLabel by mutableStateOf("Today"); private set
    val listState = io.github.devall96.nettle.tv.vm.tvListState()

    // Info panel for the focused program.
    var infoTitle by mutableStateOf<String?>(null); private set
    var infoSub by mutableStateOf<String?>(null); private set
    var infoTime by mutableStateOf<String?>(null); private set
    var infoDesc by mutableStateOf<String?>(null); private set
    var infoChannel by mutableStateOf<String?>(null); private set
    var infoImage by mutableStateOf<String?>(null); private set
    var infoProgress by mutableStateOf(0f); private set
    var focusedKey by mutableStateOf<String?>(null)

    val gridWidth get() = totalMinutes * PX_PER_MINUTE
    val guideEnd get() = windowStart + totalMinutes * 60_000L

    override suspend fun load() = loadGuide(force = false)

    private suspend fun loadGuide(force: Boolean) {
        notConfigured = !AppServices.ntv.isConfigured
        if (notConfigured) return
        run {
            // Everything NostalgiaTV publishes; cached for a few minutes, so coming back is instant.
            val all = AppServices.ntv.loadGuide(14, force)
            // Channels with nothing scheduled are hidden.
            channels = all.filter { it.programs.isNotEmpty() }
            channels.forEach { AppServices.allowHostOf(it.streamUrl) }
            val built = withContext(Dispatchers.Default) { layout(channels) }
            rows.clear()
            rows.addAll(built)
            scrollX = 0f
            updateDay()
            if (infoTitle == null) rows.firstOrNull()?.let { r -> (r.cellsIn(nowMs, nowMs + 1).firstOrNull() ?: r.cells.firstOrNull())?.let { inspect(it) } }
        }
        // A cached guide that's getting old is refreshed quietly in the background.
        val at = AppServices.ntv.guideLoadedAt
        if (!force && at != null && System.currentTimeMillis() - at > 5 * 60_000) AppServices.scope.launch { runCatching { AppServices.ntv.loadGuide(14, true) } }
    }

    private fun alignDown(t: Long): Long {
        val z = PlayerModel.local(t)
        val aligned = z.withMinute(if (z.minute >= 30) 30 else 0).withSecond(0).withNano(0)
        return aligned.atZone(java.time.ZoneId.systemDefault()).toInstant().toEpochMilli()
    }

    /** From the current half hour to the last program NostalgiaTV has scheduled. */
    private fun layout(chs: List<NtvChannel>): List<GuideRow> {
        val start = alignDown(System.currentTimeMillis())
        val lastStop = chs.flatMap { it.programs }.maxOfOrNull { it.stop } ?: (start + 3 * 3600_000)
        var end = alignDown(lastStop + 29 * 60_000)
        if (end <= start + 3 * 3600_000) end = start + 3 * 3600_000
        windowStart = start
        totalMinutes = ((end - start) / 60_000).toInt()
        return chs.mapIndexed { i, ch ->
            val row = GuideRow(ch, i)
            val cells = ArrayList<GuideCell>()
            var cursor = start
            for (p in ch.programs) {
                if (p.stop <= start) continue
                if (p.start >= end) break
                val s = maxOf(p.start, start)
                val e = minOf(p.stop, end)
                if (s > cursor) cells += GuideCell(row, null, cursor, s)
                if (e > s) cells += GuideCell(row, p, s, e)
                cursor = maxOf(cursor, e)
            }
            if (cursor < end) cells += GuideCell(row, null, cursor, end)
            row.cells = cells
            row
        }
    }

    /** The 30-minute time labels across the top. */
    fun slotLabel(m: Int): String {
        val t = PlayerModel.local(windowStart + m * 60_000L)
        // Midnight gets the date so it's clear when the day changes.
        return if (t.hour == 0 && t.minute == 0) t.format(Fmt.emd) else t.format(Fmt.hma)
    }

    fun xOf(ms: Long): Float = (ms - windowStart) / 60_000f * PX_PER_MINUTE
    fun msAt(x: Float): Long = windowStart + (x / PX_PER_MINUTE * 60_000).toLong()

    private fun updateDay() {
        val t = PlayerModel.local(msAt(scrollX) + 60_000).toLocalDate()
        dayLabel = when (t) {
            LocalDate.now() -> "Today"
            LocalDate.now().plusDays(1) -> "Tomorrow"
            else -> t.format(Fmt.eemd)
        }
    }

    fun scrollTo(x: Float, viewWidth: Float) {
        scrollX = x.coerceIn(0f, maxOf(0f, gridWidth - viewWidth))
        updateDay()
    }

    fun jumpToNow(viewWidth: Float) = scrollTo(maxOf(0f, xOf(alignDown(System.currentTimeMillis()))), viewWidth)
    fun later(viewWidth: Float) = scrollTo(scrollX + 90 * PX_PER_MINUTE, viewWidth)
    fun earlier(viewWidth: Float) = scrollTo(scrollX - 90 * PX_PER_MINUTE, viewWidth)

    /** A cell got focus: show it in the info panel and keep it in view. */
    fun onCellFocused(c: GuideCell, viewWidth: Float) {
        focusedKey = c.key
        inspect(c)
        val x0 = xOf(c.startMs)
        val x1 = xOf(c.endMs)
        if (x0 < scrollX) scrollTo(maxOf(x0, x1 - viewWidth * 0.8f).coerceAtMost(x0), viewWidth)
        else if (x0 > scrollX + viewWidth - 140) scrollTo(x0 - 60, viewWidth)
    }

    private fun inspect(c: GuideCell) {
        val p = c.program
        infoChannel = (c.row.number?.let { "$it  " } ?: "") + c.row.name
        if (p == null) {
            infoTitle = "No listing"; infoSub = null; infoDesc = null; infoImage = null; infoTime = null; infoProgress = 0f
            return
        }
        infoTitle = p.title
        infoSub = listOfNotNull(p.episodeNum, p.subTitle, p.category).filter { it.isNotEmpty() }.joinToString("  •  ")
        infoTime = "${PlayerModel.local(p.start).format(Fmt.ehma)} – " +
            "${PlayerModel.local(p.stop).format(Fmt.hma)}  (${p.durationMs / 60_000} min)"
        infoDesc = p.description
        infoImage = p.iconUrl ?: c.row.logoUrl
        val now = System.currentTimeMillis()
        infoProgress = if (now >= p.start && now < p.stop) (now - p.start).toFloat() / p.durationMs else 0f
    }

    fun playChannel(row: GuideRow) = Main.playLive(channels, row.index)

    fun openSettings() = Main.shell?.navigateRoot(SettingsModel())
    fun reload() { scope.launch { loadGuide(force = true) } }

    override fun onActivated() {
        scope.launch { while (isActive) { nowMs = System.currentTimeMillis(); delay(30_000) } }
    }
}
