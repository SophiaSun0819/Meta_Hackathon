package com.thinkingspace.security;

import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyInfo;
import android.security.keystore.KeyProperties;
import android.os.Build;
import android.util.Base64;
import java.math.BigInteger;
import java.nio.charset.StandardCharsets;
import java.security.KeyPairGenerator;
import java.security.KeyFactory;
import java.security.KeyStore;
import java.security.MessageDigest;
import java.security.PrivateKey;
import java.security.Signature;
import java.security.interfaces.ECPublicKey;
import java.security.spec.ECGenParameterSpec;
import java.util.Arrays;
import java.util.regex.Pattern;

public final class QuestP256KeyStore {
    private static final String STORE = "AndroidKeyStore";
    private static final Pattern ALIAS = Pattern.compile("^[A-Za-z0-9._-]{1,64}$");

    private QuestP256KeyStore() {}

    public static void ensureKey(String alias) throws Exception {
        requireAlias(alias);
        KeyStore store = store();
        if (store.containsAlias(alias)) return;
        KeyPairGenerator generator = KeyPairGenerator.getInstance(KeyProperties.KEY_ALGORITHM_EC, STORE);
        generator.initialize(new KeyGenParameterSpec.Builder(alias,
            KeyProperties.PURPOSE_SIGN | KeyProperties.PURPOSE_VERIFY)
            .setAlgorithmParameterSpec(new ECGenParameterSpec("secp256r1"))
            .setDigests(KeyProperties.DIGEST_SHA256)
            .setUserAuthenticationRequired(false)
            .build());
        generator.generateKeyPair();
    }

    public static boolean containsKey(String alias) throws Exception {
        requireAlias(alias);
        return store().containsAlias(alias);
    }

    public static String getPublicJwk(String alias) throws Exception {
        requireAlias(alias);
        ECPublicKey key = publicKey(alias);
        String x = base64Url(unsigned32(key.getW().getAffineX()));
        String y = base64Url(unsigned32(key.getW().getAffineY()));
        return "{\"kty\":\"EC\",\"crv\":\"P-256\",\"x\":\"" + x
            + "\",\"y\":\"" + y + "\",\"ext\":true,\"key_ops\":[\"verify\"]}";
    }

    public static String getPublicKeyThumbprint(String alias) throws Exception {
        requireAlias(alias);
        ECPublicKey key = publicKey(alias);
        String x = base64Url(unsigned32(key.getW().getAffineX()));
        String y = base64Url(unsigned32(key.getW().getAffineY()));
        String canonical = "{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"" + x
            + "\",\"y\":\"" + y + "\"}";
        return base64Url(MessageDigest.getInstance("SHA-256")
            .digest(canonical.getBytes(StandardCharsets.UTF_8)));
    }

    public static String signP1363(String alias, String payloadBase64) throws Exception {
        requireAlias(alias);
        KeyStore store = store();
        PrivateKey privateKey = (PrivateKey)store.getKey(alias, null);
        if (privateKey == null) throw new IllegalStateException("DEVICE_KEY_MISSING");
        Signature signer = Signature.getInstance("SHA256withECDSA");
        signer.initSign(privateKey);
        signer.update(Base64.decode(payloadBase64, Base64.DEFAULT));
        return base64Url(derToP1363(signer.sign()));
    }

    public static void deleteKey(String alias) throws Exception {
        requireAlias(alias);
        KeyStore store = store();
        if (store.containsAlias(alias)) store.deleteEntry(alias);
    }

    public static String runDiagnostics(String alias, String payloadBase64, boolean deleteAtEnd)
        throws Exception {
        requireAlias(alias);
        boolean existedBefore = containsKey(alias);
        ensureKey(alias);
        boolean existsAfterGeneration = containsKey(alias);
        byte[] payload = Base64.decode(payloadBase64, Base64.DEFAULT);
        String signatureText = signP1363(alias, payloadBase64);
        byte[] signature = Base64.decode(signatureText,
            Base64.URL_SAFE | Base64.NO_WRAP | Base64.NO_PADDING);
        boolean signatureVerified = verifyP1363(alias, payload, signature);

        KeyStore store = store();
        PrivateKey privateKey = (PrivateKey)store.getKey(alias, null);
        byte[] encoded = privateKey == null ? null : privateKey.getEncoded();
        boolean privateKeyExportable = encoded != null;
        if (encoded != null) Arrays.fill(encoded, (byte)0);

        KeyInfo info = KeyFactory.getInstance(privateKey.getAlgorithm(), STORE)
            .getKeySpec(privateKey, KeyInfo.class);
        String securityLevel = securityLevel(info);
        boolean insideSecureHardware = info.isInsideSecureHardware();
        if (deleteAtEnd) deleteKey(alias);
        boolean deleted = deleteAtEnd && !containsKey(alias);
        Arrays.fill(payload, (byte)0);
        Arrays.fill(signature, (byte)0);

        return "{\"keyExistedBefore\":" + existedBefore
            + ",\"keyExistsAfterGeneration\":" + existsAfterGeneration
            + ",\"signatureLength\":64,\"signatureVerified\":" + signatureVerified
            + ",\"privateKeyExportable\":" + privateKeyExportable
            + ",\"insideSecureHardware\":" + insideSecureHardware
            + ",\"securityLevel\":\"" + securityLevel
            + "\",\"deleted\":" + deleted + "}";
    }

