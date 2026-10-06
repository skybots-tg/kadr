"""
Builds README images: renders demo pages with headless Chrome, lets Kadr draw its real UI
over them offscreen (Kadr.exe --promo), then composes the final cards into docs/.

    python promo/build.py [--lang ru|en] [path\\to\\Kadr.exe]

Russian (default) uses src/, cards/ and writes docs/*.png; English uses src/en/, cards/en/
and writes docs/en/*.png.
"""
import os, subprocess, sys, tempfile, shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parent
CHROME = next(p for p in [
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
] if os.path.exists(p))


def render(html: Path, out: Path, w: int, h: int, scale: float):
    out.parent.mkdir(parents=True, exist_ok=True)
    profile = tempfile.mkdtemp(prefix="kadr-promo-")
    try:
        subprocess.run([CHROME, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--no-first-run",
                        "--allow-file-access-from-files", f"--user-data-dir={profile}",
                        f"--force-device-scale-factor={scale}", f"--window-size={w},{h}",
                        f"--screenshot={out}", html.as_uri()], check=True, capture_output=True, timeout=120)
    finally:
        shutil.rmtree(profile, ignore_errors=True)
    print("rendered", out.relative_to(REPO))


def main():
    args = sys.argv[1:]
    lang = "ru"
    if "--lang" in args:
        i = args.index("--lang")
        lang = args[i + 1]
        del args[i:i + 2]
    sub = "" if lang == "ru" else lang
    src, cards = ROOT / "src" / sub, ROOT / "cards" / sub
    assets, scenes, docs = ROOT / "assets" / sub, ROOT / "scenes" / sub, REPO / "docs" / sub

    exe = Path(args[0]) if args else REPO / "bin" / "Release" / "net8.0-windows" / "Kadr.exe"
    render(src / "desk.html", assets / "desk.png", 2048, 1152, 1.25)
    render(src / "app.html", assets / "app.png", 1280, 800, 1.25)
    render(src / "winscene.html", assets / "winscene.png", 2048, 1152, 1.25)

    scenes.mkdir(parents=True, exist_ok=True)
    subprocess.run([str(exe), "--promo", str(assets), str(scenes), "--lang", lang], check=True, timeout=180)
    err = scenes / "error.txt"
    if err.exists():
        sys.exit(err.read_text(encoding="utf-8"))
    print("scenes:", ", ".join(sorted(p.name for p in scenes.glob("*.png"))))

    for card in sorted(cards.glob("*.html")):
        render(card, docs / (card.stem + ".png"), 1600, 900, 1.5)


if __name__ == "__main__":
    main()
