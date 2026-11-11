package io.github.devall96.nettle.tv.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.devall96.nettle.tv.theme.Nettle
import io.github.devall96.nettle.tv.ui.*
import io.github.devall96.nettle.tv.vm.CrashModel

/** "Nettle closed unexpectedly last time": the saved error, and two ways on. */
@Composable
fun CrashScreen(vm: CrashModel) {
    val p = Nettle.colors
    val first = remember { FocusRequester() }
    LaunchedEffect(Unit) { runCatching { first.requestFocus() } }
    Column(Modifier.fillMaxSize().padding(horizontal = 48.dp, vertical = 40.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
        BrandMark(0.9f)
        T("Nettle closed unexpectedly last time", style = Nettle.type.h1)
        T(
            "This is what went wrong. Nothing has been sent anywhere: it is only shown here. To report it, take a photo of " +
                "this screen and post it at github.com/devall96/Nettle/issues (it may show your server's address).",
            muted = true,
        )
        Box(
            Modifier.fillMaxWidth().weight(1f).clip(RoundedCornerShape(10.dp)).background(p.surface)
                .border(1.dp, p.border, RoundedCornerShape(10.dp)).padding(18.dp, 14.dp),
        ) {
            T(vm.shown, style = TextStyle(fontFamily = FontFamily.Monospace, fontSize = 13.sp, lineHeight = 18.sp), maxLines = 22)
        }
        Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            TextButton("Try again", { vm.tryAgain() }, kind = ButtonKind.Primary, focusRequester = first)
            TextButton("Go to the sign-in screen", { vm.toSignIn() })
        }
        T("If it closes again on \"Try again\", choose \"Go to the sign-in screen\" next time.", style = Nettle.type.caption, muted = true)
    }
}
