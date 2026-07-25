"""Build GitHub social preview (1280x640) + square logo from app icon."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont, ImageFilter

root = Path(__file__).resolve().parents[1]
icon_path = root / "Assets" / "tcgbootlog-icon.png"
out_dir = root / "docs"
out_dir.mkdir(exist_ok=True)

# Brand colors (match app: dark slate + teal)
BG_TOP = (10, 16, 24)
BG_BOT = (18, 36, 48)
TEAL = (45, 212, 191)
TEAL_DIM = (20, 120, 110)
TEXT = (226, 232, 240)
MUTED = (148, 163, 184)


def gradient(size, top, bot):
    w, h = size
    im = Image.new("RGB", size, top)
    draw = ImageDraw.Draw(im)
    for y in range(h):
        t = y / max(h - 1, 1)
        c = tuple(int(top[i] * (1 - t) + bot[i] * t) for i in range(3))
        draw.line([(0, y), (w, y)], fill=c)
    return im


def load_font(size):
    for name in (
        r"C:\Windows\Fonts\segoeuib.ttf",
        r"C:\Windows\Fonts\cascadiacode.ttf",
        r"C:\Windows\Fonts\consolab.ttf",
        r"C:\Windows\Fonts\arialbd.ttf",
    ):
        p = Path(name)
        if p.exists():
            return ImageFont.truetype(str(p), size)
    return ImageFont.load_default()


def soft_glow(base, xy, radius, color, alpha=60):
    layer = Image.new("RGBA", base.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    x, y = xy
    d.ellipse((x - radius, y - radius, x + radius, y + radius), fill=(*color, alpha))
    return Image.alpha_composite(base.convert("RGBA"), layer.filter(ImageFilter.GaussianBlur(40)))


# ── Social preview 1280×640 ──────────────────────────────────────────────
W, H = 1280, 640
banner = gradient((W, H), BG_TOP, BG_BOT).convert("RGBA")
banner = soft_glow(banner, (320, 320), 220, TEAL, 50)
banner = soft_glow(banner, (980, 200), 180, TEAL_DIM, 40)

icon = Image.open(icon_path).convert("RGBA")
icon = icon.resize((340, 340), Image.Resampling.LANCZOS)
# circular-ish panel behind icon
panel = Image.new("RGBA", banner.size, (0, 0, 0, 0))
pd = ImageDraw.Draw(panel)
pd.rounded_rectangle((120, 130, 520, 530), radius=36, fill=(15, 25, 36, 200), outline=(*TEAL, 90), width=2)
banner = Image.alpha_composite(banner, panel)
banner.paste(icon, (210, 160), icon)

draw = ImageDraw.Draw(banner)
title_f = load_font(72)
sub_f = load_font(28)
tag_f = load_font(22)

draw.text((580, 200), "TcgBootLog", font=title_f, fill=TEXT)
draw.text((580, 290), "Measured Boot  ·  TPM  ·  Windows Security", font=sub_f, fill=TEAL)

# pills
pills = ["TCG Log", "PCR Replay", "Integrity", "Secure Boot"]
x = 580
y = 370
for pill in pills:
    bbox = draw.textbbox((0, 0), pill, font=tag_f)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    pad_x, pad_y = 16, 10
    draw.rounded_rectangle(
        (x, y, x + tw + pad_x * 2, y + th + pad_y * 2),
        radius=14,
        fill=(20, 40, 52, 230),
        outline=(*TEAL_DIM, 180),
        width=1,
    )
    draw.text((x + pad_x, y + pad_y - 2), pill, font=tag_f, fill=MUTED)
    x += tw + pad_x * 2 + 12

draw.text((580, 470), "Windows toolkit for TPM attestation & boot integrity", font=tag_f, fill=MUTED)

social = out_dir / "github-social-preview.png"
banner.convert("RGB").save(social, "PNG", optimize=True)
print("wrote", social)

# ── Square logo 1024 (clean, for README / avatars) ───────────────────────
sq = gradient((1024, 1024), BG_TOP, BG_BOT).convert("RGBA")
sq = soft_glow(sq, (512, 512), 380, TEAL, 55)
big = icon.resize((720, 720), Image.Resampling.LANCZOS)
sq.paste(big, ((1024 - 720) // 2, (1024 - 720) // 2), big)
logo = out_dir / "repo-logo.png"
sq.convert("RGB").save(logo, "PNG", optimize=True)
print("wrote", logo)
