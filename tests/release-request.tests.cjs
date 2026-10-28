'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { parseRequest, checkNotes, checkVersion, checkRun, validate, ensureTag, sha256, makeApi, materializeNotes } =
  require('../scripts/release/release-request.cjs');
const root = path.resolve(__dirname, '..');
const repo = { id: 1326100928 };
const source = 'a'.repeat(40), merge = 'b'.repeat(40), head = 'c'.repeat(40);
const notes = Buffer.from('# 正式版\n\n中文发布说明：保持原文。\n');
const requestPath = '.github/release-requests/v1.7.1.0.json';
const request = { schema: 1, tag: 'v1.7.1.0', channel: 'stable', keyId: 'prod-2026-08',
  sourceSha: source, ciRunId: 100, title: 'Watch Together v1.7.1.0 正式版',
  notesPath: 'docs/releases/v1.7.1.0.md', notesSha256: sha256(notes) };
const json = r => JSON.stringify(r, null, 2) + '\n';
const run = { repository: repo, head_repository: repo, head_sha: source, head_branch: 'main',
  event: 'push', path: '.github/workflows/ci.yml', status: 'completed', conclusion: 'success' };
const pr = { merged: true, state: 'closed', base: { ref: 'main', repo, sha: 'd'.repeat(40) },
  head: { ref: 'release-request/v1.7.1.0', repo, sha: head }, merge_commit_sha: merge, changed_files: 1 };
const event = { action: 'closed', number: 27, repository: repo, pull_request: { merged: true, merge_commit_sha: merge } };
const project = '<Project>' + ['Version', 'FileVersion', 'AssemblyVersion']
  .map(k => `<${k}>1.7.1.0</${k}>`).join('') + '</Project>';

function fixture(overrides = {}) {
  const data = {
    [`/pulls/27`]: pr,
    ['/pulls/27/files?per_page=100']: [{ status: 'added', filename: requestPath }],
    [`/compare/${source}...main`]: { status: 'ahead', base_commit: { sha: source } },
    ['/actions/runs/100']: run,
    ['/releases?per_page=100&page=1']: [{ tag_name: 'v1.6.1.0', draft: false, prerelease: false }],
    [`/actions/workflows/ci.yml/runs?head_sha=${head}&event=pull_request&status=success&per_page=100`]: {
      workflow_runs: [{ ...run, head_sha: head, event: 'pull_request', pull_requests: [{ number: 27 }] }] },
    [`/git/ref/tags/${request.tag}`]: null,
    [`/releases/tags/${request.tag}`]: null,
  };
  const content = { [requestPath]: json(request), [request.notesPath]: notes,
    'src/EmbyWatchTogether/EmbyWatchTogether.csproj': project,
    'src/EmbyWatchTogether/ReleaseTrustStore.cs': '["prod-2026-08"] = "<RSAKeyValue>public</RSAKeyValue>"' };
  for (const [file, bytes] of Object.entries(content)) {
    data[`/contents/${file}?ref=${file === requestPath ? merge : source}`] = {
      type: 'file', encoding: 'base64', content: Buffer.from(bytes).toString('base64') };
  }
  for (const ref of [merge, source]) data[`/git/trees/${ref}?recursive=1`] = {
    truncated: false, tree: Object.keys(content).map(p => ({ path: p, type: 'blob', mode: '100644' })) };
  Object.assign(data, overrides);
  const calls = [];
  const api = async (url, options = {}) => {
    calls.push({ url, options });
    if (url === '/git/refs' && options.method === 'POST') {
      data[`/git/ref/tags/${request.tag}`] = { object: { type: 'commit', sha: options.body.sha } };
      return data[`/git/ref/tags/${request.tag}`];
    }
    assert.ok(Object.hasOwn(data, url), `Unexpected API URL: ${url}`);
    return structuredClone(data[url]);
  };
  return { api, calls, data, args: { eventName: 'pull_request_target', repository: 'hope140/EmbyWatchTogether', event, api } };
}

