#import <Photos/Photos.h>
#import <UIKit/UIKit.h>
#include <cstring>

extern "C" int DemoAlbum_HasAccess()
{
    PHAuthorizationStatus status = [PHPhotoLibrary authorizationStatusForAccessLevel:PHAccessLevelReadWrite];
    return status == PHAuthorizationStatusAuthorized || status == PHAuthorizationStatusLimited;
}

extern "C" void DemoAlbum_RequestAccess()
{
    dispatch_async(dispatch_get_main_queue(), ^{
        [PHPhotoLibrary requestAuthorizationForAccessLevel:PHAccessLevelReadWrite handler:^(PHAuthorizationStatus status) {}];
    });
}

extern "C" int DemoAlbum_List(unsigned char *destination, int capacity, int limit)
{
    @autoreleasepool {
        PHFetchOptions *options = [PHFetchOptions new];
        options.fetchLimit = limit;
        options.sortDescriptors = @[[NSSortDescriptor sortDescriptorWithKey:@"creationDate" ascending:NO]];
        PHFetchResult<PHAsset *> *assets = [PHAsset fetchAssetsWithMediaType:PHAssetMediaTypeImage options:options];
#if !__has_feature(objc_arc)
        [options release];
#endif
        NSMutableArray<NSString *> *ids = [NSMutableArray arrayWithCapacity:assets.count];
        for (PHAsset *asset in assets) [ids addObject:asset.localIdentifier];
        NSData *data = [[ids componentsJoinedByString:@"\n"] dataUsingEncoding:NSUTF8StringEncoding];
        if (data.length > (NSUInteger)capacity) return -1;
        if (data.length > 0) std::memcpy(destination, data.bytes, data.length);
        return (int)data.length;
    }
}

extern "C" int DemoAlbum_Jpeg(const char *id, unsigned char *destination, int capacity, int side)
{
    @autoreleasepool {
        NSString *identifier = [NSString stringWithUTF8String:id];
        PHAsset *asset = [PHAsset fetchAssetsWithLocalIdentifiers:@[identifier] options:nil].firstObject;
        if (asset == nil) return 0;
        PHImageRequestOptions *options = [PHImageRequestOptions new];
        options.synchronous = YES;
        options.resizeMode = PHImageRequestOptionsResizeModeExact;
        options.deliveryMode = PHImageRequestOptionsDeliveryModeHighQualityFormat;
        options.networkAccessAllowed = NO;
        __block int length = 0;
        [[PHImageManager defaultManager] requestImageForAsset:asset
            targetSize:CGSizeMake(side, side) contentMode:PHImageContentModeAspectFit options:options
            resultHandler:^(UIImage *image, NSDictionary *info) {
                if (image == nil || [info[PHImageResultIsDegradedKey] boolValue]) return;
                CGSize size = image.size;
                if (size.width <= 0 || size.height <= 0) return;
                CGFloat scale = MIN(1.0, side / MAX(size.width, size.height));
                CGSize output = CGSizeMake(MAX(1, floor(size.width * scale)), MAX(1, floor(size.height * scale)));
                UIGraphicsImageRendererFormat *format = [UIGraphicsImageRendererFormat defaultFormat];
                format.opaque = YES;
                format.scale = 1;
                UIGraphicsImageRenderer *renderer = [[UIGraphicsImageRenderer alloc] initWithSize:output format:format];
                UIImage *resized = [renderer imageWithActions:^(UIGraphicsImageRendererContext *context) {
                    [image drawInRect:CGRectMake(0, 0, output.width, output.height)];
                }];
#if !__has_feature(objc_arc)
                [renderer release];
#endif
                for (int quality = 82; resized != nil && quality >= 34; quality -= 12) {
                    NSData *data = UIImageJPEGRepresentation(resized, quality / 100.0);
                    if (data.length == 0 || data.length > (NSUInteger)capacity) continue;
                    std::memcpy(destination, data.bytes, data.length);
                    length = (int)data.length;
                    break;
                }
            }];
#if !__has_feature(objc_arc)
        [options release];
#endif
        return length;
    }
}
