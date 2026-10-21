'use strict';

// This controller is checked out from the trusted workflow SHA, never the PR
// head or the requested source. Requests are data, not executable instructions.
const { createHash } = require('node:crypto');
const { readFileSync, appendFileSync, writeFileSync } = require('node:fs');
const { join } = require('node:path');
const REPOSITORY = 'hope140/EmbyWatchTogether';
const REPOSITORY_ID = 1326100928;
const FIELDS = ['schema', 'tag', 'channel', 'keyId', 'sourceSha', 'ciRunId',
  'title', 'notesPath', 'notesSha256'];
class PolicyError extends Error {}
const fail = message => { throw new PolicyError(message); };
const check = (condition, message) => { if (!condition) fail(message); };
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const strictText = bytes => new TextDecoder('utf-8', { fatal: true, ignoreBOM: true }).decode(bytes);

function parseRequest(text, path) {
  check(Buffer.byteLength(text) <= 16384, 'Request exceeds 16 KiB.');
  const r = JSON.parse(text);
  check(r && !Array.isArray(r) && typeof r === 'object', 'Request must be an object.');
  check(Object.keys(r).length === FIELDS.length && FIELDS.every(k => Object.hasOwn(r, k)),
    'Unexpected or missing request fields.');
  // Canonical formatting also rejects duplicate keys and ambiguous review text.
  check(text === JSON.stringify(r, null, 2) + '\n', 'Request must be canonical two-space JSON with one final LF.');
  check(r.schema === 1, 'Unsupported request schema.');
  check(typeof r.tag === 'string' && /^v(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)$/.test(r.tag),
    'Tag must be canonical v<four numeric parts>.');
  check(r.tag.slice(1).split('.').every(n => Number(n) <= 65534), 'Assembly version component is out of range.');
  check(path === `.github/release-requests/${r.tag}.json`, 'Request path must be derived from its canonical tag.');
  check(['stable', 'beta'].includes(r.channel), 'Unknown release channel.');
  check(r.keyId === 'prod-2026-08', 'Unapproved signing key ID.');
  check(typeof r.sourceSha === 'string' && /^[0-9a-f]{40}$/.test(r.sourceSha), 'Source must be a full immutable SHA.');
  check(Number.isSafeInteger(r.ciRunId) && r.ciRunId > 0, 'Source CI run ID is invalid.');
  check(typeof r.title === 'string' && r.title.trim() === r.title && r.title.length > 0 &&
    r.title.length <= 200 && !/[\u0000-\u001f\u007f]/.test(r.title), 'Release title is invalid.');
  check(r.notesPath === `docs/releases/${r.tag}.md`, 'Notes path must be derived from the tag.');
  check(typeof r.notesSha256 === 'string' && /^[0-9a-f]{64}$/.test(r.notesSha256), 'Notes SHA-256 is invalid.');
  return r;
}

function checkNotes(bytes, request) {
  check(bytes.length > 0 && bytes.length <= 65536 && !bytes.subarray(0, 3).equals(Buffer.from([239, 187, 191])),
    'Notes must be nonempty UTF-8 without BOM, at most 64 KiB.');
  check(/[\u3400-\u9fff]/.test(strictText(bytes)), 'Reviewed notes must contain Chinese text.');
  check(sha256(bytes) === request.notesSha256, 'Reviewed notes SHA-256 differs from the source.');
}

function checkVersion(text, request) {
  for (const field of ['Version', 'FileVersion', 'AssemblyVersion']) {
    const matches = [...text.matchAll(new RegExp(`<${field}>([^<]+)</${field}>`, 'g'))];
    check(matches.length === 1 && matches[0][1] === request.tag.slice(1), `Source ${field} differs from the tag.`);
  }
}

function checkRun(run, sha, branch) {
  check(run.repository?.id === REPOSITORY_ID && run.head_repository?.id === REPOSITORY_ID &&
    run.head_sha === sha && run.path === '.github/workflows/ci.yml' && run.status === 'completed' &&
    run.conclusion === 'success' && (!branch || (run.head_branch === branch && run.event === 'push')),
  'Required repository CI did not succeed for the exact source SHA and branch.');
}