test('normal merged dedicated request is fully bound and validation is read-only', async () => {
  const f = fixture();
  const result = await validate(f.args);
  assert.deepEqual(result, { request, path: requestPath });
  assert.ok(f.calls.every(c => !c.options.method));
  assert.equal(result.request.title, request.title);
  assert.equal(sha256(notes), result.request.notesSha256);
});
for (const mode of ['push', 'pull_request', 'workflow_dispatch', 'workflow_run']) {
  test(`${mode} cannot authorize a request even with an existing request file`, async () => {
    const f = fixture();
    await assert.rejects(validate({ ...f.args, eventName: mode }), /Only merged/);
    assert.equal(f.calls.length, 0);
  });
}
test('unmerged, wrong repository and mismatched merge identity are rejected', async () => {
  for (const extra of [{ event: { ...event, pull_request: { ...event.pull_request, merged: false } } },
    { repository: 'another/repo' }]) {
    await assert.rejects(validate({ ...fixture().args, ...extra }));
  }
  await assert.rejects(validate(fixture({ '/pulls/27': { ...pr, merge_commit_sha: 'd'.repeat(40) } }).args));
});
for (const changed of [
  { ...pr, changed_files: 2 },
  { ...pr, base: { ...pr.base, ref: 'beta' } },
  { ...pr, head: { ...pr.head, repo: { id: 1 } } },
  { ...pr, head: { ...pr.head, ref: 'ordinary-feature' } }
]) test('wrong PR scope/branch or mixed changes fail closed ' + JSON.stringify(changed), async () => {
  await assert.rejects(validate(fixture({ '/pulls/27': changed }).args));
});
test('modified or renamed old request cannot trigger a new publication', async () => {
  for (const status of ['modified', 'renamed', 'removed']) await assert.rejects(validate(fixture({
    '/pulls/27/files?per_page=100': [{ status, filename: requestPath }]
  }).args));
});
for (const [field, value] of [['schema', 2], ['tag', 'v01.7.1.0'], ['tag', 'v1.7.1'],
  ['tag', 'v1.7.1.65535'], ['sourceSha', 'main'], ['sourceSha', '../main'], ['channel', 'preview'],
  ['keyId', 'unknown'], ['ciRunId', -1], ['title', 'bad\ntitle'], ['notesPath', '../../secret'],
  ['notesPath', 'docs/releases/other.md'], ['notesSha256', 'BAD']]) {
  test(`invalid ${field}: ${JSON.stringify(value)}`, () => assert.throws(() =>
    parseRequest(json({ ...request, [field]: value }), requestPath)));
}
test('extra, duplicate JSON keys and unsafe request paths are rejected', () => {
  assert.throws(() => parseRequest(json({ ...request, command: 'run me' }), requestPath));
  assert.throws(() => parseRequest(json(request).replace('"schema": 1,', '"schema": 1,\n  "schema": 1,'), requestPath));
  for (const p of ['../v1.7.1.0.json', '.github/release-requests/v1.7.1.0.json/extra'])
    assert.throws(() => parseRequest(json(request), p));
});
test('failed, unrelated, cancelled or wrong SHA CI cannot authorize source', async () => {
  for (const extra of [{ conclusion: 'failure' }, { status: 'in_progress' }, { head_sha: head },
    { path: '.github/workflows/other.yml' }, { event: 'pull_request' }, { head_branch: 'beta' },
    { head_repository: { id: 1 } }]) {
    await assert.rejects(validate(fixture({ '/actions/runs/100': { ...run, ...extra } }).args), /CI/);
  }
});
test('merged PR CI remains valid when GitHub clears its pull_requests association', async () => {
  const f = fixture();
  const runs = f.data[`/actions/workflows/ci.yml/runs?head_sha=${head}&event=pull_request&status=success&per_page=100`];
  runs.workflow_runs[0].pull_requests = [];
  assert.deepEqual((await validate(f.args)).request, request);
});
test('request head CI must succeed too', async () => {
  const f = fixture();
  f.data[`/actions/workflows/ci.yml/runs?head_sha=${head}&event=pull_request&status=success&per_page=100`] = { workflow_runs: [] };
  await assert.rejects(validate(f.args), /PR head/);
});
test('source must be ancestor of the channel branch', async () => {
  for (const status of ['behind', 'diverged']) await assert.rejects(validate(fixture({
    [`/compare/${source}...main`]: { status, base_commit: { sha: source } }
  }).args));
});
test('symlink or truncated tree request files are rejected', async () => {
  for (const tree of [{ truncated: true, tree: [] },
    { tree: [{ path: requestPath, mode: '120000', type: 'blob' }] }]) {
    await assert.rejects(validate(fixture({ [`/git/trees/${merge}?recursive=1`]: tree }).args), /Git blob/);
  }
});
test('version duplicates, notes changes and malformed UTF-8 fail closed', () => {
  assert.throws(() => checkVersion(project.replace('1.7.1.0', '1.7.0.1'), request));
  assert.throws(() => checkVersion(project + '<Version>1.7.1.0</Version>', request));
  for (const bytes of [Buffer.from('changed 中文'), Buffer.from([255]), Buffer.concat([Buffer.from([239,187,191]), notes])])
    assert.throws(() => checkNotes(bytes, request));
  checkNotes(notes, request);
});
test('tag is created once at the exact source and identical tag retries do not write', async () => {
  const f = fixture();
  await ensureTag(f.api, request);
  await ensureTag(f.api, request);
  const writes = f.calls.filter(c => c.options.method);
  assert.equal(writes.length, 1);
  assert.deepEqual(writes[0], { url: '/git/refs', options: { method: 'POST',
    body: { ref: 'refs/tags/v1.7.1.0', sha: source } } });
});
test('old tag and existing Release are never overwritten, including duplicate requests', async () => {
  for (const overrides of [
    { [`/git/ref/tags/${request.tag}`]: { object: { type: 'commit', sha: head } } },
    { [`/releases/tags/${request.tag}`]: { tag_name: request.tag } }
  ]) {
    const f = fixture(overrides);
    await assert.rejects(validate(f.args));
    await assert.rejects(ensureTag(f.api, request));
    assert.equal(f.calls.filter(c => c.options.method).length, 0);
  }
});
test('annotated existing tag is peeled and compared without moving it', async () => {
  const f = fixture({ [`/git/ref/tags/${request.tag}`]: { object: { type: 'tag', sha: merge } },
    [`/git/tags/${merge}`]: { object: { type: 'commit', sha: source } } });
  await ensureTag(f.api, request);
  assert.equal(f.calls.filter(c => c.options.method).length, 0);
});
test('workflow wiring preserves manual publishing and isolates trusted code from source', () => {
  const controller = fs.readFileSync(path.join(root, '.github/workflows/release-request.yml'), 'utf8');
  const release = fs.readFileSync(path.join(root, '.github/workflows/release.yml'), 'utf8');
  const ci = fs.readFileSync(path.join(root, '.github/workflows/ci.yml'), 'utf8');
  assert.match(controller, /pull_request_target:\s*\n\s+types: \[closed\]\s*\n\s+branches: \[main\]/);
  assert.match(controller, /pull_request\.merged == true/);
  assert.match(controller, /startsWith\(github.event.pull_request.head.ref, 'release-request\/'\)/);
  assert.doesNotMatch(controller, /^\s+(push|workflow_run|schedule):/m);
  assert.match(controller, /ref: \$\{\{ github.workflow_sha \}\}/);
  assert.doesNotMatch(controller, /ref:.*(?:head\.sha|inputs\.source_sha)|secrets: inherit/);
  assert.match(controller, /secrets:\s*\n\s+WATCH_TOGETHER_RELEASE_SIGNING_KEY_PKCS8_B64: \$\{\{ secrets.WATCH_TOGETHER_RELEASE_SIGNING_KEY_PKCS8_B64 \}\}/);
  assert.match(release, /workflow_call:\s*\n\s+secrets:\s*\n\s+WATCH_TOGETHER_RELEASE_SIGNING_KEY_PKCS8_B64:\s*\n\s+required: true/);
  assert.ok(release.indexOf('Require signing environment before creating any tag') < release.indexOf('Revalidate merged request and create immutable tag'));
  assert.match(release, /workflow_dispatch:/);
  assert.match(release, /workflow_call:/);
  assert.match(release, /concurrency:\s*\n\s+group: watch-together-signed-release\s*\n\s+cancel-in-progress: false/);
  assert.match(release, /environment: release/);
  assert.match(release, /ref: \$\{\{ inputs.source_sha \|\| inputs.tag \}\}\s*\n\s+path: source/);
  assert.match(release, /pwsh \(Join-Path \$env:GITHUB_WORKSPACE 'scripts\/release\/Sign-ReleaseManifest.ps1'\)/);
  assert.match(release, /RELEASE_NOTES_SHA256/);
  assert.match(release, /'--notes-file'\s*\n\s+\$releaseNotesPath/);
  assert.match(release, /'--title'\s*\n\s+\$releaseTitle/);
  assert.doesNotMatch(release, /--generate-notes|--clobber|secrets: inherit/);
  assert.match(ci, /node --test tests\/release-request.tests.cjs/);
});

