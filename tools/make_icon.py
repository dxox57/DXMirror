# Generates src/DXMirror.App/Assets/DXMirror.ico (original artwork: screen + signal waves)
from PIL import Image, ImageDraw
import os
def draw(size):
    s = size * 4
    im = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    r = int(s * 0.22)
    d.rounded_rectangle([0, 0, s - 1, s - 1], radius=r, fill=(15, 108, 189, 255))
    # monitor
    m = s * 0.18
    d.rounded_rectangle([m, s*0.24, s - m, s*0.66], radius=int(s*0.05), outline=(255,255,255,255), width=max(2,int(s*0.055)))
    # stand / triangle (bridge)
    d.polygon([(s*0.5, s*0.58), (s*0.34, s*0.80), (s*0.66, s*0.80)], fill=(255,255,255,255))
    return im.resize((size, size), Image.LANCZOS)
sizes = [16, 24, 32, 48, 64, 128, 256]
imgs = [draw(z) for z in sizes]
out = os.path.join(os.path.dirname(__file__), "..", "src", "DXMirror.App", "Assets", "DXMirror.ico")
imgs[-1].save(out, format="ICO", sizes=[(z, z) for z in sizes], append_images=imgs[:-1])
imgs[-1].save(os.path.join(os.path.dirname(__file__), "..", "installer", "DXMirror-256.png")) if os.path.isdir(os.path.join(os.path.dirname(__file__), "..", "installer")) else None
print("ok", os.path.getsize(out))
