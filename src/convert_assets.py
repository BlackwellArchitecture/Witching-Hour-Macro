import os
import sys
from PIL import Image

def main():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    root_dir = os.path.dirname(script_dir)
    src_path = os.path.join(root_dir, "Images", "Witchinghour.webp")
    png_out = os.path.join(script_dir, "WitchingHour.png")
    ico_out = os.path.join(script_dir, "WitchingHour.ico")

    if not os.path.exists(src_path):
        print("ERROR: Source image not found:", src_path, file=sys.stderr)
        sys.exit(1)

    img = Image.open(src_path).convert("RGBA")
    w, h = img.size

    # 1. Save crisp high-DPI embedded UI PNG preserving aspect ratio (height = 480)
    target_h = 480
    target_w = int(round(w * (target_h / float(h))))
    ui_png = img.resize((target_w, target_h), Image.Resampling.LANCZOS)
    ui_png.save(png_out, format="PNG", optimize=True)
    print("Created PNG:", png_out, ui_png.size, os.path.getsize(png_out), "bytes")

    # 2. Create square transparent canvas centered without stretching for .ico
    side = max(w, h)
    # Add tiny padding (~4%) so silhouette edges breathe cleanly in Windows taskbar/Explorer
    pad = int(round(side * 0.04))
    canvas_size = side + pad * 2
    square = Image.new("RGBA", (canvas_size, canvas_size), (0, 0, 0, 0))
    offset_x = (canvas_size - w) // 2
    offset_y = (canvas_size - h) // 2
    square.paste(img, (offset_x, offset_y), img)

    icon_sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
    square.save(ico_out, format="ICO", sizes=icon_sizes)
    print("Created ICO:", ico_out, "sizes:", icon_sizes, os.path.getsize(ico_out), "bytes")

if __name__ == "__main__":
    main()
