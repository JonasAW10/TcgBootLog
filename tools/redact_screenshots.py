from PIL import Image, ImageFilter, ImageDraw
from pathlib import Path

src = Path(r"C:\Users\jonas\.cursor\projects\c-Users-jonas-TcgBootLog\assets")
out = Path(r"C:\Users\jonas\TcgBootLog\docs\screenshots")
out.mkdir(parents=True, exist_ok=True)

files = {
    "events.png": "c__Users_jonas_AppData_Roaming_Cursor_User_workspaceStorage_empty-window_images_image-9a816fc0-dfe9-4425-965e-969ca4a38a03.png",
    "tpm-values.png": "c__Users_jonas_AppData_Roaming_Cursor_User_workspaceStorage_empty-window_images_image-18c99bdb-8e71-4dfb-bc4a-26382a655b79.png",
    "integrity.png": "c__Users_jonas_AppData_Roaming_Cursor_User_workspaceStorage_empty-window_images_image-cc8a2850-2cf3-4dea-a38d-75cd0c839186.png",
    "boot-order.png": "c__Users_jonas_AppData_Roaming_Cursor_User_workspaceStorage_empty-window_images_image-e76b873e-bffe-4ad6-a3b5-ac5a1a6384b2.png",
    "windows-security.png": "c__Users_jonas_AppData_Roaming_Cursor_User_workspaceStorage_empty-window_images_image-c6f45da1-143e-41c1-afea-a8aead00d175.png",
}

# Strong redaction boxes (fractions of width/height)
regions = {
    "events.png": [
        (0.42, 0.20, 0.70, 0.99),  # Digest
        (0.62, 0.20, 0.995, 0.99),  # Details GUIDs / ImageLoc / digests in detail
    ],
    "tpm-values.png": [
        (0.22, 0.12, 0.995, 0.995),  # full Value (hex) incl. first chars
    ],
    "integrity.png": [
        (0.06, 0.16, 0.98, 0.30),   # NTP times
        (0.06, 0.28, 0.98, 0.56),   # EK leaf / TPM ids / CNs
        (0.06, 0.54, 0.98, 0.70),   # AK name (full line)
        (0.12, 0.72, 0.995, 0.995), # PCR expected + actual
    ],
    "boot-order.png": [],
    "windows-security.png": [],
}


def redact_region(im: Image.Image, box) -> Image.Image:
    x0, y0, x1, y1 = box
    w, h = im.size
    left, top = max(0, int(x0 * w)), max(0, int(y0 * h))
    right, bottom = min(w, int(x1 * w)), min(h, int(y1 * h))
    if right <= left or bottom <= top:
        return im

    # Solid dark panel + label — digests must be unreadable
    draw = ImageDraw.Draw(im)
    draw.rounded_rectangle(
        (left, top, right, bottom),
        radius=8,
        fill=(18, 28, 40),
        outline=(45, 70, 90),
        width=1,
    )
    label = "redacted"
    # Approximate center text without font metrics dependency
    tw = len(label) * 7
    th = 12
    cx = (left + right - tw) // 2
    cy = (top + bottom - th) // 2
    draw.text((cx, cy), label, fill=(80, 120, 140))
    return im


for name, fname in files.items():
    im = Image.open(src / fname).convert("RGB")
    for box in regions.get(name, []):
        im = redact_region(im, box)
    dest = out / name
    im.save(dest, "PNG", optimize=True)
    print("wrote", dest, im.size)

print("done")
