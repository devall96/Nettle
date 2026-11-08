package io.github.devall96.nettle.tv.vm

import androidx.compose.runtime.Immutable
import io.github.devall96.nettle.core.models.BaseItemDto
import io.github.devall96.nettle.core.models.Music
import io.github.devall96.nettle.tv.AppServices
import java.time.LocalDateTime
import java.time.format.DateTimeFormatter
import java.util.Locale
import kotlin.math.ceil
import kotlin.math.max

enum class CardShape { Poster, Landscape, Square }

/**
 * Pre-computed presentation for one item tile (port of MediaCardViewModel). Everything the card
 * needs is resolved once here so scrolling never does string / URL work.
 */
@Immutable
class MediaCard(val item: BaseItemDto, val shape: CardShape, preferSeriesTitle: Boolean = false, val playOnOpen: Boolean = false) {
    val title: String
    val subtitle: String?
    val imageUrl: String?
    val progress: Float
    val isPlayed: Boolean
    val badge: String?

    val cardWidth: Int get() = when (shape) { CardShape.Landscape -> 300; CardShape.Square -> 180; CardShape.Poster -> 164 }
    val imageHeight: Int get() = when (shape) { CardShape.Landscape -> 169; CardShape.Square -> 180; CardShape.Poster -> 246 }
    val hasProgress get() = progress > 0.005f && progress < 0.995f

    init {
        // Fetch at the card's real pixel width on this TV (1440-unit canvas on a 1920 px UI = 1.33×), in steps of 64.
        val w = (ceil(cardWidth * 1.34 * 1.1 / 64) * 64).toInt()
        if (item.isEpisode) {
            title = if (preferSeriesTitle) item.seriesName ?: item.name ?: "" else item.name ?: ""
            val ep = if (item.parentIndexNumber != null && item.indexNumber != null) "S${item.parentIndexNumber} • E${item.indexNumber}" else null
            subtitle = if (preferSeriesTitle) (if (ep != null) "$ep  ${item.name}" else item.name) else ep
        } else {
            title = item.name ?: ""
            subtitle = item.productionYear?.toString() ?: if (item.type == "CollectionFolder") null else if (item.type == "Series") "Series" else null
        }
        imageUrl = if (shape == CardShape.Landscape) landscapeImage(item, w) else posterImage(item, w)
        val ud = item.userData
        progress = ((ud?.playedPercentage ?: 0.0) / 100.0).toFloat()
        isPlayed = ud?.played == true
        badge = if (item.isSeries && (ud?.unplayedItemCount ?: 0) > 0) ud!!.unplayedItemCount.toString() else null
    }

    /** Everything shown on the card; equal signatures mean the card looks the same. */
    val signature = "${item.id}|$title|$subtitle|$imageUrl|${"%.3f".format(Locale.ROOT, progress)}|$isPlayed|$badge"

    fun open() {
        if (playOnOpen) Main.playItem(item, resume = false) else Main.shell?.openItem(item)
    }

    fun play() = Main.playItem(item, resume = true)

    companion object {
        fun posterImage(i: BaseItemDto, w: Int): String? {
            if (i.isEpisode && i.seriesPrimaryImageTag != null) return Img.of(i.seriesId, "Primary", i.seriesPrimaryImageTag, w)
            i.tag("Primary")?.let { return Img.of(i.id, "Primary", it, w) }
            return null
        }

        fun landscapeImage(i: BaseItemDto, w: Int): String? {
            if (i.isEpisode) i.tag("Primary")?.let { return Img.of(i.id, "Primary", it, w) }
            i.tag("Thumb")?.let { return Img.of(i.id, "Thumb", it, w) }
            if (i.parentThumbItemId != null && i.parentThumbImageTag != null) return Img.of(i.parentThumbItemId, "Thumb", i.parentThumbImageTag, w)
            i.backdropImageTags?.firstOrNull()?.let { return Img.of(i.id, "Backdrop", it, w) }
            if (i.parentBackdropItemId != null) i.parentBackdropImageTags?.firstOrNull()?.let { return Img.of(i.parentBackdropItemId, "Backdrop", it, w) }
            i.tag("Primary")?.let { return Img.of(i.id, "Primary", it, w) }
            return null
        }

        /** Music isn't a feature yet: those items never appear. */
        fun visible(items: List<BaseItemDto>): List<BaseItemDto> = items.filterNot { Music.isHidden(it) }
    }
}

@Immutable
class CardRow(val title: String, val items: List<MediaCard>) {
    val signature = title + "#" + items.joinToString(",") { it.signature }
}

object Format {
    fun runtime(ticks: Long?): String {
        if (ticks == null || ticks <= 0) return ""
        val totalMin = ticks / 600_000_000L
        val h = totalMin / 60
        val m = totalMin % 60
        return if (h >= 1) "${h}h ${m}m" else "${max(1, m)}m"
    }

    fun clock(ms: Long): String {
        val s = max(0, ms / 1000)
        val h = s / 3600
        val m = (s % 3600) / 60
        val sec = s % 60
        return if (h >= 1) "%d:%02d:%02d".format(h, m, sec) else "%d:%02d".format(m, sec)
    }

    private val endsFmt = DateTimeFormatter.ofPattern("h:mm a", Locale.US)

    fun endsAt(ticks: Long?, positionTicks: Long = 0): String {
        if (ticks == null || ticks <= 0) return ""
        val end = LocalDateTime.now().plusNanos((ticks - positionTicks) * 100)
        return "Ends at " + end.format(endsFmt)
    }
}
