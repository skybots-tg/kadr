"""
Builds README images: renders demo pages with headless Chrome, lets Kadr draw its real UI
over them offscreen (Kadr.exe --promo), then composes the final cards into docs/.

    python promo/build.py [path\\to\\Kadr.exe]
"""
import os, subprocess, sys, tempfile, shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parent
ASSETS = ROOT / "assets"
SCENES = ROOT / "scenes"
DOCS = REPO / "docs"
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
    exe = Path(sys.argv[1]) if len(sys.argv) > 1 else REPO / "bin" / "Release" / "net8.0-windows" / "Kadr.exe"
    render(ROOT / "src" / "desk.html", ASSETS / "desk.png", 2048, 1152, 1.25)
    render(ROOT / "src" / "app.html", ASSETS / "app.png", 1280, 800, 1.25)
    render(ROOT / "src" / "winscene.html", ASSETS / "winscene.png", 2048, 1152, 1.25)

    SCENES.mkdir(exist_ok=True)
    subprocess.run([str(exe), "--promo", str(ASSETS), str(SCENES)], check=True, timeout=180)
    err = SCENES / "error.txt"
    if err.exists():
        sys.exit(err.read_text(encoding="utf-8"))
    print("scenes:", ", ".join(sorted(p.name for p in SCENES.glob("*.png"))))

    for card in sorted((ROOT / "cards").glob("*.html")):
        render(card, DOCS / (card.stem + ".png"), 1600, 900, 1.5)


if __name__ == "__main__":
    main()
