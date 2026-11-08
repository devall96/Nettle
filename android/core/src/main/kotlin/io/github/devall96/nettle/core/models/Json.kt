package io.github.devall96.nettle.core.models

import kotlinx.serialization.ExperimentalSerializationApi
import kotlinx.serialization.descriptors.SerialDescriptor
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonNamingStrategy

/**
 * JSON settings, matching Nettle.Core.Json: Jellyfin uses PascalCase names, Seerr / NostalgiaTV
 * camelCase. Defaults are always written (so EnableTranscoding=false really goes to the server),
 * nulls never are.
 */
@OptIn(ExperimentalSerializationApi::class)
object NettleJson {
    private object PascalCase : JsonNamingStrategy {
        override fun serialNameForJson(descriptor: SerialDescriptor, elementIndex: Int, serialName: String): String =
            serialName.replaceFirstChar { it.uppercaseChar() }
    }

    /** Jellyfin REST API (PascalCase). */
    val jellyfin: Json = Json {
        namingStrategy = PascalCase
        ignoreUnknownKeys = true
        encodeDefaults = true
        explicitNulls = false
        coerceInputValues = true
        isLenient = true
    }

    /** Seerr / NostalgiaTV (camelCase) and Nettle's own files. */
    val camel: Json = Json {
        ignoreUnknownKeys = true
        encodeDefaults = true
        explicitNulls = false
        coerceInputValues = true
        isLenient = true
    }

    /** Settings / vault files on disk: readable, tolerant. */
    val files: Json = Json {
        ignoreUnknownKeys = true
        encodeDefaults = true
        explicitNulls = false
        coerceInputValues = true
        prettyPrint = true
    }
}
