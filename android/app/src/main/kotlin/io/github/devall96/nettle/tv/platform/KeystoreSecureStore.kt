package io.github.devall96.nettle.tv.platform

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import io.github.devall96.nettle.core.services.SecureStore
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/**
 * Secrets (saved sign-ins, Seerr / NostalgiaTV keys) encrypted with an AES-256-GCM key that
 * lives in the Android Keystore and never leaves it. Each value is a file in the app's private
 * folder: 12-byte IV followed by the ciphertext. The app has allowBackup=false, so none of this
 * is ever copied to a cloud backup.
 */
class KeystoreSecureStore(context: Context) : SecureStore {
    private val dir = File(context.noBackupFilesDir, "secure").apply { mkdirs() }
    private val alias = "nettle.vault.v1"

    /** Opening the Keystore costs tens of ms on a TV box: the key handle is looked up once and kept. */
    @Volatile private var cachedKey: SecretKey? = null

    private fun key(): SecretKey = cachedKey ?: synchronized(this) { cachedKey ?: loadKey().also { cachedKey = it } }

    private fun loadKey(): SecretKey {
        val ks = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (ks.getEntry(alias, null) as? KeyStore.SecretKeyEntry)?.let { return it.secretKey }
        val gen = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
        gen.init(
            KeyGenParameterSpec.Builder(alias, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .build(),
        )
        return gen.generateKey()
    }

    private fun file(key: String) = File(dir, key.replace(Regex("[^A-Za-z0-9._-]"), "_") + ".bin")

    override suspend fun get(key: String): String? = withContext(Dispatchers.IO) {
        val f = file(key)
        if (!f.exists()) return@withContext null
        val bytes = f.readBytes()
        if (bytes.size < 13) return@withContext null
        val c = Cipher.getInstance("AES/GCM/NoPadding")
        c.init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, bytes, 0, 12))
        String(c.doFinal(bytes, 12, bytes.size - 12), Charsets.UTF_8)
    }

    override suspend fun set(key: String, value: String) = withContext(Dispatchers.IO) {
        val c = Cipher.getInstance("AES/GCM/NoPadding")
        c.init(Cipher.ENCRYPT_MODE, key())
        val out = c.iv + c.doFinal(value.toByteArray(Charsets.UTF_8))
        val f = file(key)
        val tmp = File(f.path + ".tmp")
        tmp.writeBytes(out)
        if (!tmp.renameTo(f)) { f.delete(); tmp.renameTo(f) }
        Unit
    }

    override suspend fun remove(key: String) = withContext(Dispatchers.IO) { file(key).delete(); Unit }
}
