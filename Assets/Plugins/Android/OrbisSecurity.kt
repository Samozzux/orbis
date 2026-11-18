// Archivio protetto e sblocco biometrico per Orbis (docs/06 §4, docs/01 §10).
// Chiamato da Assets/Orbis/Platform/Android/AndroidSecurity.cs.
// - Valori normali: cifrati con una chiave AES-GCM dell'Android Keystore (nessuno sblocco richiesto).
// - Valori protetti: cifrati con la chiave pubblica di una coppia RSA del Keystore; la chiave privata richiede
//   l'autenticazione biometrica forte a ogni uso (BiometricPrompt con CryptoObject). Così salvare il refresh
//   token ruotato ogni ora non chiede nulla, leggerlo all'avvio chiede l'impronta o il volto. Se si registrano
//   nuove impronte la chiave viene invalidata dal sistema e serve di nuovo la password.
// Solo API di sistema (minimo Android 10 / API 29): nessuna dipendenza AndroidX.

package it.orbis.platform

import android.app.Activity
import android.content.Context
import android.hardware.biometrics.BiometricManager
import android.hardware.biometrics.BiometricPrompt
import android.os.Build
import android.os.CancellationSignal
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import java.security.Key
import java.security.KeyFactory
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.PrivateKey
import java.security.spec.MGF1ParameterSpec
import java.security.spec.X509EncodedKeySpec
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.OAEPParameterSpec
import javax.crypto.spec.PSource

/** Risultato di una lettura verso C# (AndroidJavaProxy): value = null se manca, se è annullata o se non si legge. */
interface OrbisSecurityCallback {
    fun onResult(requestId: Int, value: String?)
}

object OrbisSecurity {
    private const val STORE = "AndroidKeyStore"
    private const val PREFS = "orbis_secure"
    private const val PLAIN_KEY = "orbis.plain"
    private const val BIO_KEY = "orbis.bio"
    private const val IV = 12

    // OAEP con SHA-256 e MGF1 con SHA-1: è la combinazione che il Keystore accetta in decifratura
    private val oaep = OAEPParameterSpec("SHA-256", "MGF1", MGF1ParameterSpec.SHA1, PSource.PSpecified.DEFAULT)

    @JvmStatic
    fun biometryAvailable(activity: Activity): Boolean {
        val manager = activity.getSystemService(BiometricManager::class.java) ?: return false
        return if (Build.VERSION.SDK_INT >= 30) {
            manager.canAuthenticate(BiometricManager.Authenticators.BIOMETRIC_STRONG) == BiometricManager.BIOMETRIC_SUCCESS
        } else {
            @Suppress("DEPRECATION")
            manager.canAuthenticate() == BiometricManager.BIOMETRIC_SUCCESS
        }
    }

    @JvmStatic
    fun set(activity: Activity, key: String, value: String, biometric: Boolean): Boolean {
        return try {
            val plain = value.toByteArray(Charsets.UTF_8)
            val data = if (biometric) encryptRsa(plain) else encryptAes(plain)
            prefs(activity).edit().putString(name(key, biometric), Base64.encodeToString(data, Base64.NO_WRAP)).commit()
        } catch (e: Exception) {
            false
        }
    }

