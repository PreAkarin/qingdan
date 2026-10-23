from PIL import Image, ImageDraw, ImageFilter
import os

SIZE = 1024
OUT_DIR = os.path.dirname(os.path.abspath(__file__))
SRC_DIR = os.path.join(OUT_DIR, '..', 'src', 'Qingdan', 'Assets')
os.makedirs(SRC_DIR, exist_ok=True)


def lerp(a, b, t):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))


def gradient(size, top_left, bottom_right):
    img = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    px = img.load()
    for y in range(size):
        for x in range(size):
            t = (x / (size - 1) * 0.42 + y / (size - 1) * 0.58)
            px[x, y] = (*lerp(top_left, bottom_right, t), 255)
    return img


# Big supersampled canvas for smooth anti-aliasing.
SS = 2
S = SIZE * SS
base = Image.new('RGBA', (S, S), (0, 0, 0, 0))

# Soft colored glow behind the tile.
glow = Image.new('RGBA', (S, S), (0, 0, 0, 0))
gd = ImageDraw.Draw(glow)
gd.rounded_rectangle((int(S*0.09), int(S*0.12), int(S*0.91), int(S*0.94)), radius=int(S*0.22),
                     fill=(73, 93, 245, 95))
glow = glow.filter(ImageFilter.GaussianBlur(int(S*0.055)))
base.alpha_composite(glow)

# Main gradient tile.
margin = int(S * 0.085)
tile_size = S - margin * 2
grad = gradient(tile_size, (105, 125, 255), (76, 71, 216))
mask = Image.new('L', (tile_size, tile_size), 0)
md = ImageDraw.Draw(mask)
md.rounded_rectangle((0, 0, tile_size - 1, tile_size - 1), radius=int(tile_size * 0.255), fill=255)
tile = Image.new('RGBA', (tile_size, tile_size), (0, 0, 0, 0))
tile.paste(grad, (0, 0), mask)
base.alpha_composite(tile, (margin, margin))

d = ImageDraw.Draw(base)

# Inner highlight at the top-left.
highlight = Image.new('RGBA', (S, S), (0, 0, 0, 0))
hd = ImageDraw.Draw(highlight)
hd.rounded_rectangle((margin + int(S*0.018), margin + int(S*0.018), S - margin - int(S*0.018), S - margin - int(S*0.018)),
                     radius=int(tile_size*0.238), outline=(255, 255, 255, 62), width=int(S*0.006))
base.alpha_composite(highlight)

# A faint diagonal sheen.
sheen = Image.new('RGBA', (S, S), (0, 0, 0, 0))
sd = ImageDraw.Draw(sheen)
sd.ellipse((-S*0.42, -S*0.55, S*0.67, S*0.32), fill=(255, 255, 255, 22))
sheen = sheen.filter(ImageFilter.GaussianBlur(int(S*0.04)))
mask2 = Image.new('L', (S, S), 0)
md2 = ImageDraw.Draw(mask2)
md2.rounded_rectangle((margin, margin, S - margin, S - margin), radius=int(tile_size*0.255), fill=255)
base = Image.composite(Image.alpha_composite(base, sheen), base, mask2)
d = ImageDraw.Draw(base)

# Check mark with rounded joins.
stroke = int(S * 0.085)
points = [(int(S*0.285), int(S*0.515)), (int(S*0.445), int(S*0.675)), (int(S*0.745), int(S*0.345))]
# Draw the path as several overlapping rounded lines + circles.
for i in range(len(points) - 1):
    d.line([points[i], points[i+1]], fill=(255, 255, 255, 255), width=stroke, joint='curve')
    for p in (points[i], points[i+1]):
        r = stroke // 2
        d.ellipse((p[0]-r, p[1]-r, p[0]+r, p[1]+r), fill=(255, 255, 255, 255))

# White check gets a very soft shadow.
# Accent dot in the top-right.
dot_center = (int(S*0.745), int(S*0.255))
dot_r = int(S*0.068)
# Glow
dot_glow = Image.new('RGBA', (S, S), (0, 0, 0, 0))
dg = ImageDraw.Draw(dot_glow)
dg.ellipse((dot_center[0]-dot_r*2.2, dot_center[1]-dot_r*2.2, dot_center[0]+dot_r*2.2, dot_center[1]+dot_r*2.2),
           fill=(255, 185, 92, 85))
dot_glow = dot_glow.filter(ImageFilter.GaussianBlur(int(S*0.028)))
base.alpha_composite(dot_glow)

d = ImageDraw.Draw(base)
# Amber sparkle dot with a tiny inner shine.
d.ellipse((dot_center[0]-dot_r, dot_center[1]-dot_r, dot_center[0]+dot_r, dot_center[1]+dot_r), fill=(255, 190, 92, 255))
d.ellipse((dot_center[0]-dot_r*0.38 - dot_r*0.2, dot_center[1]-dot_r*0.38 - dot_r*0.2,
           dot_center[0]-dot_r*0.38 + dot_r*0.22, dot_center[1]-dot_r*0.38 + dot_r*0.22),
          fill=(255, 246, 222, 230))

# Downsample to the target size.
icon = base.resize((SIZE, SIZE), Image.LANCZOS)

# Save a large PNG preview.
icon.save(os.path.join(OUT_DIR, 'icon-1024.png'))

# Save multi-resolution ICO.
sizes = [256, 128, 96, 64, 48, 40, 32, 24, 20, 16]
ico_path = os.path.join(SRC_DIR, 'app.ico')
icon.save(ico_path, format='ICO', sizes=[(s, s) for s in sizes])

# Save preview on a light desktop-ish background for review.
preview_bg = Image.new('RGBA', (1200, 800), (235, 239, 248, 255))
# soft blobs
blob = Image.new('RGBA', preview_bg.size, (0, 0, 0, 0))
bd = ImageDraw.Draw(blob)
bd.ellipse((-180, -160, 620, 520), fill=(123, 151, 255, 58))
bd.ellipse((650, 380, 1400, 1080), fill=(255, 200, 160, 48))
blob = blob.filter(ImageFilter.GaussianBlur(90))
preview_bg.alpha_composite(blob)
large = icon.resize((420, 420), Image.LANCZOS)
shadow = Image.new('RGBA', preview_bg.size, (0, 0, 0, 0))
sh = ImageDraw.Draw(shadow)
sh.ellipse((400, 505, 800, 635), fill=(30, 40, 90, 65))
shadow = shadow.filter(ImageFilter.GaussianBlur(28))
preview_bg.alpha_composite(shadow)
preview_bg.alpha_composite(large, (390, 130))
small = icon.resize((96, 96), Image.LANCZOS)
preview_bg.alpha_composite(small, (535, 590))
preview_bg.convert('RGB').save(os.path.join(OUT_DIR, 'icon-preview.png'))

print('saved', ico_path)
