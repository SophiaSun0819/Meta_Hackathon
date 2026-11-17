package com.thinkingspace.security;

import android.content.Context;
import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyInfo;
import android.security.keystore.KeyProperties;
import android.os.Build;
import android.util.Base64;
import com.unity3d.player.UnityPlayer;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.security.SecureRandom;
import java.util.regex.Pattern;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.SecretKeyFactory;
import javax.crypto.spec.GCMParameterSpec;

/** Encrypts the complete device credential with a non-exportable Android Keystore AES key. */
public final class QuestEncryptedCredentialStore {
    private static final String STORE = "AndroidKeyStore";
    private static final String PREFS = "thinking_space_pairing_credentials_v1";
    private static final String PREFIX = "credential.";
    private static final Pattern ALIAS = Pattern.compile("^[A-Za-z0-9._-]{1,64}$");
    private static final String DIAGNOSTIC_PREFIX = "thinking-space-credential-diagnostic-";
    private static final SecureRandom RANDOM = new SecureRandom();

    private QuestEncryptedCredentialStore() {}

    public static void store(String alias, String credentialJson) throws Exception {
        requireAlias(alias);
        if (credentialJson == null || credentialJson.length() == 0 || credentialJson.length() > 16384)
            throw new IllegalArgumentException("DEVICE_CREDENTIAL_INVALID");
        SecretKey key = ensureKey(alias);
        byte[] iv = new byte[12];
        RANDOM.nextBytes(iv);
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.ENCRYPT_MODE, key, new GCMParameterSpec(128, iv));
        byte[] ciphertext = cipher.doFinal(credentialJson.getBytes(StandardCharsets.UTF_8));
        String encoded = Base64.encodeToString(iv, Base64.NO_WRAP) + "."
            + Base64.encodeToString(ciphertext, Base64.NO_WRAP);
        if (!preferences().edit().putString(PREFIX + alias, encoded).commit())
            throw new IllegalStateException("DEVICE_CREDENTIAL_STORAGE_FAILED");
    }

    public static String load(String alias) throws Exception {
        requireAlias(alias);
        String encoded = preferences().getString(PREFIX + alias, null);
        if (encoded == null) return null;
        int separator = encoded.indexOf('.');
        if (separator <= 0 || separator == encoded.length() - 1)
            throw new IllegalStateException("DEVICE_CREDENTIAL_INVALID");
        byte[] iv = Base64.decode(encoded.substring(0, separator), Base64.NO_WRAP);
        byte[] ciphertext = Base64.decode(encoded.substring(separator + 1), Base64.NO_WRAP);
        if (iv.length != 12 || ciphertext.length < 16)
            throw new IllegalStateException("DEVICE_CREDENTIAL_INVALID");
        SecretKey key = existingKey(alias);
        if (key == null) throw new IllegalStateException("DEVICE_CREDENTIAL_KEY_MISSING");
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.DECRYPT_MODE, key, new GCMParameterSpec(128, iv));
        return new String(cipher.doFinal(ciphertext), StandardCharsets.UTF_8);
    }

    public static boolean contains(String alias) throws Exception {
        requireAlias(alias);
        return preferences().contains(PREFIX + alias) && existingKey(alias) != null;
    }

    public static void delete(String alias) throws Exception {
        requireAlias(alias);
        if (!preferences().edit().remove(PREFIX + alias).commit())
            throw new IllegalStateException("DEVICE_CREDENTIAL_DELETE_FAILED");
        KeyStore store = keyStore();
        if (store.containsAlias(keyAlias(alias))) store.deleteEntry(keyAlias(alias));
    }

    public static boolean privateKeyMaterialExportable(String alias) throws Exception {
        SecretKey key = existingKey(requireAlias(alias));
        return key != null && key.getEncoded() != null;
    }

    public static String getSecurityLevel(String alias) throws Exception {
        KeyInfo info = keyInfo(alias);
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            int level = info.getSecurityLevel();
            if (level == KeyProperties.SECURITY_LEVEL_STRONGBOX) return "STRONGBOX";
            if (level == KeyProperties.SECURITY_LEVEL_TRUSTED_ENVIRONMENT) return "TRUSTED_ENVIRONMENT";
            if (level == KeyProperties.SECURITY_LEVEL_SOFTWARE) return "SOFTWARE";
            return "UNKNOWN_" + level;
        }
        return info.isInsideSecureHardware() ? "TRUSTED_ENVIRONMENT_LEGACY" : "SOFTWARE_LEGACY";
    }

    public static boolean isInsideSecureHardware(String alias) throws Exception {
        return keyInfo(alias).isInsideSecureHardware();
    }

    /** Test-runner-only corruption hook. Production aliases are rejected before any mutation. */
    public static void corruptForDiagnostics(String alias) throws Exception {
        requireAlias(alias);
        if (!alias.startsWith(DIAGNOSTIC_PREFIX))
            throw new SecurityException("DIAGNOSTIC_ALIAS_REQUIRED");
        String key = PREFIX + alias;
        String value = preferences().getString(key, null);
        if (value == null || value.length() < 4) throw new IllegalStateException("DEVICE_CREDENTIAL_MISSING");
        char replacement = value.charAt(value.length() - 1) == 'A' ? 'B' : 'A';
        if (!preferences().edit().putString(key, value.substring(0, value.length() - 1) + replacement).commit())
            throw new IllegalStateException("DEVICE_CREDENTIAL_STORAGE_FAILED");
    }

    private static SecretKey ensureKey(String alias) throws Exception {
        SecretKey existing = existingKey(alias);
        if (existing != null) return existing;
        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, STORE);
        generator.init(new KeyGenParameterSpec.Builder(keyAlias(alias),
            KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
            .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
            .setRandomizedEncryptionRequired(true)
            .build());
        return generator.generateKey();
    }

    private static SecretKey existingKey(String alias) throws Exception {
        return (SecretKey) keyStore().getKey(keyAlias(alias), null);
    }

    private static KeyInfo keyInfo(String alias) throws Exception {
        SecretKey key = existingKey(requireAlias(alias));
        if (key == null) throw new IllegalStateException("DEVICE_CREDENTIAL_KEY_MISSING");
        SecretKeyFactory factory = SecretKeyFactory.getInstance(key.getAlgorithm(), STORE);
        return (KeyInfo) factory.getKeySpec(key, KeyInfo.class);
    }

    private static KeyStore keyStore() throws Exception {
        KeyStore store = KeyStore.getInstance(STORE);
        store.load(null);
        return store;
    }

    private static SharedPreferences preferences() {
        return UnityPlayer.currentActivity.getApplicationContext()
            .getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }

    private static String keyAlias(String alias) { return "thinking-space-credential-" + alias; }

    private static String requireAlias(String alias) {
        if (alias == null || !ALIAS.matcher(alias).matches())
            throw new IllegalArgumentException("DEVICE_KEY_ALIAS_INVALID");
        return alias;
    }
}
