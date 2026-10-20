# app.html + engine.js → artifact.html (claude 아티팩트용, 스켈레톤 없음) / index.html (자체 호스팅용 완전한 HTML)
import pathlib
root = pathlib.Path(__file__).parent
app = (root/'app.html').read_text(encoding='utf-8')
eng = (root/'engine.js').read_text(encoding='utf-8')
body = app.replace('/*ENGINE*/', eng)
(root/'artifact.html').write_text(body, encoding='utf-8')
head_end = body.index('<style>')
head, rest = body[:head_end], body[head_end:]
style_end = rest.index('</style>') + len('</style>')
full = ('<!doctype html>\n<html lang="ko">\n<head>\n<meta charset="utf-8">\n'
        '<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">\n'
        + head + rest[:style_end] + '\n</head>\n<body>\n' + rest[style_end:] + '\n</body>\n</html>\n')
(root/'index.html').write_text(full, encoding='utf-8')
print('built', len(full))