    private static KeyStore store() throws Exception {
        KeyStore store = KeyStore.getInstance(STORE);
        store.load(null);
        return store;
    }

    private static ECPublicKey publicKey(String alias) throws Exception {
        java.security.cert.Certificate certificate = store().getCertificate(alias);
        if (certificate == null || !(certificate.getPublicKey() instanceof ECPublicKey))
            throw new IllegalStateException("DEVICE_KEY_MISSING");
        return (ECPublicKey)certificate.getPublicKey();
    }

    private static boolean verifyP1363(String alias, byte[] payload, byte[] p1363)
        throws Exception {
        if (p1363 == null || p1363.length != 64)
            throw new IllegalArgumentException("DEVICE_PROOF_INVALID");
        Signature verifier = Signature.getInstance("SHA256withECDSA");
        verifier.initVerify(publicKey(alias));
        verifier.update(payload);
        return verifier.verify(p1363ToDer(p1363));
    }

    private static byte[] p1363ToDer(byte[] raw) {
        byte[] r = encodeScalar(raw, 0);
        byte[] s = encodeScalar(raw, 32);
        byte[] result = new byte[2 + r.length + s.length];
        result[0] = 0x30;
        result[1] = (byte)(r.length + s.length);
        System.arraycopy(r, 0, result, 2, r.length);
        System.arraycopy(s, 0, result, 2 + r.length, s.length);
        return result;
    }

    private static byte[] encodeScalar(byte[] raw, int offset) {
        int first = 0;
        while (first < 31 && raw[offset + first] == 0) first++;
        int length = 32 - first;
        boolean prefix = (raw[offset + first] & 0x80) != 0;
        byte[] result = new byte[2 + length + (prefix ? 1 : 0)];
        result[0] = 0x02;
        result[1] = (byte)(length + (prefix ? 1 : 0));
        System.arraycopy(raw, offset + first, result, prefix ? 3 : 2, length);
        return result;
    }

    private static String securityLevel(KeyInfo info) {
        if (Build.VERSION.SDK_INT >= 31) {
            switch (info.getSecurityLevel()) {
                case KeyProperties.SECURITY_LEVEL_STRONGBOX: return "STRONGBOX";
                case KeyProperties.SECURITY_LEVEL_TRUSTED_ENVIRONMENT: return "TRUSTED_ENVIRONMENT";
                case KeyProperties.SECURITY_LEVEL_SOFTWARE: return "SOFTWARE";
                case KeyProperties.SECURITY_LEVEL_UNKNOWN_SECURE: return "UNKNOWN_SECURE";
                default: return "UNKNOWN";
            }
        }
        return info.isInsideSecureHardware() ? "SECURE_HARDWARE_LEGACY" : "SOFTWARE_LEGACY";
    }

    private static void requireAlias(String alias) {
        if (alias == null || !ALIAS.matcher(alias).matches())
            throw new IllegalArgumentException("DEVICE_KEY_ALIAS_INVALID");
    }

    private static byte[] unsigned32(BigInteger value) {
        byte[] source = value.toByteArray();
        int start = source.length == 33 && source[0] == 0 ? 1 : 0;
        int length = source.length - start;
        if (length < 1 || length > 32) throw new IllegalArgumentException("DEVICE_KEY_INVALID");
        byte[] result = new byte[32];
        System.arraycopy(source, start, result, 32 - length, length);
        return result;
    }

    private static byte[] derToP1363(byte[] der) {
        if (der == null || der.length < 8 || der.length > 72 || der[0] != 0x30
            || (der[1] & 0xff) >= 0x80 || (der[1] & 0xff) != der.length - 2)
            throw new IllegalArgumentException("DEVICE_PROOF_INVALID");
        int[] offset = new int[] { 2 };
        byte[] result = new byte[64];
        readScalar(der, offset, result, 0);
        readScalar(der, offset, result, 32);
        if (offset[0] != der.length) throw new IllegalArgumentException("DEVICE_PROOF_INVALID");
        return result;
    }

    private static void readScalar(byte[] der, int[] offset, byte[] output, int outputOffset) {
        int position = offset[0];
        if (position + 2 > der.length || der[position++] != 0x02)
            throw new IllegalArgumentException("DEVICE_PROOF_INVALID");
        int length = der[position++] & 0xff;
        if (length < 1 || length > 33 || position + length > der.length)
            throw new IllegalArgumentException("DEVICE_PROOF_INVALID");
        if ((der[position] & 0x80) != 0) throw new IllegalArgumentException("DEVICE_PROOF_INVALID");
        if (length > 1 && der[position] == 0) {
            if ((der[position + 1] & 0x80) == 0) throw new IllegalArgumentException("DEVICE_PROOF_INVALID");
            position++;
            length--;
        }
        if (length > 32) throw new IllegalArgumentException("DEVICE_PROOF_INVALID");
        boolean nonZero = false;
        for (int i = 0; i < length; i++) nonZero |= der[position + i] != 0;
        if (!nonZero) throw new IllegalArgumentException("DEVICE_PROOF_INVALID");
        System.arraycopy(der, position, output, outputOffset + 32 - length, length);
        offset[0] = position + length;
    }

    private static String base64Url(byte[] value) {
        return Base64.encodeToString(value, Base64.URL_SAFE | Base64.NO_WRAP | Base64.NO_PADDING);
    }
}
