"""Regenerates the image-codec fixtures used by ImageCodecTests.

Each fixture comes with <name>.expect: the decoded image as RGBA, 16 bits per
sample (little-endian, rows top to bottom), scaled from the file's own depth
(an 8-bit value v is stored as v * 257). Lossless files are written from known
arrays; the JPEG references are libjpeg's decodes (through PIL), which the
engine's decoder matches within a few levels (another IDCT, bilinear chroma).

Needs numpy, Pillow, pypng and ImageMagick's `convert`:
    python3 src/RayTracer.Tests/TestImages/generate.py
"""
import os, subprocess
import numpy as np
import png  # pypng: writes every PNG colour type, depth and Adam7
from PIL import Image

out = os.path.dirname(os.path.abspath(__file__))
W, H = 19, 13


def expect(name, rgba16):
    rgba16.astype('<u2').tofile(os.path.join(out, name + '.expect'))


def rgba16(a, maxv, ch):
    h, w = a.shape[:2]
    o = np.zeros((h, w, 4), dtype=np.int64)
    s = 65535 / maxv
    if ch in (1, 2):
        o[..., 0] = o[..., 1] = o[..., 2] = np.round(a[..., 0] * s)
        o[..., 3] = np.round(a[..., 1] * s) if ch == 2 else 65535
    else:
        o[..., :3] = np.round(a[..., :3] * s)
        o[..., 3] = np.round(a[..., 3] * s) if ch == 4 else 65535
    return o


def rand(maxv, ch, seed):
    return np.random.default_rng(seed).integers(0, maxv + 1, size=(H, W, ch))


# PNG: sub-byte grey, 16-bit colour with alpha, both interlaced; grey + alpha.
for kind, bd, ch, il in (('g', 2, 1, True), ('rgba', 16, 4, True), ('ga', 8, 2, False), ('g', 16, 1, False)):
    a = rand((1 << bd) - 1, ch, bd * 10 + ch)
    name = f'png-{kind}{bd}{"-i" if il else ""}'
    w = png.Writer(W, H, greyscale=ch <= 2, alpha=ch in (2, 4), bitdepth=bd, interlace=il)
    with open(os.path.join(out, name + '.png'), 'wb') as f:
        w.write(f, a.reshape(H, W * ch).tolist())
    expect(name, rgba16(a, (1 << bd) - 1, ch))

# PNG palette, 4 bits, with per-entry alpha (tRNS), interlaced.
rng = np.random.default_rng(5)
pal = [tuple(int(v) for v in rng.integers(0, 256, 4)) for _ in range(16)]
idx = rand(15, 1, 104)[..., 0]
with open(os.path.join(out, 'png-pal4-i.png'), 'wb') as f:
    png.Writer(W, H, palette=pal, bitdepth=4, interlace=True).write(f, idx.tolist())
expect('png-pal4-i', rgba16(np.array(pal)[idx], 255, 4))

# PNG RGB with a colour key (tRNS).
a = rand(3, 3, 7) * 85
with open(os.path.join(out, 'png-rgbkey.png'), 'wb') as f:
    png.Writer(W, H, greyscale=False, bitdepth=8, transparent=(85, 170, 255)).write(f, a.reshape(H, W * 3).tolist())
alpha = np.where((a == [85, 170, 255]).all(-1), 0, 255)[..., None]
expect('png-rgbkey', rgba16(np.concatenate([a, alpha], -1), 255, 4))

# JPEG: a smooth colour image (chroma subsampling shows on it).
JW, JH = 45, 29
yy, xx = np.mgrid[0:JH, 0:JW]
smooth = np.zeros((JH, JW, 3), np.uint8)
smooth[..., 0] = (127 + 120 * np.sin(xx / 5.0)).astype(np.uint8)
smooth[..., 1] = (127 + 120 * np.cos(yy / 4.0)).astype(np.uint8)
smooth[..., 2] = ((xx + yy) * 255 // (JW + JH)).astype(np.uint8)
src = os.path.join(out, 'src.png')
Image.fromarray(smooth, 'RGB').save(src)


def jpeg_expect(name, grey=False):
    im = Image.open(os.path.join(out, name + '.jpg'))
    dec = np.array(im.convert('L' if grey else 'RGB')).astype(np.int64)
    expect(name, rgba16(dec[..., None] if grey else dec, 255, 1 if grey else 3))


Image.fromarray(smooth, 'RGB').save(os.path.join(out, 'jpg-422.jpg'), quality=90, subsampling=1)
jpeg_expect('jpg-422')
Image.fromarray(smooth, 'RGB').save(os.path.join(out, 'jpg-420-progressive.jpg'), quality=90, subsampling=2, progressive=True)
jpeg_expect('jpg-420-progressive')
Image.fromarray(smooth[..., 1], 'L').save(os.path.join(out, 'jpg-grey.jpg'), quality=90)
jpeg_expect('jpg-grey', grey=True)
subprocess.run(['convert', src, '-quality', '92', '-interlace', 'JPEG', '-define', 'jpeg:restart-interval=2',
                '-sampling-factor', '2x1', os.path.join(out, 'jpg-restart-progressive.jpg')], check=True)
jpeg_expect('jpg-restart-progressive')

# BMP: 4-bit RLE palette, 32-bit with an alpha mask (v5 header).
rgb = rand(255, 3, 55)
Image.fromarray(rgb.astype(np.uint8), 'RGB').save(src)
subprocess.run(['convert', src, '-colors', '16', '-compress', 'RLE', 'BMP3:' + os.path.join(out, 'bmp-rle4.bmp')], check=True)
expect('bmp-rle4', rgba16(np.array(Image.open(os.path.join(out, 'bmp-rle4.bmp')).convert('RGB')).astype(np.int64), 255, 3))
rgba = rand(255, 4, 77)
Image.fromarray(rgba.astype(np.uint8), 'RGBA').save(src)
subprocess.run(['convert', src, '-alpha', 'on', 'BMP:' + os.path.join(out, 'bmp-32a.bmp')], check=True)
expect('bmp-32a', rgba16(rgba, 255, 4))

# TGA: RLE RGBA with a top-left origin.
rgba = rand(255, 4, 66)
Image.fromarray(rgba.astype(np.uint8), 'RGBA').save(os.path.join(out, 'tga-rgba-rle-top.tga'),
                                                     compression='tga_rle', orientation=-1)
expect('tga-rgba-rle-top', rgba16(rgba, 255, 4))
os.remove(src)
