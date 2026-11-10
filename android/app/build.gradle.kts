import java.util.Properties

// Nettle for Android TV: native Kotlin + Compose front end. See docs/native-android-tv.md.
plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
    alias(libs.plugins.kotlin.compose)
    alias(libs.plugins.kotlin.serialization)
}

/** The one version number for every platform (Directory.Build.props <Version>, e.g. 0.4.1). */
val nettleVersion: String = rootProject.file("../Directory.Build.props").readText()
    .let { Regex("<Version>\\s*([0-9]+\\.[0-9]+\\.[0-9]+)\\s*</Version>").find(it)?.groupValues?.get(1) }
    ?: throw GradleException("No <Version> in Directory.Build.props")

/** The release key's settings, kept outside the repository (see signingConfigs below). */
val releaseSigning: Properties? = (System.getenv("NETTLE_SIGNING")?.let { file(it) }
    ?: file(System.getProperty("user.home") + "/.nettle/nettle-release.properties"))
    .takeIf { it.isFile }
    ?.let { f -> Properties().apply { f.inputStream().use { load(it) } } }

android {
    namespace = "io.github.devall96.nettle.tv"
    compileSdk = 36

    defaultConfig {
        // Different id from the Avalonia build (io.github.devall96.nettle), so both can be installed.
        applicationId = "io.github.devall96.nettle.tv"
        minSdk = 28
        targetSdk = 35
        // Same version as the Windows and Linux builds: read from the repository's Directory.Build.props.
        versionName = nettleVersion
        versionCode = nettleVersion.split('.').map { it.toInt() }.let { (a, b, c) -> a * 10_000 + b * 100 + c }
    }

    signingConfigs {
        getByName("debug")
        // Official releases are signed with the maintainer's release key, which never enters the repo:
        // ~/.nettle/nettle-release.properties (storeFile, storePassword, keyAlias, keyPassword), or the
        // file named by NETTLE_SIGNING. Without it, release builds fall back to the debug key so anyone can build.
        if (releaseSigning != null) create("release") {
            storeFile = file(releaseSigning.getProperty("storeFile"))
            storePassword = releaseSigning.getProperty("storePassword")
            keyAlias = releaseSigning.getProperty("keyAlias")
            keyPassword = releaseSigning.getProperty("keyPassword")
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            signingConfig = signingConfigs.findByName("release") ?: signingConfigs.getByName("debug")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    // Sync-test builds (gradlew assembleRelease -Pnettle.syncTest=true): bundle the flash + beep clips from
    // tools/avsync/clips and accept `--es synctest aac|ac3`. Normal builds carry neither (only
    // assets/lipsync-check.mkv, the same flash + beep clip, for Settings → Lip sync check).
    val syncTest = (findProperty("nettle.syncTest") as String?) == "true"
    defaultConfig { buildConfigField("boolean", "SYNC_TEST", syncTest.toString()) }
    if (syncTest) sourceSets["main"].assets.srcDir(rootProject.file("../tools/avsync/clips"))
    // Played straight from the APK: keep the clips stored, so Media3 can seek in them.
    androidResources { noCompress += "mkv" }

    // Privacy: no dependency metadata blob for app stores (it is only readable by Google Play).
    dependenciesInfo {
        includeInApk = false
        includeInBundle = false
    }

    packaging {
        resources.excludes += setOf("META-INF/*.version", "META-INF/*.kotlin_module", "kotlin/**", "DebugProbesKt.bin")
    }

    lint {
        abortOnError = false
        checkReleaseBuilds = false
    }
}

kotlin { jvmToolchain(17) }

dependencies {
    implementation(project(":core"))
    implementation(libs.coroutines.android)

    implementation(platform(libs.compose.bom))
    implementation(libs.compose.foundation)
    implementation(libs.compose.ui)
    implementation(libs.compose.animation)
    implementation(libs.activity.compose)

    implementation(libs.coil.compose)
    implementation(libs.coil.okhttp)

    implementation(libs.media3.exoplayer)
    implementation(libs.media3.okhttp)
    implementation(libs.media3.ui)

    implementation(libs.profileinstaller)
}

// ---------------------------------------------------------------------------------------------
// Zero phoning home: the build fails if a dependency outside this allowlist appears, and always
// fails on Google Play Services / Firebase / transport / crash reporting / analytics or an
// adaptive-streaming (HLS / DASH / SmoothStreaming) player module.
// ---------------------------------------------------------------------------------------------
val allowedGroups = listOf(
    "androidx", "org.jetbrains", "com.squareup.okhttp3", "com.squareup.okio", "io.coil-kt.coil3", "com.google.accompanist",
    "com.google.guava", "com.google.errorprone", "com.google.j2objc", "org.checkerframework", "org.jspecify",
    "com.google.code.findbugs",
)
val bannedPatterns = listOf(
    "com.google.android.gms", "com.google.firebase", "com.google.android.datatransport", "com.google.android.play",
    "io.sentry", "com.bugsnag", "com.crashlytics", "io.fabric", "com.microsoft.appcenter", "com.amplitude",
    "com.mixpanel", "com.segment", "com.flurry", "com.newrelic", "com.datadoghq", "ch.acra",
    "androidx.media3:media3-exoplayer-hls", "androidx.media3:media3-exoplayer-dash", "androidx.media3:media3-exoplayer-smoothstreaming",
    "androidx.media3:media3-exoplayer-ima", "androidx.media3:media3-cast",
)

val checkDependencies by tasks.registering {
    group = "verification"
    description = "Fails if any runtime dependency could phone home or transcode (see build.gradle.kts)."
    val resolved = configurations.named("releaseRuntimeClasspath").map { cfg ->
        cfg.incoming.resolutionResult.allComponents.mapNotNull { c ->
            (c.id as? org.gradle.api.artifacts.component.ModuleComponentIdentifier)?.let { "${it.group}:${it.module}:${it.version}" }
        }
    }
    inputs.property("modules", resolved)
    doLast {
        val modules = resolved.get()
        val banned = modules.filter { m -> bannedPatterns.any { m.startsWith(it) } }
        val unknown = modules.filter { m -> val g = m.substringBefore(":"); allowedGroups.none { g == it || g.startsWith("$it.") } }
        if (banned.isNotEmpty() || unknown.isNotEmpty())
            throw GradleException("Dependency check failed.\nBanned: $banned\nNot on the allowlist: $unknown")
        logger.lifecycle("checkDependencies: ${modules.size} modules, all on the allowlist")
    }
}
tasks.named("preBuild") { dependsOn(checkDependencies) }
