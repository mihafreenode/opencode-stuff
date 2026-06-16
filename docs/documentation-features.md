# Documentation Features Workspace

The `Documentation Features` workspace profile turns a stock Ubuntu workspace into a complete documentation and reporting environment.

Node.js 22 LTS is the default runtime baseline for this workspace and for newly created workspaces in general.

It is intended for:

- business reports
- manuals and tutorials
- architecture documents
- multilingual PDFs
- diagram-heavy technical documentation
- data analysis and reporting

## Included Tooling

- Markdown to PDF: `pandoc`, `typst`
- HTML to PDF: `weasyprint`, `libreoffice`
- Diagrams: `@mermaid-js/mermaid-cli`, `graphviz`, `plantuml`
- PDF inspection: `poppler-utils`, `pypdf`, `pymupdf`, `qpdf`, `ghostscript`
- Report generation: `reportlab`
- Markdown parsing and custom pipelines: `markdown-it-py`
- Browser-backed rendering support: `playwright`, Playwright Chromium runtime

## Runtime Baseline

The generated `workspace.yaml` uses:

```yaml
runtime:
  default: default
  node: 22
```

This keeps Mermaid, Playwright, modern npm packages, and future MCP-style tooling on a current LTS runtime that matches present ecosystem expectations.

## Font Coverage

The profile installs a broad font set to keep Ubuntu-generated PDFs closer to Windows-authored documents:

- DejaVu
- Liberation
- Carlito for Calibri-compatible layout
- Caladea for Cambria-compatible layout
- Noto families including CJK and emoji coverage
- Inter and Roboto
- JetBrains Mono and Fira Code for developer-facing content
- `ttf-mscorefonts-installer` when the Ubuntu image makes it available

Provisioning also rebuilds the font cache with `fc-cache -fv`.

## Generated Workspace Assets

Workspaces that include the `document-processing` feature receive:

- `DOCUMENTATION-FEATURES.md`
- `docs/documentation-features.md`
- `scripts/validate-documentation-tooling.sh`
- `scripts/demo-documentation-workflows.sh`
- `samples/documentation/report.md`
- `samples/documentation/report.html`
- `samples/documentation/architecture.mmd`

## Validation

Run:

```bash
scripts/validate-documentation-tooling.sh
```

The script checks:

- `pandoc`
- `typst`
- `playwright`
- `chromium`
- `mmdc`
- `weasyprint`
- Python imports for `pypdf`, `pymupdf`, `reportlab`, and `markdown-it-py`
- `dot`
- `plantuml`
- installed fonts via `fc-list | sort`
- practical font matching through `fc-match`

It writes reports under `artifacts/documentation-validation/`.

## Demo Workflow

Run:

```bash
scripts/demo-documentation-workflows.sh
```

The demo script produces:

- Markdown to PDF output
- HTML to PDF output
- Mermaid SVG and PNG output
- a ReportLab-generated sample PDF
- PDF metadata inspection reports

Outputs are written under `artifacts/documentation-demo/`.