    @JvmStatic
    fun get(activity: Activity, key: String, reason: String, cancel: String, biometric: Boolean, requestId: Int, callback: OrbisSecurityCallback) {
        val stored = prefs(activity).getString(name(key, biometric), null)
        if (stored == null) {
            callback.onResult(requestId, null)
            return
        }
        val data = Base64.decode(stored, Base64.NO_WRAP)
        if (!biometric) {
            val text = try {
                String(decryptAes(data), Charsets.UTF_8)
            } catch (e: Exception) {
                null
            }
            callback.onResult(requestId, text)
            return
        }
        val cipher = try {
            rsaCipher(Cipher.DECRYPT_MODE, privateKey())
        } catch (e: Exception) {
            // chiave invalidata (nuove impronte o volti): il valore protetto non si legge più
            prefs(activity).edit().remove(name(key, true)).apply()
            try {
                keyStore().deleteEntry(BIO_KEY)
            } catch (ignored: Exception) {
            }
            callback.onResult(requestId, null)
            return
        }
        activity.runOnUiThread {
            val executor = activity.mainExecutor
            val builder = BiometricPrompt.Builder(activity)
                .setTitle(reason)
                .setNegativeButton(cancel, executor) { _, _ -> callback.onResult(requestId, null) }
            if (Build.VERSION.SDK_INT >= 30) builder.setAllowedAuthenticators(BiometricManager.Authenticators.BIOMETRIC_STRONG)
            builder.build().authenticate(BiometricPrompt.CryptoObject(cipher), CancellationSignal(), executor,
                object : BiometricPrompt.AuthenticationCallback() {
                    override fun onAuthenticationSucceeded(result: BiometricPrompt.AuthenticationResult) {
                        // il cifrario sbloccato arriva col risultato (le API lo dichiarano nullable)
                        val text = try {
                            result.cryptoObject?.cipher?.doFinal(data)?.let { String(it, Charsets.UTF_8) }
                        } catch (e: Exception) {
                            null
                        }
                        callback.onResult(requestId, text)
                    }

                    override fun onAuthenticationError(errorCode: Int, errString: CharSequence) {
                        callback.onResult(requestId, null)
                    }
                })
        }
    }

    @JvmStatic
    fun delete(activity: Activity, key: String, biometric: Boolean) {
        prefs(activity).edit().remove(name(key, biometric)).apply()
    }

    private fun name(key: String, biometric: Boolean) = if (biometric) "$key.bio" else key

    private fun prefs(context: Context) = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    private fun keyStore(): KeyStore = KeyStore.getInstance(STORE).apply { load(null) }

    private fun aesKey(): SecretKey {
        (keyStore().getKey(PLAIN_KEY, null) as? SecretKey)?.let { return it }
        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, STORE)
        generator.init(
            KeyGenParameterSpec.Builder(PLAIN_KEY, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .build()
        )
        return generator.generateKey()
    }

    private fun encryptAes(plain: ByteArray): ByteArray {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, aesKey())
        return cipher.iv + cipher.doFinal(plain)
    }

    private fun decryptAes(data: ByteArray): ByteArray {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, aesKey(), GCMParameterSpec(128, data, 0, IV))
        return cipher.doFinal(data, IV, data.size - IV)
    }

    private fun rsaCipher(mode: Int, key: Key): Cipher =
        Cipher.getInstance("RSA/ECB/OAEPWithSHA-256AndMGF1Padding").apply { init(mode, key, oaep) }

    private fun ensureRsa() {
        if (keyStore().containsAlias(BIO_KEY)) return
        val spec = KeyGenParameterSpec.Builder(BIO_KEY, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
            .setKeySize(2048)
            .setDigests(KeyProperties.DIGEST_SHA256, KeyProperties.DIGEST_SHA1)
            .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_RSA_OAEP)
            .setUserAuthenticationRequired(true)
            .setInvalidatedByBiometricEnrollment(true)
        if (Build.VERSION.SDK_INT >= 30) spec.setUserAuthenticationParameters(0, KeyProperties.AUTH_BIOMETRIC_STRONG)
        val generator = KeyPairGenerator.getInstance(KeyProperties.KEY_ALGORITHM_RSA, STORE)
        generator.initialize(spec.build())
        generator.generateKeyPair()
    }

    private fun encryptRsa(plain: ByteArray): ByteArray {
        ensureRsa()
        val pub = keyStore().getCertificate(BIO_KEY).publicKey
        // la chiave pubblica si usa fuori dal Keystore: cifrare non richiede lo sblocco
        val soft = KeyFactory.getInstance(pub.algorithm).generatePublic(X509EncodedKeySpec(pub.encoded))
        return rsaCipher(Cipher.ENCRYPT_MODE, soft).doFinal(plain)
    }

    private fun privateKey(): PrivateKey = keyStore().getKey(BIO_KEY, null) as PrivateKey
}
