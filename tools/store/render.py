# Renders the store pictures (icon 256x256, cover 1280x720, banner 960x240) from their templates
# with the version the mod is at, so the number printed on them never lags behind a release.
# Usage:  python tools/store/render.py            version taken from Plugin.cs
#         python tools/store/render.py v0.5.0
# Needs Microsoft Edge (or set BROWSER to a Chromium-based browser) to take the pictures.
import io, os, re, subprocess, sys, json, html

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, '..', '..')
OUT = os.path.join(ROOT, 'docs', 'store')
WORK = os.path.join(ROOT, 'dist', 'store-render')
os.makedirs(WORK, exist_ok=True)

if len(sys.argv) > 1:
    version = sys.argv[1]
else:
    plugin = io.open(os.path.join(ROOT, 'src', 'LivingFactions', 'Plugin.cs'), encoding='utf-8').read()
    version = 'v' + re.search(r'Version = "([^"]+)"', plugin).group(1)

browser = os.environ.get('BROWSER')
for guess in (r'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe', r'C:\Program Files\Microsoft\Edge\Application\msedge.exe',
              r'C:\Program Files\Google\Chrome\Application\chrome.exe'):
    if not browser and os.path.exists(guess):
        browser = guess
if not browser:
    sys.exit('no browser found: set BROWSER')

# The templates name their picture and fonts by the ids the design tool gave them; map.tsv says which
# file in assets/ each id is.
assets = {}
for line in io.open(os.path.join(HERE, 'assets', 'map.tsv'), encoding='utf-8'):
    if line.strip():
        key, name = line.strip().split('\t')
        assets[key] = 'file:///' + os.path.join(HERE, 'assets', name).replace(os.sep, '/')

def camel(match):
    parts = match.group(1).split('-')
    return ' ' + parts[0] + ''.join(part.capitalize() for part in parts[1:]) + '='

for name in ('icon', 'cover', 'banner'):
    page = io.open(os.path.join(HERE, name + '.template.html'), encoding='utf-8').read()
    props = json.loads(html.unescape(re.search(r'data-props="([^"]*)"', page).group(1)))
    width, height = props['$preview']['width'], props['$preview']['height']
    values = {k: v.get('default', '') for k, v in props.items() if isinstance(v, dict) and 'default' in v}
    values['version'] = version
    # The design tool's own wrappers and loader go; what is left is plain HTML and CSS.
    page = re.sub(r'<script src="[^"]*"></script>', '', page)
    page = re.sub(r'<script type="text/x-dc".*?</script>', '', page, flags=re.S)
    page = re.sub(r'</?(x-dc|helmet)>', '', page)
    for key, url in assets.items():
        page = page.replace('"' + key + '"', '"' + url + '"')
    # The tool writes attributes that need capitals (viewBox) in its own spelling.
    page = re.sub(r' sc-camel-([a-z-]+)=', camel, page)
    for key, value in values.items():
        page = page.replace('{{%s}}' % key, str(value))
    left = re.findall(r'\{\{[^}]+\}\}', page)
    if left:
        sys.exit('%s: nothing to put in %s' % (name, left))
    page = page.replace('</head>', '<style>html,body{margin:0;padding:0;background:#0b0d0a;overflow:hidden}</style></head>')
    src = os.path.join(WORK, name + '.html')
    io.open(src, 'w', encoding='utf-8').write(page)
    png = os.path.join(WORK, name + '.png')
    if os.path.exists(png):
        os.remove(png)
    subprocess.run([browser, '--headless=new', '--disable-gpu', '--hide-scrollbars', '--force-device-scale-factor=1',
                    '--virtual-time-budget=8000', '--window-size=%d,%d' % (width, height), '--screenshot=' + png,
                    'file:///' + src.replace(os.sep, '/')], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=120)
    from PIL import Image
    im = Image.open(png)
    if im.size != (width, height):
        im = im.crop((0, 0, width, height))
    if name == 'cover':
        im.convert('RGB').save(os.path.join(OUT, 'cover.jpg'), quality=92)
    else:
        im.save(os.path.join(OUT, name + '.png'))
    print(name, im.size, version)
