package com.example.nearbydemo;

import android.content.ContentResolver;
import android.content.ContentUris;
import android.content.Context;
import android.database.Cursor;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.ImageDecoder;
import android.graphics.Matrix;
import android.media.ExifInterface;
import android.net.Uri;
import android.os.Build;
import android.provider.MediaStore;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.util.ArrayList;

public final class DemoAlbum {
    public static String[] list(Context context, int limit) {
        Uri collection = MediaStore.Images.Media.EXTERNAL_CONTENT_URI;
        String id = MediaStore.Images.Media._ID;
        ArrayList<String> photos = new ArrayList<String>();
        try (Cursor cursor = context.getContentResolver().query(collection, new String[]{id},
                null, null, MediaStore.Images.Media.DATE_ADDED + " DESC, " + id + " DESC")) {
            while (cursor != null && photos.size() < limit && cursor.moveToNext()) {
                photos.add(ContentUris.withAppendedId(collection, cursor.getLong(0)).toString());
            }
        }
        return photos.toArray(new String[0]);
    }

    public static byte[] jpeg(Context context, String id, int side, int maxBytes) throws IOException {
        Uri uri = Uri.parse(id);
        ContentResolver resolver = context.getContentResolver();
        Bitmap bitmap;
        if (Build.VERSION.SDK_INT >= 28) {
            bitmap = ImageDecoder.decodeBitmap(ImageDecoder.createSource(resolver, uri), (decoder, info, source) -> {
                int width = info.getSize().getWidth();
                int height = info.getSize().getHeight();
                float scale = Math.min(1f, (float) side / Math.max(width, height));
                decoder.setTargetSize(Math.max(1, Math.round(width * scale)), Math.max(1, Math.round(height * scale)));
                decoder.setAllocator(ImageDecoder.ALLOCATOR_SOFTWARE);
            });
        } else {
            bitmap = decodeLegacy(resolver, uri, side);
        }
        if (bitmap == null) return null;
        try {
            ByteArrayOutputStream output = new ByteArrayOutputStream();
            for (int quality = 82; quality >= 34; quality -= 12) {
                output.reset();
                if (bitmap.compress(Bitmap.CompressFormat.JPEG, quality, output) && output.size() <= maxBytes)
                    return output.toByteArray();
            }
            return null;
        } finally {
            bitmap.recycle();
        }
    }

    private static Bitmap decodeLegacy(ContentResolver resolver, Uri uri, int side) throws IOException {
        BitmapFactory.Options options = new BitmapFactory.Options();
        options.inJustDecodeBounds = true;
        try (InputStream input = resolver.openInputStream(uri)) {
            if (input == null) return null;
            BitmapFactory.decodeStream(input, null, options);
        }
        if (options.outWidth < 1 || options.outHeight < 1) return null;
        options.inSampleSize = 1;
        int longest = Math.max(options.outWidth, options.outHeight);
        while (longest / options.inSampleSize > side * 2) options.inSampleSize *= 2;
        options.inJustDecodeBounds = false;
        Bitmap decoded;
        try (InputStream input = resolver.openInputStream(uri)) {
            if (input == null) return null;
            decoded = BitmapFactory.decodeStream(input, null, options);
        }
        if (decoded == null) return null;

        int orientation = ExifInterface.ORIENTATION_NORMAL;
        try (InputStream input = resolver.openInputStream(uri)) {
            if (input != null) orientation = new ExifInterface(input)
                .getAttributeInt(ExifInterface.TAG_ORIENTATION, ExifInterface.ORIENTATION_NORMAL);
        } catch (IOException ignored) {
            // Formats without EXIF already use their decoded orientation.
        }
        Matrix matrix = new Matrix();
        switch (orientation) {
            case ExifInterface.ORIENTATION_FLIP_HORIZONTAL: matrix.setScale(-1, 1); break;
            case ExifInterface.ORIENTATION_ROTATE_180: matrix.setRotate(180); break;
            case ExifInterface.ORIENTATION_FLIP_VERTICAL: matrix.setScale(1, -1); break;
            case ExifInterface.ORIENTATION_TRANSPOSE: matrix.setRotate(90); matrix.postScale(-1, 1); break;
            case ExifInterface.ORIENTATION_ROTATE_90: matrix.setRotate(90); break;
            case ExifInterface.ORIENTATION_TRANSVERSE: matrix.setRotate(-90); matrix.postScale(-1, 1); break;
            case ExifInterface.ORIENTATION_ROTATE_270: matrix.setRotate(-90); break;
        }
        float scale = Math.min(1f, (float)side / Math.max(decoded.getWidth(), decoded.getHeight()));
        matrix.postScale(scale, scale);
        Bitmap result = Bitmap.createBitmap(decoded, 0, 0, decoded.getWidth(), decoded.getHeight(), matrix, true);
        if (result != decoded) decoded.recycle();
        return result;
    }
}
