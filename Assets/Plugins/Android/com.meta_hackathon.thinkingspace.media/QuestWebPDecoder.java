package com.meta_hackathon.thinkingspace.media;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import java.io.ByteArrayOutputStream;
public final class QuestWebPDecoder {
    private QuestWebPDecoder() {}
    public static byte[] decodeToPng(byte[] webp) {
        if (webp == null || webp.length == 0 || webp.length > 8388608) return null;
        Bitmap bitmap = BitmapFactory.decodeByteArray(webp, 0, webp.length);
        if (bitmap == null || bitmap.getWidth() != 1024 || bitmap.getHeight() != 1024) { if (bitmap != null) bitmap.recycle(); return null; }
        ByteArrayOutputStream output = new ByteArrayOutputStream();
        boolean ok = bitmap.compress(Bitmap.CompressFormat.PNG, 100, output);
        bitmap.recycle();
        return ok ? output.toByteArray() : null;
    }
}
