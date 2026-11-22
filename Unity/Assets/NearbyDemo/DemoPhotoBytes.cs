// Read the JPEG dimensions before asking Unity to allocate a texture.
internal static class DemoPhotoBytes
{
    public static bool Valid(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 12 || bytes.Length > DemoGroup.MaxPhotoBytes ||
            bytes[0] != 0xff || bytes[1] != 0xd8 || bytes[^2] != 0xff || bytes[^1] != 0xd9) return false;

        int offset = 2;
        while (offset + 3 < bytes.Length && bytes[offset++] == 0xff)
        {
            byte marker = bytes[offset++];
            if (marker == 0xff)
            {
                offset--;
                continue;
            }
            int length = (bytes[offset] << 8) | bytes[offset + 1];
            if (length < 2 || length > bytes.Length - offset || marker == 0xda) return false;
            if (marker == 0xc0 || marker == 0xc1 || marker == 0xc2)
            {
                if (length < 8) return false;
                int height = (bytes[offset + 3] << 8) | bytes[offset + 4];
                int width = (bytes[offset + 5] << 8) | bytes[offset + 6];
                return width > 0 && width <= DemoGroup.MaxPhotoSide && height > 0 && height <= DemoGroup.MaxPhotoSide;
            }
            offset += length;
        }
        return false;
    }
}
