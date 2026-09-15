# Редактор Markdown (CodeMirror 6)

`flow-editor.src.js` — исходник, `../wwwroot/js/flow-editor.js` — собранный бандл, который отдаётся браузеру.
Бандл закоммичен намеренно: у клиента нет фронтового пайплайна, и держать ради одного файла npm-сборку в
Dockerfile дороже, чем пересобирать его руками в те редкие разы, когда меняется сам редактор.

Бандл грузится лениво — только когда на странице впервые понадобился редактор (`flow.loadEditor()` в `flow.js`),
поэтому экраны без ввода Markdown за него не платят.

## Пересборка

Нужен Node 20+. Из корня репозитория:

```bash
mkdir -p /tmp/flow-editor && cd /tmp/flow-editor
npm init -y
npm install @codemirror/state@6 @codemirror/view@6 @codemirror/commands@6 \
            @codemirror/language@6 @codemirror/lang-markdown@6 @lezer/highlight@1 esbuild@0.24
cp <repo>/src/Flow.Client/editor/flow-editor.src.js entry.js
./node_modules/.bin/esbuild entry.js --bundle --minify --format=iife --target=es2020 \
  --outfile=<repo>/src/Flow.Client/wwwroot/js/flow-editor.js
```

Размер бандла — около 500 КБ (≈140 КБ после gzip). Проверять после пересборки: набор текста, тулбар,
`Ctrl+B/I/K`, `Ctrl+Enter`, автодополнение `@`, `Esc` при открытом списке упоминаний.