test('HTTP adapter accepts compare syntax but rejects traversal before fetching', async () => {
  const urls = [];
  const api = makeApi('unit-test-placeholder', async (url, options) => {
    urls.push(url);
    assert.equal(options.method, 'GET');
    return { ok: true, json: async () => ({ status: 'ahead' }) };
  });
  assert.deepEqual(await api(`/compare/${source}...main`), { status: 'ahead' });
  assert.equal(urls.length, 1);
  assert.equal(urls[0], `https://api.github.com/repos/hope140/EmbyWatchTogether/compare/${source}...main`);
  for (const p of ['/contents/../secret', '/../user', '/./user', 'https://example.org'])
    await assert.rejects(api(p), /fixed repository API path/);
  assert.equal(urls.length, 1);
});
test('reviewed notes are materialized from exact Git blob bytes without checkout newline changes', async () => {
  const os = require('node:os');
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'ewt-notes-test-'));
  try {
    const f = fixture();
    const file = await materializeNotes(f.api, request, directory);
    assert.deepEqual(fs.readFileSync(file), notes);
    assert.equal(sha256(fs.readFileSync(file)), request.notesSha256);
  } finally { fs.rmSync(directory, { recursive: true }); }
});

test('stable monotonic and revision-only rules are checked before tag creation', async () => {
  for (const tag_name of ['v1.7.1.0', 'v1.7.1.1', 'v1.8.0.0']) {
    const f = fixture({ '/releases?per_page=100&page=1': [{ tag_name, draft: false, prerelease: false }] });
    await assert.rejects(validate(f.args), /Stable must increase/);
    assert.equal(f.calls.filter(c => c.options.method).length, 0);
  }
  const f = fixture({ '/releases?per_page=100&page=1': [
    { tag_name: 'v1.7.1.9', draft: false, prerelease: true },
    { tag_name: 'v9.0.0.0', draft: true, prerelease: false },
    { tag_name: 'v1.6.1.0', draft: false, prerelease: false }] });
  assert.deepEqual((await validate(f.args)).request, request);
});

