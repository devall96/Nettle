// Platform-neutral port of src/Nettle.Core: no Android imports, so its tests run on the PC.
plugins {
    alias(libs.plugins.kotlin.jvm)
    alias(libs.plugins.kotlin.serialization)
}
kotlin { jvmToolchain(17) }
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
