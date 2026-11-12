package ai.pranavpadidala.jarvisphone

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import org.json.JSONObject
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/**
 * Stores the phone-to-laptop bridge configuration encrypted with an AES-256
 * key held by Android Keystore. The bearer token is never written to
 * preferences in plaintext and is excluded from Android backup.
 */
class BridgeConfigStore(context: Context) {
    data class Config(val host: String, val port: Int, val token: String)

    private val preferences = context.getSharedPreferences(
        "jarvis_bridge_secure_config",
        Context.MODE_PRIVATE
    )

    fun save(config: Config) {
        require(config.host.isNotBlank())
        require(config.port in 1024..65535)
        require(config.token.toByteArray(Charsets.UTF_8).size >= 32)

        val plain = JSONObject()
            .put("host", config.host)
            .put("port", config.port)
            .put("token", config.token)
            .toString()
            .toByteArray(Charsets.UTF_8)

        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, getOrCreateKey())
        val encrypted = cipher.doFinal(plain)

        preferences.edit()
            .putString(KEY_IV, Base64.encodeToString(cipher.iv, Base64.NO_WRAP))
            .putString(KEY_DATA, Base64.encodeToString(encrypted, Base64.NO_WRAP))
            .commit()
    }

    fun load(): Config? {
        val ivText = preferences.getString(KEY_IV, null) ?: return null
        val dataText = preferences.getString(KEY_DATA, null) ?: return null
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(
            Cipher.DECRYPT_MODE,
            getOrCreateKey(),
            GCMParameterSpec(128, Base64.decode(ivText, Base64.NO_WRAP))
        )
        val plain = cipher.doFinal(Base64.decode(dataText, Base64.NO_WRAP))
        val json = JSONObject(String(plain, Charsets.UTF_8))
        val config = Config(
            host = json.getString("host"),
            port = json.getInt("port"),
            token = json.getString("token")
        )
        require(config.host.isNotBlank() && config.port in 1024..65535)
        require(config.token.toByteArray(Charsets.UTF_8).size >= 32)
        return config
    }

    fun clear() {
        preferences.edit().clear().commit()
    }

    private fun getOrCreateKey(): SecretKey {
        val keyStore = KeyStore.getInstance(ANDROID_KEY_STORE).apply { load(null) }
        val existing = keyStore.getKey(KEY_ALIAS, null) as? SecretKey
        if (existing != null) return existing

        val generator = KeyGenerator.getInstance(
            KeyProperties.KEY_ALGORITHM_AES,
            ANDROID_KEY_STORE
        )
        generator.init(
            KeyGenParameterSpec.Builder(
                KEY_ALIAS,
                KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT
            )
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .setRandomizedEncryptionRequired(true)
                .build()
        )
        return generator.generateKey()
    }

    private companion object {
        const val ANDROID_KEY_STORE = "AndroidKeyStore"
        const val KEY_ALIAS = "jarvis_phone_bridge_aes"
        const val TRANSFORMATION = "AES/GCM/NoPadding"
        const val KEY_IV = "iv"
        const val KEY_DATA = "ciphertext"
    }
}
