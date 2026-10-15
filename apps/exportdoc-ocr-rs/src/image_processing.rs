use anyhow::{bail, Context, Result};
use image::{DynamicImage, ImageDecoder, Rgb, RgbImage};
use ndarray::Array4;
use std::io::Cursor;

const MAX_IMAGE_SIDE: u32 = 16_384;
const MAX_IMAGE_PIXELS: u64 = 40_000_000;

pub(crate) fn decode_image(encoded: &[u8]) -> Result<RgbImage> {
    if encoded.is_empty() || encoded.len() as u64 > super::MAX_IMAGE_BYTES {
        bail!("image must be non-empty and no larger than 25 MiB");
    }
    let mut decoder = image::ImageReader::new(Cursor::new(encoded))
        .with_guessed_format()?
        .into_decoder()
        .context("unsupported or damaged image")?;
    let (width, height) = decoder.dimensions();
    validate_image_dimensions(width, height)?;
    let orientation = decoder.orientation().context("invalid image orientation")?;
    let mut image = DynamicImage::from_decoder(decoder).context("cannot decode OCR image")?;
    image.apply_orientation(orientation);
    let (width, height) = (image.width(), image.height());
    Ok(match image {
        DynamicImage::ImageRgb8(image) => image,
        image if image.color().has_alpha() => {
            let rgba = image.into_rgba8();
            RgbImage::from_fn(width, height, |x, y| {
                let pixel = rgba.get_pixel(x, y);
                let alpha = u32::from(pixel[3]);
                Rgb([0, 1, 2].map(|channel| {
                    ((u32::from(pixel[channel]) * alpha + 255 * (255 - alpha) + 127) / 255) as u8
                }))
            })
        }
        image => image.into_rgb8(),
    })
}

pub(crate) fn rgb_tensor(image: &RgbImage, detection: bool) -> Array4<f32> {
    let (width, height) = image.dimensions();
    let mut tensor = Array4::zeros((1, 3, height as usize, width as usize));
    let mean = [0.485, 0.456, 0.406];
    let deviation = [0.229, 0.224, 0.225];
    for (x, y, pixel) in image.enumerate_pixels() {
        for channel in 0..3 {
            let value = f32::from(pixel[2 - channel]) / 255.0;
            tensor[[0, channel, y as usize, x as usize]] = if detection {
                (value - mean[channel]) / deviation[channel]
            } else {
                (value - 0.5) / 0.5
            };
        }
    }
    tensor
}

pub(crate) fn det_size(width: u32, height: u32) -> (u32, u32) {
    let scale = (960.0 / f64::from(width.max(height))).min(1.0);
    let round = |value: u32| ((((f64::from(value) * scale).round() as u32).max(32) + 16) / 32) * 32;
    (round(width), round(height))
}

pub(crate) fn validate_image_dimensions(width: u32, height: u32) -> Result<()> {
    if width == 0
        || height == 0
        || width > MAX_IMAGE_SIDE
        || height > MAX_IMAGE_SIDE
        || u64::from(width) * u64::from(height) > MAX_IMAGE_PIXELS
    {
        bail!("image dimensions must be non-zero, at most {MAX_IMAGE_SIDE} per side and {MAX_IMAGE_PIXELS} pixels");
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn dimensions_and_encoded_input_are_bounded() {
        for (width, height) in [(0, 100), (MAX_IMAGE_SIDE + 1, 1), (10_000, 5_000)] {
            assert!(validate_image_dimensions(width, height).is_err());
        }
        assert!(validate_image_dimensions(5_000, 7_000).is_ok());
        assert!(decode_image(&[]).is_err());
        assert!(decode_image(b"not an image").is_err());
        assert_eq!(det_size(1600, 800), (960, 480));
    }

    #[test]
    fn transparent_background_is_composited_on_white() {
        let mut image = image::RgbaImage::new(2, 1);
        image.put_pixel(1, 0, image::Rgba([0, 0, 0, 255]));
        let mut encoded = Cursor::new(Vec::new());
        image
            .write_to(&mut encoded, image::ImageFormat::Png)
            .unwrap();
        let decoded = decode_image(encoded.get_ref()).unwrap();
        assert_eq!(decoded.get_pixel(0, 0).0, [255; 3]);
        assert_eq!(decoded.get_pixel(1, 0).0, [0; 3]);
    }

    #[test]
    fn phone_orientation_matches_the_browser_preview_dimensions() {
        let bytes = include_bytes!("../../../tests/OcrFixtures/exif_rotated.jpg");
        let image = decode_image(bytes).unwrap();
        assert_eq!(image.dimensions(), (1300, 460));
    }
}
