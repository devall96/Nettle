package io.github.devall96.nettle.tv.screens

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import io.github.devall96.nettle.tv.theme.Icons
import io.github.devall96.nettle.tv.theme.Nettle
import io.github.devall96.nettle.tv.ui.*
import io.github.devall96.nettle.tv.vm.WatchTogetherModel

/** WatchTogetherView: start or join a SyncPlay group, see who's in it, leave it. */
@Composable
fun WatchTogetherScreen(vm: WatchTogetherModel) {
    val p = Nettle.colors
    val first = vm.requester(if (vm.inGroup) "leave" else "create")
    LaunchedEffect(vm.inGroup) {
        kotlinx.coroutines.delay(50)
        runCatching { (vm.lastFocusKey?.let { vm.focusRequesters[it] } ?: first).requestFocus() }
    }
    Box(Modifier.fillMaxSize()) {
        LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(start = 56.dp, top = 40.dp, end = 56.dp, bottom = 40.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp)) {
            item(key = "head") {
                Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    T("Watch together", style = Nettle.type.h1)
                    T("Start a group or join one, then play anything: it plays for everyone in the group at the same moment, here and in other Jellyfin apps that support SyncPlay. Pausing, seeking and skipping happen for everyone too.",
                        Modifier.widthIn(max = 760.dp), muted = true)
                }
            }
            if (vm.inGroup) item(key = "group") {
                Panel(Modifier.widthIn(max = 760.dp)) {
                    T("YOUR GROUP", style = Nettle.type.section)
                    Spacer(Modifier.height(10.dp))
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                        Icon(Icons.Group, size = 28.dp, tint = p.accent)
                        T(vm.groupName ?: "", style = Nettle.type.h3)
                    }
                    Spacer(Modifier.height(8.dp))
                    T("Watching: ${vm.groupPeople ?: ""}")
                    T(vm.groupState, muted = true)
                    Spacer(Modifier.height(6.dp))
                    T("Pick something from your libraries and press Play: it starts for everyone once all players are ready.", style = Nettle.type.caption, muted = true)
                    Spacer(Modifier.height(12.dp))
                    TextButton("Leave the group", { vm.leave() }, Modifier.rememberFocus(vm, "leave"))
                }
            } else {
                item(key = "actions") {
                    Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                        TextButton("Start a group", { vm.create() }, Modifier.rememberFocus(vm, "create"), kind = ButtonKind.Primary)
                        TextButton("Refresh", { vm.reload() }, Modifier.rememberFocus(vm, "refresh"))
                    }
                }
                item(key = "label") { T("GROUPS ON YOUR SERVER", Modifier.padding(top = 10.dp), style = Nettle.type.section) }
                if (vm.noGroups) item(key = "none") { T("No groups right now. Start one, or ask someone else to.", muted = true) }
                items(vm.groups, key = { it.info.groupId }) { g ->
                    TextButton("${g.info.name}  ·  ${g.people}  ·  Join", { vm.join(g) }, Modifier.widthIn(min = 420.dp).rememberFocus(vm, "g:" + g.info.groupId))
                }
            }
        }
        if (vm.isBusy) Spinner(Modifier.align(Alignment.TopCenter).padding(top = 180.dp), size = 40.dp)
        ErrorText(vm.error, Modifier.align(Alignment.BottomStart).padding(start = 56.dp, bottom = 40.dp))
    }
}