function makeApi(token, fetchImpl = fetch) {
  check(typeof token === 'string' && token.length > 0, 'GitHub runtime token is unavailable.');
  return async (path, { method = 'GET', body, allow404 = false } = {}) => {
    check(path.startsWith('/') && !/(?:^|\/)\.\.?(?:\/|\?|$)/.test(path), 'Invalid fixed repository API path.');
    const response = await fetchImpl(`https://api.github.com/repos/${REPOSITORY}${path}`, {
      method, redirect: 'error',
      headers: { Accept: 'application/vnd.github+json', Authorization: `Bearer ${token}`,
        'X-GitHub-Api-Version': '2022-11-28', 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
      signal: AbortSignal.timeout(30000)
    });
    if (allow404 && response.status === 404) return null;
    // Never echo headers, tokens, response bodies, or private exception details.
    check(response.ok, `GitHub API ${method} failed with HTTP ${response.status}.`);
    return response.json();
  };
}

async function readRegularFile(api, path, sha) {
  const entry = await api(`/contents/${path}?ref=${sha}`);
  check(entry.type === 'file' && entry.encoding === 'base64' && typeof entry.content === 'string',
    'Requested content must be a regular file.');
  // Contents API dereferences symlinks; use the Git tree to reject them explicitly.
  const tree = await api(`/git/trees/${sha}?recursive=1`);
  check(!tree.truncated && tree.tree.some(e => e.path === path && e.type === 'blob' && e.mode === '100644'),
    'Requested content must be a non-executable regular Git blob.');
  return Buffer.from(entry.content, 'base64');
}

async function tagCommit(api, tag) {
  let ref = await api(`/git/ref/tags/${tag}`, { allow404: true });
  if (!ref) return null;
  let object = ref.object;
  for (let i = 0; object.type === 'tag' && i < 8; i++) {
    object = (await api(`/git/tags/${object.sha}`)).object;
  }
  check(object.type === 'commit', 'Tag does not resolve to a commit.');
  return object.sha;
}

async function checkStableVersion(api, request) {
  if (request.channel !== 'stable') return;
  const current = request.tag.slice(1).split('.').map(Number);
  const compare = (a, b) => {
    for (let i = 0; i < 4; i++) if (a[i] !== b[i]) return a[i] > b[i] ? 1 : -1;
    return 0;
  };
  let highest = null;
  for (let page = 1; ; page++) {
    const releases = await api(`/releases?per_page=100&page=${page}`);
    check(Array.isArray(releases), 'Invalid Releases history response.');
    for (const release of releases) {
      if (release.draft || release.prerelease || typeof release.tag_name !== 'string' ||
          !/^v(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)$/.test(release.tag_name)) continue;
      const parts = release.tag_name.slice(1).split('.').map(Number);
      if (!parts.every(n => Number.isSafeInteger(n) && n <= 2147483647)) continue;
      if (!highest || compare(parts, highest) > 0) highest = parts;
    }
    if (releases.length < 100) break;
  }
  if (highest) check(compare(current, highest) > 0 &&
    current.slice(0, 3).some((n, i) => n !== highest[i]),
  'Stable must increase the highest formal version and change MAJOR, MINOR or PATCH.');
}

async function checkUnpublished(api, request) {
  const sha = await tagCommit(api, request.tag);
  check(sha === null || sha === request.sourceSha, 'Existing tag points to a different SHA; refusing to overwrite.');
  check(await api(`/releases/tags/${request.tag}`, { allow404: true }) === null,
    'Release already exists; refusing to overwrite any release or assets.');
  return sha;
}

async function validate({ eventName, repository, event, api }) {
  check(eventName === 'pull_request_target' && repository === REPOSITORY &&
    event.action === 'closed' && event.pull_request?.merged === true &&
    event.repository?.id === REPOSITORY_ID, 'Only merged release-request PR events are authorized.');
  const number = event.number;
  check(Number.isSafeInteger(number) && number > 0, 'Invalid PR number.');
  const pr = await api(`/pulls/${number}`);
  check(pr.merged === true && pr.state === 'closed' && pr.base.ref === 'main' &&
    pr.base.repo.id === REPOSITORY_ID && pr.head.repo?.id === REPOSITORY_ID &&
    pr.merge_commit_sha === event.pull_request.merge_commit_sha &&
    /^[0-9a-f]{40}$/.test(pr.merge_commit_sha), 'PR merge identity differs from the event.');
  check(pr.changed_files === 1, 'A release-request PR may only add its one request file.');
  const files = await api(`/pulls/${number}/files?per_page=100`);
  check(files.length === 1 && files[0].status === 'added' && !files[0].previous_filename &&
    /^\.github\/release-requests\/v[0-9.]+\.json$/.test(files[0].filename),
    'A release-request PR must add exactly one canonical request file.');
  const path = files[0].filename;
  const r = parseRequest(strictText(await readRegularFile(api, path, pr.merge_commit_sha)), path);
  check(pr.head.ref === `release-request/${r.tag}`, 'Release requests require the dedicated version branch.');
  const branch = r.channel === 'stable' ? 'main' : 'beta';
  const comparison = await api(`/compare/${r.sourceSha}...${branch}`);
  check(['ahead', 'identical'].includes(comparison.status) && comparison.base_commit?.sha === r.sourceSha,
    'Source SHA is not an ancestor of its authorized channel branch.');
  // Pin the successful source CI run; do not infer success from a branch name.
  checkRun(await api(`/actions/runs/${r.ciRunId}`), r.sourceSha, branch);
  const ci = await api(`/actions/workflows/ci.yml/runs?head_sha=${pr.head.sha}&event=pull_request&status=success&per_page=100`);
  const requestRun = ci.workflow_runs.find(run => run.head_sha === pr.head.sha &&
    run.event === 'pull_request' && (!run.pull_requests?.length || run.pull_requests.some(p => p.number === number)));
  check(requestRun, 'The exact release-request PR head has no successful CI run.');
  checkRun(requestRun, pr.head.sha);
  checkVersion(strictText(await readRegularFile(api, 'src/EmbyWatchTogether/EmbyWatchTogether.csproj', r.sourceSha)), r);
  const notes = await readRegularFile(api, r.notesPath, r.sourceSha);
  checkNotes(notes, r);
  const trust = strictText(await readRegularFile(api, 'src/EmbyWatchTogether/ReleaseTrustStore.cs', r.sourceSha));
  check(trust.includes(`["${r.keyId}"] = "<RSAKeyValue>`), 'Key ID is not mapped in the source trust store.');
  await checkStableVersion(api, r);
  await checkUnpublished(api, r);
  return { request: r, path };
}

async function ensureTag(api, request) {
  const existing = await checkUnpublished(api, request);
  if (existing === null) {
    // No update/force/delete endpoint is used. A race fails closed; re-runs
    // re-read remote state and may reuse only the identical tag without a Release.
    await api('/git/refs', { method: 'POST', body: { ref: `refs/tags/${request.tag}`, sha: request.sourceSha } });
  }
  check(await tagCommit(api, request.tag) === request.sourceSha, 'Tag verification failed.');
}

async function materializeNotes(api, request, directory) {
  const bytes = await readRegularFile(api, request.notesPath, request.sourceSha);
  checkNotes(bytes, request);
  const file = join(directory, 'EWT-reviewed-release-notes.md');
  writeFileSync(file, bytes);
  return file;
}

async function main() {
  const mode = process.argv[2];
  check(['validate', 'create-tag', 'verify-tag'].includes(mode), 'Unknown controller mode.');
  const api = makeApi(process.env.GH_TOKEN);
  const result = await validate({ eventName: process.env.GITHUB_EVENT_NAME,
    repository: process.env.GITHUB_REPOSITORY,
    event: JSON.parse(readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8')), api });
  const r = result.request;
  const outputs = { tag: r.tag, channel: r.channel, key_id: r.keyId,
    request_path: result.path, source_sha: r.sourceSha, release_title: r.title, notes_sha256: r.notesSha256 };
  if (mode === 'create-tag' || mode === 'verify-tag') {
    for (const [key, value] of Object.entries(outputs)) {
      const envKey = { key_id: 'RELEASE_KEY_ID', request_path: 'RELEASE_REQUEST_PATH',
        source_sha: 'RELEASE_SOURCE_SHA', release_title: 'RELEASE_TITLE',
        notes_sha256: 'RELEASE_NOTES_SHA256' }[key] || `RELEASE_${key.toUpperCase()}`;
      check(process.env[envKey] === value, 'Reusable workflow inputs differ from the reviewed request.');
    }
    if (mode === 'create-tag') await ensureTag(api, r);
    else {
      check(await tagCommit(api, r.tag) === r.sourceSha, 'Remote tag differs from the reviewed source before publishing.');
      // Use original Git blob bytes, independent of Windows checkout CRLF conversion.
      await materializeNotes(api, r, process.env.RUNNER_TEMP);
    }
  } else {
    for (const [key, value] of Object.entries(outputs)) appendFileSync(process.env.GITHUB_OUTPUT, `${key}=${value}\n`);
  }
  console.log(`Reviewed release request validated: ${r.tag}, source ${r.sourceSha}.`);
}

module.exports = { parseRequest, checkNotes, checkVersion, checkRun, validate, ensureTag, sha256, makeApi, materializeNotes };
if (require.main === module) main().catch(error => {
  // Static error messages only; no fetch exception/header/response data.
  console.error(error instanceof PolicyError ? error.message : 'Release request failed due to an unexpected I/O or parsing error.');
  process.exitCode = 1;
});
