package io.github.devall96.nettle.tv.vm

import io.github.devall96.nettle.tv.platform.CrashLog

/**
 * Shown at start when Nettle closed unexpectedly last time: what went wrong, and a way on that doesn't
 * lead straight back into the same screen (a saved sign-in otherwise reopens Home, and a crash there
 * would repeat on every start).
 */
class CrashModel(val report: String) : ViewModelBase() {
    /** What fits on the screen; the saved text holds a little more. */
    val shown: String = report.lines().take(22).joinToString("\n")

    /** Start as usual (signs in to the last profile again). */
    fun tryAgain() { CrashLog.clear(); Main.startNormally(autoResume = true) }

    /** Start at the profiles / sign-in screen, without opening the last profile. */
    fun toSignIn() { CrashLog.clear(); Main.startNormally(autoResume = false) }
}
