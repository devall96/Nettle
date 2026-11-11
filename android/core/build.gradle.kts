// Platform-neutral port of src/Nettle.Core: no Android imports, so its tests run on the PC.
plugins {
    alias(libs.plugins.kotlin.jvm)
    alias(libs.plugins.kotlin.serialization)
}
kotlin {
    jvmToolchain(17)
    // This module runs on Android 9+ TVs, which only have the Java 8 class library (plus java.time). Compiling
    // against the Java 8 API makes a newer call (e.g. URLEncoder.encode(String, Charset), Android 13+) a build
    // error here instead of a crash on older TVs: Android's lint doesn't look inside a plain Kotlin module.
    compilerOptions {
        jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_1_8)
        freeCompilerArgs.add("-Xjdk-release=1.8")
    }
}
java {
    sourceCompatibility = JavaVersion.VERSION_1_8
    targetCompatibility = JavaVersion.VERSION_1_8
}
dependencies {
    api(libs.coroutines.core)
    api(libs.serialization.json)
    api(libs.okhttp)
    compileOnly(libs.kxml2)
    testImplementation(libs.kxml2)
    testImplementation(libs.junit)
    testImplementation(libs.okhttp.mockwebserver)
}
tasks.test {
    // The source scan in NeverTranscodeTests reads the whole android/ tree.
    systemProperty("nettle.androidRoot", rootDir.absolutePath)
}
