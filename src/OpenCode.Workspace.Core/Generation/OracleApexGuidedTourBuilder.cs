using System.Text;
using System.Text.Json;

namespace OpenCode.Workspace.Core.Generation;

internal static class OracleApexGuidedTourBuilder
{
    private const string TutorialVersion = "1.0";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static IReadOnlyDictionary<string, string> BuildFiles()
    {
        var lessons = BuildLessons();
        var metadata = new TutorialMetadata
        {
            TutorialVersion = TutorialVersion,
            LessonIdentifiers = lessons.Select(item => item.Id).ToList(),
            ExpectedCapabilities =
            [
                "workspace inspection",
                "semantic planning",
                "semantic editing",
                "workspace index refresh",
                "sqlcl validation",
                "development import",
                "builder synchronization review",
                "repair planning",
                "assistant rollback",
            ],
        };

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine("docs", "tutorials", "apexlang-guided-tour.md")] = BuildMarkdown(lessons),
            [Path.Combine("docs", "tutorials", "apexlang-guided-tour.html")] = BuildHtml(lessons),
            [Path.Combine(".opencode", "tutorials", "apexlang-guided-tour.json")] = JsonSerializer.Serialize(metadata, JsonOptions) + "\n",
        };
    }

    private static string BuildMarkdown(IReadOnlyList<TourLesson> lessons)
    {
        var lines = new List<string>
        {
            "# APEXlang Guided Tour",
            string.Empty,
            "This guided tour is a practical acceptance test for the `oracle-apexlang-demo` workspace.",
            string.Empty,
            "Tracks:",
            "- Beginner track: explains Oracle APEX page, region, item, process, LOV, validation, Builder, and deployment concepts in plain language.",
            "- Experienced APEX developer track: emphasizes Builder-to-source mapping, semantic plans, Git, validation, import/export, and drift control.",
            string.Empty,
            "Both tracks build the same Equipment Register application so results are comparable.",
            string.Empty,
            "Environment links and placeholders:",
            "- Builder: `https://example.test/replace-with-builder-url` or the generated development Builder URL in your workspace docs.",
            "- Running application: `https://example.test/replace-with-application-url` or the generated application URL in your workspace docs.",
            "- Workspace index: `.opencode/knowledge/apexlang-atlas/workspace-index.json`",
            "- Atlas docs: `.opencode/knowledge/apexlang-atlas/docs/oracle-apex-atlas.md`",
            "- Developer Companion docs: `.opencode/knowledge/apex-developers-companion/prompts/compact-context.md`",
            "- Synchronization status: `.opencode/knowledge/apexlang-atlas/generated/synchronization-status.json` when available, plus assistant and workspace diagnostics.",
            string.Empty,
            "Completion checklist:",
        };

        lines.AddRange(lessons.Select(lesson => $"- [ ] {lesson.Title}"));

        foreach (var lesson in lessons)
        {
            lines.Add(string.Empty);
            lines.Add($"## {lesson.Title}");
            lines.Add(string.Empty);
            lines.Add($"Lesson id: `{lesson.Id}`");
            lines.Add(string.Empty);
            lines.Add(lesson.Overview);
            lines.Add(string.Empty);
            lines.Add("Beginner track:");
            lines.Add($"- {lesson.BeginnerTrack}");
            lines.Add("Experienced APEX developer track:");
            lines.Add($"- {lesson.ExperiencedTrack}");
            lines.Add(string.Empty);
            if (!string.IsNullOrWhiteSpace(lesson.Command))
            {
                lines.Add("Command:");
                lines.Add("```text");
                lines.Add(lesson.Command);
                lines.Add("```");
                lines.Add(string.Empty);
            }

            if (!string.IsNullOrWhiteSpace(lesson.Prompt))
            {
                lines.Add("Agent prompt:");
                lines.Add("```text");
                lines.Add(lesson.Prompt);
                lines.Add("```");
                lines.Add(string.Empty);
            }

            lines.Add("Expected result:");
            lines.AddRange(lesson.ExpectedResult.Select(item => $"- {item}"));
            lines.Add(string.Empty);
            lines.Add("Verification:");
            lines.AddRange(lesson.Verification.Select(item => $"- {item}"));
            lines.Add(string.Empty);
            lines.Add("Troubleshooting:");
            lines.AddRange(lesson.Troubleshooting.Select(item => $"- {item}"));
        }

        return string.Join("\n", lines) + "\n";
    }

    private static string BuildHtml(IReadOnlyList<TourLesson> lessons)
    {
        var navItems = string.Join("\n", lessons.Select((lesson, index) => $"<button class=\"nav-link\" data-target=\"{lesson.Id}\"><span class=\"nav-step\">{index + 1}</span><span>{Escape(lesson.Title)}</span></button>"));
        var mobileOptions = string.Join("\n", lessons.Select((lesson, index) => $"<option value=\"{lesson.Id}\">{index + 1}. {Escape(lesson.Title)}</option>"));
        var sections = string.Join("\n", lessons.Select((lesson, index) => BuildLessonSectionHtml(lesson, index + 1)));
        return $$"""
<!-- GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES -->
<!-- Source inputs: workspace.yaml and catalog manifests under catalog/. -->
<!-- User edits to this file are not preserved. Edit workspace.yaml or catalog manifests instead. -->
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>APEXlang Guided Tour</title>
  <style>
    :root {
      color-scheme: light dark;
      --bg: #fff8f2;
      --panel: #ffffff;
      --muted: #6d6258;
      --text: #241b14;
      --accent: #f07a24;
      --accent-strong: #c95b0c;
      --border: rgba(36, 27, 20, 0.12);
      --shadow: 0 12px 28px rgba(36, 27, 20, 0.08);
      --code: #2d231c;
      --code-bg: #fff1e5;
    }
    @media (prefers-color-scheme: dark) {
      :root {
        --bg: #17120f;
        --panel: #241b16;
        --muted: #c9b7a7;
        --text: #fff4ea;
        --accent: #ff9447;
        --accent-strong: #ffb27a;
        --border: rgba(255, 244, 234, 0.12);
        --shadow: 0 14px 36px rgba(0, 0, 0, 0.32);
        --code: #fff4ea;
        --code-bg: #321f12;
      }
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      font-family: Inter, "Segoe UI", Arial, sans-serif;
      background: radial-gradient(circle at top left, rgba(240,122,36,0.16), transparent 30%), var(--bg);
      color: var(--text);
      line-height: 1.55;
    }
    a { color: var(--accent-strong); }
    code, pre { font-family: "JetBrains Mono", "Cascadia Code", monospace; }
    .layout { display: grid; grid-template-columns: 320px 1fr; min-height: 100vh; }
    .sidebar {
      position: sticky; top: 0; align-self: start; height: 100vh; overflow: auto;
      padding: 24px 18px; border-right: 1px solid var(--border); background: rgba(255,255,255,0.48);
      backdrop-filter: blur(14px);
    }
    .brand { margin-bottom: 20px; }
    .brand h1 { margin: 0 0 10px; font-size: 1.4rem; }
    .accent { color: var(--accent-strong); }
    .nav-link {
      width: 100%; border: 1px solid var(--border); background: var(--panel); color: var(--text);
      border-radius: 14px; padding: 12px; margin-bottom: 10px; text-align: left; display: flex; gap: 10px; align-items: center;
      cursor: pointer; box-shadow: var(--shadow);
    }
    .nav-link.active { outline: 2px solid var(--accent); }
    .nav-step {
      width: 32px; height: 32px; border-radius: 999px; display: inline-flex; align-items: center; justify-content: center;
      background: rgba(240,122,36,0.16); color: var(--accent-strong); font-weight: 700;
    }
    .content { padding: 28px; }
    .hero, .lesson, .resources, .checklist { background: var(--panel); border: 1px solid var(--border); box-shadow: var(--shadow); border-radius: 24px; padding: 24px; margin-bottom: 18px; }
    .hero h2, .lesson h2 { margin-top: 0; }
    .pill-row { display: flex; flex-wrap: wrap; gap: 10px; margin: 14px 0 0; }
    .pill { border: 1px solid var(--border); padding: 8px 12px; border-radius: 999px; background: rgba(240,122,36,0.08); }
    .mobile-nav { display: none; margin-bottom: 16px; }
    .track-toggle { display: inline-flex; gap: 8px; margin-top: 12px; flex-wrap: wrap; }
    .track-button {
      border: 1px solid var(--border); background: var(--panel); color: var(--text); border-radius: 999px; padding: 9px 14px; cursor: pointer;
    }
    .track-button.active { background: var(--accent); color: #fff; border-color: var(--accent); }
    .track { display: none; }
    .track.active { display: block; }
    .prompt-box, .command-box {
      position: relative; margin: 16px 0; border-radius: 18px; overflow: hidden; border: 1px solid var(--border); background: var(--code-bg);
    }
    .copy-button {
      position: absolute; top: 12px; right: 12px; border: 0; background: var(--accent); color: white; border-radius: 999px; padding: 8px 12px; cursor: pointer;
    }
    pre { margin: 0; padding: 18px; white-space: pre-wrap; color: var(--code); }
    details { border: 1px solid var(--border); border-radius: 16px; padding: 12px 14px; margin-top: 12px; }
    summary { cursor: pointer; font-weight: 600; }
    .meta-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 16px; }
    .meta-card { border: 1px solid var(--border); border-radius: 18px; padding: 14px; background: rgba(240,122,36,0.05); }
    .resource-list, .checklist ul, .lesson ul { padding-left: 1.1rem; }
    .completion-item { display: flex; gap: 10px; align-items: center; margin-bottom: 8px; }
    .note { color: var(--muted); font-size: 0.95rem; }
    @media (max-width: 920px) {
      .layout { grid-template-columns: 1fr; }
      .sidebar { display: none; }
      .mobile-nav { display: block; }
      .content { padding: 18px; }
      .meta-grid { grid-template-columns: 1fr; }
    }
  </style>
</head>
<body>
  <div class="layout">
    <aside class="sidebar">
      <div class="brand">
        <h1><span class="accent">OpenCode</span> APEXlang Guided Tour</h1>
        <p class="note">A polished, self-contained tutorial for the `oracle-apexlang-demo` workspace.</p>
        <div class="track-toggle">
          <button class="track-button active" data-track="beginner">Beginner</button>
          <button class="track-button" data-track="experienced">Experienced APEX</button>
        </div>
      </div>
      <nav>{{navItems}}</nav>
    </aside>
    <main class="content">
      <div class="mobile-nav hero">
        <label for="lessonPicker"><strong>Step navigation</strong></label>
        <select id="lessonPicker" style="width:100%; margin-top:10px; padding:10px; border-radius:12px;">
          {{mobileOptions}}
        </select>
        <div class="track-toggle">
          <button class="track-button active" data-track="beginner">Beginner</button>
          <button class="track-button" data-track="experienced">Experienced APEX</button>
        </div>
      </div>
      <section class="hero" id="top">
        <h2>Equipment Register Acceptance Tour</h2>
        <p>Use one attractive page to drive the complete APEXlang loop: inspect, plan, generate report and form, add shared components and validation, validate, repair, deploy, preview, round-trip Builder changes back to Git, enhance, and roll back.</p>
        <div class="pill-row">
          <span class="pill">Semantic planning first</span>
          <span class="pill">No raw .apx edits</span>
          <span class="pill">Validate before import</span>
          <span class="pill">Never overwrite Builder changes silently</span>
        </div>
      </section>
      <section class="resources">
        <h2>Core Links</h2>
        <div class="meta-grid">
          <div class="meta-card"><strong>Builder</strong><br /><a href="https://example.test/replace-with-builder-url">https://example.test/replace-with-builder-url</a><div class="note">Replace with the generated development Builder URL from your workspace.</div></div>
          <div class="meta-card"><strong>Running application</strong><br /><a href="https://example.test/replace-with-application-url">https://example.test/replace-with-application-url</a><div class="note">Replace with the generated development application URL.</div></div>
          <div class="meta-card"><strong>Workspace index</strong><br /><a href=".opencode/knowledge/apexlang-atlas/workspace-index.json">.opencode/knowledge/apexlang-atlas/workspace-index.json</a></div>
          <div class="meta-card"><strong>Atlas docs</strong><br /><a href=".opencode/knowledge/apexlang-atlas/docs/oracle-apex-atlas.md">.opencode/knowledge/apexlang-atlas/docs/oracle-apex-atlas.md</a></div>
          <div class="meta-card"><strong>Developer Companion docs</strong><br /><a href=".opencode/knowledge/apex-developers-companion/prompts/compact-context.md">.opencode/knowledge/apex-developers-companion/prompts/compact-context.md</a></div>
          <div class="meta-card"><strong>Synchronization status</strong><br /><a href=".opencode/knowledge/apexlang-atlas/generated/synchronization-status.json">.opencode/knowledge/apexlang-atlas/generated/synchronization-status.json</a><div class="note">Use this when present, plus assistant diagnostics and validation output.</div></div>
        </div>
      </section>
      {{sections}}
      <section class="checklist">
        <h2>Completion Checklist</h2>
        <p class="note">Progress is stored in <code>localStorage</code> in this browser only. No progress is written back into the workspace.</p>
        <div id="completionChecklist">
          {{string.Join("\n", lessons.Select(lesson => $"<label class=\"completion-item\"><input type=\"checkbox\" data-complete=\"{lesson.Id}\" /> <span>{Escape(lesson.Title)}</span></label>"))}}
        </div>
      </section>
    </main>
  </div>
  <script>
    const lessonIds = {{JsonSerializer.Serialize(lessons.Select(item => item.Id).ToList())}};
    const navLinks = Array.from(document.querySelectorAll('.nav-link'));
    const lessonPicker = document.getElementById('lessonPicker');
    const trackButtons = Array.from(document.querySelectorAll('.track-button'));
    const completionBoxes = Array.from(document.querySelectorAll('[data-complete]'));
    const progressKey = 'opencode-apexlang-guided-tour-progress';
    const trackKey = 'opencode-apexlang-guided-tour-track';

    function loadProgress() {
      try { return JSON.parse(localStorage.getItem(progressKey) || '{}'); } catch { return {}; }
    }

    function saveProgress(progress) {
      localStorage.setItem(progressKey, JSON.stringify(progress));
    }

    function activateLesson(id) {
      const target = document.getElementById(id);
      if (!target) return;
      navLinks.forEach(link => link.classList.toggle('active', link.dataset.target === id));
      if (lessonPicker) lessonPicker.value = id;
      target.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }

    function setTrack(track) {
      document.querySelectorAll('.track').forEach(node => node.classList.toggle('active', node.dataset.track === track));
      trackButtons.forEach(button => button.classList.toggle('active', button.dataset.track === track));
      localStorage.setItem(trackKey, track);
    }

    function copyText(button) {
      const text = button.parentElement.querySelector('pre').innerText;
      navigator.clipboard.writeText(text).then(() => {
        const original = button.textContent;
        button.textContent = 'Copied';
        setTimeout(() => button.textContent = original, 1200);
      });
    }

    navLinks.forEach(link => link.addEventListener('click', () => activateLesson(link.dataset.target)));
    if (lessonPicker) lessonPicker.addEventListener('change', () => activateLesson(lessonPicker.value));
    trackButtons.forEach(button => button.addEventListener('click', () => setTrack(button.dataset.track)));
    document.querySelectorAll('.copy-button').forEach(button => button.addEventListener('click', () => copyText(button)));

    const progress = loadProgress();
    completionBoxes.forEach(box => {
      box.checked = Boolean(progress[box.dataset.complete]);
      box.addEventListener('change', () => {
        const updated = loadProgress();
        updated[box.dataset.complete] = box.checked;
        saveProgress(updated);
      });
    });

    setTrack(localStorage.getItem(trackKey) || 'beginner');
    activateLesson(lessonIds[0]);
  </script>
</body>
</html>
""";
    }

    private static string BuildLessonSectionHtml(TourLesson lesson, int stepNumber)
    {
        var commandBlock = string.IsNullOrWhiteSpace(lesson.Command)
            ? string.Empty
            : $$"""
<div class="command-box">
  <button class="copy-button" type="button">Copy command</button>
  <pre>{{Escape(lesson.Command)}}</pre>
</div>
""";
        var promptBlock = string.IsNullOrWhiteSpace(lesson.Prompt)
            ? string.Empty
            : $$"""
<div class="prompt-box">
  <button class="copy-button" type="button">Copy prompt</button>
  <pre>{{Escape(lesson.Prompt)}}</pre>
</div>
""";
        return $$"""
<section class="lesson" id="{{lesson.Id}}">
  <h2>{{stepNumber}}. {{Escape(lesson.Title)}}</h2>
  <p class="note">Lesson id: <code>{{lesson.Id}}</code></p>
  <p>{{Escape(lesson.Overview)}}</p>
  <div class="track active" data-track="beginner">
    <strong>Beginner track</strong>
    <p>{{Escape(lesson.BeginnerTrack)}}</p>
  </div>
  <div class="track" data-track="experienced">
    <strong>Experienced APEX developer track</strong>
    <p>{{Escape(lesson.ExperiencedTrack)}}</p>
  </div>
  {{commandBlock}}
  {{promptBlock}}
  <details>
    <summary>Expected result</summary>
    <ul>
      {{string.Join("", lesson.ExpectedResult.Select(item => $"<li>{Escape(item)}</li>"))}}
    </ul>
  </details>
  <details>
    <summary>Verification</summary>
    <ul>
      {{string.Join("", lesson.Verification.Select(item => $"<li>{Escape(item)}</li>"))}}
    </ul>
  </details>
  <details>
    <summary>Troubleshooting</summary>
    <ul>
      {{string.Join("", lesson.Troubleshooting.Select(item => $"<li>{Escape(item)}</li>"))}}
    </ul>
  </details>
</section>
""";
    }

    private static List<TourLesson> BuildLessons()
    {
        var commonPromptRules = "Inspect the workspace index first. Use semantic planning. Show the full plan before editing. Use the semantic workflow rather than raw .apx edits. Validate before import. Avoid overwriting Builder-side changes. Report changed files, workspace-index changes, diagnostics, and synchronization state.";
        return
        [
            new TourLesson(
                "understand-workspace",
                "Understand the workspace",
                "Learn where APEXlang source lives, how source maps to APEX concepts, and which generated knowledge files help you navigate safely.",
                "A page is an APEX screen, a region is a visual container, an item is a field, an LOV is a reusable list of values, and deployment imports source into a development application for preview.",
                "Map `application.apx`, page files, shared component folders, deployment profiles, workspace index, Atlas docs, Developer Companion docs, and Git-backed source control to the familiar Builder and SQLcl workflow.",
                null,
                null,
                [
                    "Source folders for `application.apx`, `pages`, `shared-components`, and deployment profiles are identified.",
                    "Workspace index, Atlas docs, Developer Companion docs, and synchronization status locations are clear.",
                    "Git is understood as the source of truth while APEX remains the development and preview target.",
                ],
                [
                    "Open `.opencode/knowledge/apexlang-atlas/workspace-index.json` and confirm application, pages, shared components, and diagnostics are present.",
                    "Open `.opencode/knowledge/apexlang-atlas/docs/oracle-apex-atlas.md` and `.opencode/knowledge/apex-developers-companion/prompts/compact-context.md`.",
                    "Manually confirm you can explain where a page, LOV, deployment profile, and validation would be stored in source.",
                ],
                [
                    "If the workspace index is missing, run the existing validation or Atlas generation flow before editing.",
                    "If Builder or app links are unknown, keep placeholder URLs until the development environment is configured.",
                ]),
            new TourLesson(
                "inspect-before-editing",
                "Inspect before editing",
                "Start with a no-change inspection pass that proves the agent can read the current workspace model, deployment target, and synchronization state.",
                "This is like opening Page Designer and Shared Components before changing anything, except the agent reads the source model and generated knowledge first.",
                "Use this to verify the agent understands the current application structure, deployment profile, and synchronization state before planning changes.",
                null,
                $"Inspect the workspace index first. Identify the current application structure, existing pages, shared components, active deployment profile, Builder and application links when available, and the current synchronization state. Make no changes. {commonPromptRules}",
                [
                    "The agent reports current pages, shared components, deployment profile, synchronization state, and any existing diagnostics.",
                    "No files are changed.",
                ],
                [
                    "Expected semantic components: application, pages, shared components, deployment profiles.",
                    "Expected source files: `application.apx`, page files, shared component files, deployment profile files.",
                    "Manual success check: the agent explicitly states that no change was made.",
                ],
                [
                    "If synchronization is `DeploymentAhead` or `Diverged`, pause generation work until the drift is reviewed.",
                    "If the deployment profile is unclear, inspect the workspace index and deployment-profile source before proceeding.",
                ]),
            new TourLesson(
                "create-or-connect-application",
                "Create or connect the development application",
                "Confirm the development target application, workspace, schema, profile reference, Builder URL, and application URL before building the tutorial app.",
                "You need one safe development application where source can be validated and imported without touching unrelated production data.",
                "Treat this as the development import target setup step: application ID, workspace, parsing schema, profile reference, Builder URL, and application URL must all be known.",
                "Use the existing workspace docs and local-only development-loop example to confirm Builder and running-application URLs without committing secrets.",
                $"Inspect the workspace index first, then help me create a development application from source or connect an existing development APEX application for this workspace. Report the application ID, workspace, schema, deployment profile reference, Builder URL, and application URL. Make no source changes until I approve them. {commonPromptRules}",
                [
                    "The agent reports or confirms application ID, workspace, schema, deployment profile reference, Builder URL, and application URL.",
                    "No secrets are written into repository files.",
                ],
                [
                    "Expected source files: deployment profile and workspace docs only, unless a controlled connection change is approved.",
                    "Expected synchronization state: still stable and reviewable.",
                    "Manual success check: Builder opens the intended development application and the running application URL resolves.",
                ],
                [
                    "If URLs are placeholders, update only local environment configuration, not committed source files.",
                    "If application ID or schema are mismatched, stop and resolve the target before creating pages.",
                ]),
            new TourLesson(
                "add-equipment-report-page",
                "Add the Equipment report page",
                "Create the first user-facing page: a report of Equipment records with navigation and plan review before editing.",
                "A report page is the APEX screen that lists rows. Here it shows id, name, category, serial number, status, and purchase date.",
                "This maps to a page plus a report region and a navigation entry, with changed files and validation output reviewed before deployment.",
                null,
                $"Add an Equipment report page using the existing semantic APEXlang workflow. Show id, name, category, serial number, status, and purchase date. Add it to the main navigation. Build a semantic plan first and do not deploy until I approve it. {commonPromptRules}",
                [
                    "Expected semantic plan: add page, add report region, add navigation entry, update supporting source if required.",
                    "Expected source files: a new page file, a navigation/shared-component file if needed, and any application-level references.",
                    "Expected validation result: clean plan review or explicit diagnostics before import.",
                ],
                [
                    "Expected semantic components: page, region, navigation entry.",
                    "Expected workspace-index changes: new page node, new region node, and navigation references.",
                    "Expected synchronization state: local changes pending, not silently imported.",
                    "Manual success check: after approved import, the Equipment report appears in the main navigation and opens successfully.",
                ],
                [
                    "If the report source table or query is not yet present, have the agent stop at planning and explain the dependency.",
                    "If validation flags a missing required property, generate a repair plan instead of hand-editing source.",
                ]),
            new TourLesson(
                "add-equipment-form-page",
                "Add the Equipment form page",
                "Add a form page with page items, save/cancel buttons, processing, and navigation from the report page to a form page.",
                "A form page is the APEX screen where a user edits one Equipment record through page items and save processing.",
                "Relate this to Builder as page items, buttons, processes, and links from the report page to the form page in a classic create/update flow.",
                null,
                $"Add an Equipment form page using the semantic APEXlang workflow. Include page items for id, name, category, serial number, status, and purchase date, plus save and cancel buttons, basic create/update processing, and navigation from the Equipment report page to the form page. Show the plan before editing and do not deploy until I approve it. {commonPromptRules}",
                [
                    "Expected plan: add form page, add items, add buttons, add create/update processing, update report-to-form navigation.",
                    "Expected source files: new page file and updates to the report page or shared navigation definitions.",
                    "Expected validation result: page items and processes resolve cleanly or produce actionable diagnostics.",
                ],
                [
                    "Expected semantic components: page, item, button, process, branch or navigation reference.",
                    "Expected workspace-index changes: the form page and its items/processes appear.",
                    "Expected synchronization state: local changes awaiting validation/import.",
                    "Manual success check: report page opens the form page, and save/cancel behavior matches the plan after import.",
                ],
                [
                    "If item names or processing assumptions are unclear, require the agent to surface unresolved questions before editing.",
                    "If Builder concepts and source blocks feel disconnected, cross-check the Developer Companion APEXlang workflow guidance.",
                ]),
            new TourLesson(
                "add-shared-status-lov",
                "Add the shared Equipment Status LOV",
                "Create or reuse a shared LOV for Equipment status values so both report and form pages use the same source of truth.",
                "A shared LOV is a reusable value list. Here it should expose Available, Assigned, Service, and Retired.",
                "Treat this as a shared component mapping exercise: the agent should reuse an existing LOV when possible rather than duplicating configuration.",
                null,
                $"Add a shared Equipment Status LOV with the values Available, Assigned, Service, and Retired. Reuse an existing LOV if one already fits instead of duplicating it. Update the Equipment form page and report page to use the shared semantic APEXlang workflow. Show the plan first and do not deploy until I approve it. {commonPromptRules}",
                [
                    "Expected plan: reuse or create LOV, update page items and report display as needed.",
                    "Expected source files: shared component LOV file plus any affected page files.",
                    "Expected validation result: shared component references resolve and enum or LOV diagnostics remain clean.",
                ],
                [
                    "Expected semantic components: LOV, page item references, report display settings.",
                    "Expected workspace-index changes: shared LOV appears and is referenced by pages.",
                    "Expected synchronization state: still safe for review prior to import.",
                    "Manual success check: status values display consistently on both pages after import.",
                ],
                [
                    "If an existing LOV is close but not identical, have the agent explain reuse versus create-new tradeoffs before editing.",
                    "If the LOV reference format is unclear, consult the local component-references guidance before changing source.",
                ]),
            new TourLesson(
                "add-validation",
                "Add the Assigned serial-number validation",
                "Add a validation that requires serial number when status is Assigned, then inspect planned behavior and SQLcl validation output.",
                "A validation is a rule checked before save. Here the rule is: if status is Assigned, serial number must be present.",
                "Use this to examine semantic planning, diagnostics, and SQLcl validation around a realistic business rule.",
                null,
                $"Add a validation that requires serial number whenever Equipment status is Assigned. Show the planned semantic operation, expected diagnostic behavior, and SQLcl validation outcome before importing anything. {commonPromptRules}",
                [
                    "Expected plan: add or update a validation tied to the Equipment form page and relevant items.",
                    "Expected source files: form page validation block and related item definitions if needed.",
                    "Expected validation result: SQLcl validation is clean after the rule is added, or explicit diagnostics are shown before repair.",
                ],
                [
                    "Expected semantic components: validation, page items, process flow dependencies.",
                    "Expected workspace-index changes: a validation node appears on the form page.",
                    "Expected synchronization state: local change pending until reviewed and imported.",
                    "Manual success check: after import, Assigned without serial number produces the expected message while other statuses save normally.",
                ],
                [
                    "If the validation references the wrong item scope, use the workspace index to confirm item identifiers before editing.",
                    "If SQLcl validation fails, build a repair plan instead of bypassing the error.",
                ]),
            new TourLesson(
                "validate-and-deploy",
                "Validate and deploy",
                "Walk through Apply and Validate, diagnostics inspection, repair planning, revalidation, import, and opening the running application.",
                "Validation checks whether the source is acceptable before import. Import moves the reviewed source into the configured development application.",
                "This is the core source-driven loop: apply the semantic change, inspect compiler diagnostics, repair if necessary, revalidate, import, and preview.",
                null,
                $"Apply the approved Equipment Register changes with the semantic workflow, validate before import, inspect compiler diagnostics, build a repair plan if validation fails, revalidate after repair, then import into the configured development environment and report the Builder URL, running application URL, changed files, diagnostics, and synchronization state. {commonPromptRules}",
                [
                    "Expected result: applied semantic changes, validation output, optional repair plan, successful revalidation, and import into the development environment.",
                    "Expected source files: all changed page, LOV, validation, navigation, and deployment-related files are listed.",
                    "Expected synchronization state: In Sync or an explicitly explained post-import state.",
                ],
                [
                    "Expected validation result: clean SQLcl validation before import or a documented repair cycle.",
                    "Expected workspace-index changes: refreshed nodes and diagnostics after apply/repair.",
                    "Manual success check: the running application opens and the Equipment Register create/update flow works.",
                ],
                [
                    "If import is blocked by diagnostics, stop and repair instead of forcing deployment.",
                    "If the running application differs from source after import, inspect synchronization status before making more edits.",
                ]),
            new TourLesson(
                "builder-to-git-round-trip",
                "Builder-to-Git round trip",
                "Make one small Builder-side change, export it back into source, detect drift, review diffs, and confirm the workspace index refreshes cleanly.",
                "This teaches that APEX Builder is a development and preview target, but Git-backed source remains authoritative after controlled export and review.",
                "Use a safe, tiny Builder change such as a page-title update so drift detection, export, and diff review stay easy to reason about.",
                null,
                $"I changed a page title in APEX Builder. Inspect the workspace index and synchronization state first, detect whether the workspace is Deployment Ahead or Diverged, export the Builder change back into source without overwriting Git changes silently, show the source differences, refresh the workspace index, and report the new synchronization state. {commonPromptRules}",
                [
                    "Expected result: export from APEX, explicit drift detection, reviewed diff, and refreshed workspace index.",
                    "Expected source files: the changed page file and any synchronization artifacts produced by the existing workflow.",
                    "Expected synchronization state: drift explained and then reduced or resolved through a controlled pull from Builder to source.",
                ],
                [
                    "Expected validation result: either unchanged or rechecked if export introduced structural changes.",
                    "Expected workspace-index changes: updated page title and refreshed synchronization metadata.",
                    "Manual success check: Git diff shows only the intended Builder-side page-title change.",
                ],
                [
                    "If the state is Diverged, review source and Builder changes before accepting either side.",
                    "Never allow the agent to silently overwrite local Git edits to resolve synchronization drift.",
                ]),
            new TourLesson(
                "agent-enhancement",
                "Agent enhancement exercise",
                "Use a higher-level intent prompt to add a dashboard card summary for Equipment status counts while still requiring planning and validation discipline.",
                "This tests whether the agent can expand a more abstract request into the right APEX pages, regions, and shared-component reuse.",
                "This is the acceptance test for higher-level intent expansion and blueprint behavior: the agent should reuse the status LOV and navigation instead of rebuilding known structures.",
                null,
                $"Add a dashboard card showing the number of available, assigned, service, and retired equipment records. Reuse the existing status LOV and navigation. Plan before editing. {commonPromptRules}",
                [
                    "Expected plan: add or update a dashboard page or region, reuse existing status LOV, and keep navigation coherent.",
                    "Expected source files: one or more page files plus any navigation updates if needed.",
                    "Expected validation result: planned enhancements validate before import and report diagnostics clearly.",
                ],
                [
                    "Expected semantic components: page, region, LOV reuse, navigation references.",
                    "Expected workspace-index changes: new dashboard node or region nodes appear.",
                    "Expected synchronization state: reviewable local changes until imported.",
                    "Manual success check: the dashboard card renders the four counts after import.",
                ],
                [
                    "If the agent proposes duplicate LOVs or raw source edits, reject the plan and ask for a semantic rewrite.",
                    "If the requested aggregation query is unclear, have the agent stop at planning and explain assumptions.",
                ]),
            new TourLesson(
                "error-and-repair-exercise",
                "Error and repair exercise",
                "Create one safe, intentional configuration error so you can observe diagnostics, semantic mapping, repair planning, and revalidation.",
                "A safe exercise is to omit one required non-destructive value on a newly added component instead of corrupting existing application data.",
                "This is the best place to validate the repair-plan workflow: produce one known invalid planned construct, inspect diagnostics, apply a repair, and revalidate.",
                null,
                $"Create a safe tutorial-only error by omitting one required value on a newly added Equipment Register component. Show the compiler diagnostic, map it back to the semantic component, build a repair plan, apply the repair through the semantic workflow, and revalidate. Do not introduce an error that risks unrelated application data. {commonPromptRules}",
                [
                    "Expected result: one intentional validation error, a clear diagnostic, a repair plan, a semantic repair, and a clean revalidation.",
                    "Expected source files: only the tutorial-targeted Equipment Register files are touched.",
                    "Expected validation result: failing SQLcl validation before repair and passing validation after repair.",
                ],
                [
                    "Expected semantic components: whichever tutorial component was intentionally left incomplete, plus a repair operation that restores validity.",
                    "Expected workspace-index changes: diagnostic appears, then disappears after repair and refresh.",
                    "Expected synchronization state: controlled local change, then clean validated state.",
                    "Manual success check: diagnostics are understandable and the repaired application still works afterward.",
                ],
                [
                    "If the planned error affects existing shared components broadly, choose a smaller tutorial-local target instead.",
                    "If the agent tries to fix the problem by editing raw `.apx` text, stop and require semantic repair planning.",
                ]),
            new TourLesson(
                "rollback-exercise",
                "Rollback exercise",
                "Generate one small assistant change, verify rollback availability, roll back only assistant-touched files, and confirm workspace index and synchronization state afterward.",
                "Rollback should be narrow and reviewable, not a destructive reset of unrelated work.",
                "Use this to prove the assistant rollback flow can cleanly reverse its own work while preserving unrelated workspace changes.",
                null,
                $"Make one small assistant-generated Equipment Register change, report rollback availability, roll back only the assistant-touched files, refresh the workspace index, and report the resulting synchronization state and changed files. {commonPromptRules}",
                [
                    "Expected result: a small assistant change is created, rollback is confirmed, rollback is applied only to assistant-touched files, and post-rollback state is reported.",
                    "Expected validation result: workspace remains reviewable and any required validation or synchronization refresh is reported.",
                    "Expected synchronization state: refreshed after rollback, with no silent overwrite of unrelated work.",
                ],
                [
                    "Expected source files: only assistant-touched files are reverted.",
                    "Expected workspace-index changes: refreshed to reflect the rollback.",
                    "Manual success check: the small generated change is gone, but unrelated changes remain intact.",
                ],
                [
                    "If rollback is unavailable, inspect why the assistant did not create rollback evidence before attempting manual reversal.",
                    "Do not use destructive Git commands as a substitute for assistant rollback unless explicitly requested by the user.",
                ]),
        ];
    }

    private static string Escape(string value)
        => value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    private sealed record TourLesson(
        string Id,
        string Title,
        string Overview,
        string BeginnerTrack,
        string ExperiencedTrack,
        string? Command,
        string? Prompt,
        IReadOnlyList<string> ExpectedResult,
        IReadOnlyList<string> Verification,
        IReadOnlyList<string> Troubleshooting);

    private sealed class TutorialMetadata
    {
        public string TutorialVersion { get; init; } = string.Empty;
        public IReadOnlyList<string> LessonIdentifiers { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> ExpectedCapabilities { get; init; } = Array.Empty<string>();
    }
}
