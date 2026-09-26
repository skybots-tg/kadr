from PIL import Image, ImageDraw, ImageFilter

def make(size):
    S = size * 4
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    # gradient background
    grad = Image.new("RGBA", (S, S))
    top = (94, 92, 255); bot = (186, 70, 255)
    px = grad.load()
    for y in range(S):
        for x in range(S):
            t = (x * 0.35 + y * 0.65) / S
            px[x, y] = tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3)) + (255,)
    mask = Image.new("L", (S, S), 0)
    m = int(S * 0.06)
    ImageDraw.Draw(mask).rounded_rectangle([m, m, S - m, S - m], radius=int(S * 0.23), fill=255)
    img.paste(grad, (0, 0), mask)
    d = ImageDraw.Draw(img)
    w = max(4, int(S * 0.075))
    a = int(S * 0.27); b = S - a; L = int(S * 0.16)
    col = (255, 255, 255, 255)
    for (cx, cy, sx, sy) in [(a, a, 1, 1), (b, a, -1, 1), (a, b, 1, -1), (b, b, -1, -1)]:
        d.line([(cx, cy + sy * L), (cx, cy), (cx + sx * L, cy)], fill=col, width=w, joint="curve")
        r = w // 2
        for (px_, py_) in [(cx, cy + sy * L), (cx + sx * L, cy), (cx, cy)]:
            d.ellipse([px_ - r, py_ - r, px_ + r, py_ + r], fill=col)
    r = int(S * 0.085)
    c = S // 2
    d.ellipse([c - r, c - r, c + r, c + r], fill=col)
    return img.resize((size, size), Image.LANCZOS)

sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
imgs = [make(s) for s in sizes]
imgs[-1].save("kadr.ico", sizes=[(s, s) for s in sizes], append_images=imgs[:-1])
imgs[-1].save("kadr.png")
print("ok")