test('a unique retry must add a byte-identical request and cannot modify reviewed publishing metadata', async () => {
  const retryPath = '.github/release-requests/v1.7.1.0.retry-2.json';
  const prepare = () => {
    const f = fixture();
    f.data['/pulls/27/files?per_page=100'] = [{ status: 'added', filename: retryPath }];
    f.data[`/contents/${retryPath}?ref=${merge}`] = structuredClone(f.data[`/contents/${requestPath}?ref=${merge}`]);
    f.data[`/git/trees/${merge}?recursive=1`].tree.push({ path: retryPath, type: 'blob', mode: '100644' });
    const originalSha = 'e'.repeat(40);
    f.data[`/commits?path=${encodeURIComponent(requestPath)}&sha=${pr.base.sha}&per_page=100&page=1`] = [{ sha: originalSha }];
    f.data[`/commits/${originalSha}`] = { files: [{ filename: requestPath, status: 'added' }] };
    f.data[`/commits/${originalSha}/pulls?per_page=100`] = [{ ...pr, merged_at: '2026-10-21T00:00:00Z' }];
    f.data[`/contents/${requestPath}?ref=${originalSha}`] = structuredClone(f.data[`/contents/${requestPath}?ref=${merge}`]);
    f.data[`/git/trees/${originalSha}?recursive=1`] = structuredClone(f.data[`/git/trees/${merge}?recursive=1`]);
    return f;
  };
  const f = prepare();
  assert.equal((await validate(f.args)).path, retryPath);
  const changedCurrent = prepare();
  changedCurrent.data[`/contents/${requestPath}?ref=${merge}`].content = Buffer.from('Changed later on main').toString('base64');
  assert.equal((await validate(changedCurrent.args)).path, retryPath);
  const changed = prepare();
  changed.data[`/contents/${retryPath}?ref=${merge}`].content = Buffer.from(json({ ...request, title: '改写标题' })).toString('base64');
  await assert.rejects(validate(changed.args), /byte-identical/);
  const duplicate = prepare();
  duplicate.data[`/releases/tags/${request.tag}`] = { tag_name: request.tag };
  await assert.rejects(validate(duplicate.args), /Release already exists/);
  for (const suffix of ['1', '0', '02', '-2', '2/extra'])
    assert.throws(() => parseRequest(json(request), `.github/release-requests/v1.7.1.0.retry-${suffix}.json`));
});
