"""Claude Code session costs from transcripts: time, tokens, tools, docs, subagents.

Run from the repo root:
  python -X utf8 tools/session_costs.py                     all sessions; summary of the last 7 days
  python -X utf8 tools/session_costs.py "<part of title>"   sessions whose title or id contains the string
  python -X utf8 tools/session_costs.py --since 2026-11-01 [--until 2026-11-05]
  --all - summary of the whole history; --json - print session rows instead of the summary.

Each run recomputes the picked sessions and rewrites their rows in artifacts/metrics/sessions.jsonl (key -
transcript id); other rows stay, no duplicates. Price is a $ estimate by RATES, fitted to the cost-state
records of transcripts; tokens are exact, from usage. Baseline and how to compare - docs/MANAGING.md.
"""
import argparse
import datetime as dt
import glob
import json
import os
import re
import statistics
import sys
from collections import defaultdict

PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TRANSCRIPTS = os.path.join(os.path.expanduser('~'), '.claude', 'projects', 'D--Dev-lekta-Wander')
OUT = os.path.join(PROJECT, 'artifacts', 'metrics', 'sessions.jsonl')

# $ per 1M tokens: input, output, cache read, cache write. Fitted to cost-state 2026-11-05 (opus +-15%).
RATES = {
    'opus': (2.2, 16.7, 0.217, 6.4),
    'fable': (8.5, 60.0, 0.85, 6.7),
    'sonnet': (1.0, 10.0, 0.1, 2.5),
    'haiku': (0.33, 3.3, 0.033, 0.83),
}
DOC_CHARS_PER_TOKEN = 2.05   # docs (Russian); checked by context growth between requests, 2026-11-05
CODE_CHARS_PER_TOKEN = 2.4   # other tool results (code, output), same check
IMAGE_CHARS = 4000           # an image in a result (~1.6k tokens) counts as text of this length
VERSION = 1                  # row format; rows of another version are recomputed
WAIT_CAP_MIN = 30            # a longer gap before the human's reply is a break, not waiting for an answer
CTX_LINE = 300000            # context line of a pack (docs/MANAGING.md)
ASK_TOOLS = ('AskUserQuestion', 'ExitPlanMode')   # call time is waiting for the human's answer, not work
DOC_PATH = re.compile(r'(?:docs[/\\][\w/\\.\-]+\.md|CLAUDE\.md|memory[/\\][\w.\-]+\.md)', re.IGNORECASE)
READ_CMD = re.compile(r'(^|[;&|(]\s*|\$\()\s*(cat|head|tail|sed -n|grep|rg|ls|find|wc|awk|for|Get-Content|gc|'
                      r'Select-String|Get-ChildItem)\b', re.IGNORECASE)
TEST_CMD = re.compile(r'check\.bat|\bdotnet\b|Wander\.Harness', re.IGNORECASE)
REWORK = re.compile(r'не так|не работает|не работают|сломал|сломан|переделай|переделать|опять|всё ещё|все еще|'
                    r'до сих пор|не то\b|неправильн|не видно|пропал|не исправ|не поправ|не сделан|не доделан',
                    re.IGNORECASE)


def _roots():
    root = PROJECT.replace('\\', '/').lower()
    posix = '/' + root[0] + root[2:] if root[1:2] == ':' else root
    return (root, posix)


ROOTS = _roots()


def rate(model):
    for key, r in RATES.items():
        if key in (model or ''):
            return r

    return (0, 0, 0, 0)


def ts(s):
    return dt.datetime.fromisoformat(s.replace('Z', '+00:00')).timestamp() if s else None


def rel_doc(path):
    """Doc path relative to the project ('docs/...', 'CLAUDE.md'), 'memory/...' or 'notes/...'; else None."""
    if not path:
        return None

    p = path.replace('\\', '/')
    low = p.lower()
    if '/.claude/projects/' in low and '/memory/' in low:
        return 'memory/' + p.rsplit('/', 1)[-1]

    if '/side-session-notes/' in low:
        return 'notes/' + p.rsplit('/', 1)[-1]

    for root in ROOTS:
        if low.startswith(root + '/'):
            p = p[len(root) + 1:]
            low = low[len(root) + 1:]
            break
    if low == 'docs' or low.startswith('docs/'):
        return 'docs' + p[4:]

    if low == 'claude.md':
        return 'CLAUDE.md'

    return None


def doc_key(match):
    """A doc path found in a shell command, in the same form as rel_doc gives."""
    p = match.replace('\\', '/')
    low = p.lower()
    if low == 'claude.md':
        return 'CLAUDE.md'

    return 'docs' + p[4:] if low.startswith('docs/') else p


def in_project(path):
    low = (path or '').replace('\\', '/').lower()
    return not re.match(r'^([a-z]:|/)', low) or any(low.startswith(root) for root in ROOTS)


def category(name, inp):
    """Kind of work by a tool call."""
    inp = inp or {}
    if name in ('Read', 'Grep', 'Glob'):
        return 'docs' if rel_doc(inp.get('file_path') or inp.get('path')) else 'read_code'

    if name in ('Edit', 'Write', 'NotebookEdit', 'MultiEdit'):
        path = inp.get('file_path') or ''
        if rel_doc(path):
            return 'edit_docs'

        if in_project(path):
            return 'edit_code'

        if path.endswith('.html'):
            return 'artifact'

        return 'scripts' if path.endswith(('.py', '.cs', '.sh', '.ps1', '.js')) else 'edit_docs'

    if name in ('Bash', 'PowerShell'):
        return bash_category(inp.get('command', ''))

    if name.startswith('mcp__rider__'):
        return 'read_code'

    if name in ('Agent', 'Task', 'SendMessage', 'TaskStop', 'TaskOutput', 'ListAgents'):
        return 'agents'

    if name.startswith(('mcp__ccd_session', 'mcp__ccd_session_mgmt')):
        return 'sessions'

    if name.startswith('mcp__ccd_turn__') or name in ('TodoWrite',):
        return 'plan_ui'

    if name.startswith(('Artifact', 'mcp__Claude_Browser__', 'mcp__visualize__', 'mcp__claude-in-chrome__',
                        'SendUserFile')):
        return 'artifact'

    if name in ASK_TOOLS:
        return 'ask'

    if name in ('WebSearch', 'WebFetch'):
        return 'web'

    if name in ('ToolSearch', 'Skill') or 'McpResource' in name:
        return 'setup'

    return 'other'


def bash_category(cmd):
    c = re.sub(r'^\s*(cd\s+\S+\s*(&&|;)\s*)+', '', cmd)
    if re.search(r'\bsed -i|\bperl -pi', c):
        return 'edit_docs' if re.search(r'docs[/\\]', c, re.IGNORECASE) else 'edit_code'

    if TEST_CMD.search(c):
        return 'tests'

    if re.search(r'\bpython\b|\$PY\b|\.py\b', c):
        return 'scripts'

    if re.match(r'\s*git\b', c):
        return 'git'

    if READ_CMD.search(c) or re.match(r'\s*\w+=', c):
        return 'docs' if DOC_PATH.search(c) else 'read_code'

    return 'other'


def result_error(head, is_error):
    """Kind of error by the start of a tool result; None - no error. MCP servers may report it in the text."""
    if not is_error and not re.search(r'"success"\s*:\s*false', head) and not head.startswith('Error:'):
        return None

    low = head.lower()
    if 'compilation failed' in low or 'build failed' in low:
        return 'compile'

    if 'timed out' in low or 'timeout' in low:
        return 'timeout'

    if 'not ready' in low or 'disconnected' in low or 'connection' in low:
        return 'disconnected'

    if 'too large' in low or 'exceeds maximum' in low:
        return 'too_big'

    if "doesn't want to proceed" in low or 'rejected' in low:
        return 'rejected'

    return 'error'


def prompt_text(content):
    if isinstance(content, str):
        return content

    return ' '.join(b.get('text', '') for b in content if isinstance(b, dict) and b.get('type') == 'text')


def prompt_kind(d):
    """'human' - the human's reply, 'brief' - a task from another session, 'system' - events, None - not a reply."""
    if d.get('isMeta') or d.get('isCompactSummary'):
        return None

    c = d['message']['content']
    if isinstance(c, list) and any(isinstance(b, dict) and b.get('type') == 'tool_result' for b in c):
        return None

    origin = (d.get('origin') or {}).get('kind') or d.get('turnOrigin')
    text = re.sub(r'<system-reminder>.*?</system-reminder>', '', prompt_text(c), flags=re.S).strip()
    if not text and not (isinstance(c, list) and any(b.get('type') == 'image' for b in c)):
        return None

    if text.startswith('This session is being continued'):
        return None

    if text.startswith('Background (from the session that started this one)'):
        return 'brief'

    if origin == 'human':
        return 'human'

    if origin in ('task_notification', 'peer') or text.startswith(('<task-notification', '<side-sessions-event',
                                                                   '<ci-monitor-event', '<command-')):
        return 'system'

    if origin == 'sdk' and d.get('turnOrigin') == 'sdk' and d.get('promptSource') == 'sdk' and text.startswith('<'):
        return 'system'

    return 'human'


def parse(path):
    """One transcript (a session or a subagent) -> raw events."""
    s = {'title': None, 'requests': {}, 'api_ts': {}, 'tool_uses': {}, 'results': {}, 'prompts': [],
         'stamps': [], 'compactions': [], 'cost_state': None, 'first_text': None}
    for line in open(path, encoding='utf8'):
        if line.startswith('{"type":"api-request-blob"'):
            continue

        d = json.loads(line)
        t = d.get('type')
        stamp = ts(d.get('timestamp'))
        if t == 'custom-title':
            s['title'] = d['customTitle']
        elif t == 'cost-state':
            s['cost_state'] = d
        elif t == 'api-request':
            s['api_ts'][d['id']] = stamp
        elif t == 'system' and d.get('subtype') == 'compact_boundary':
            s['compactions'].append((d.get('compactMetadata') or {}).get('preTokens') or 0)
        elif t == 'assistant':
            m = d['message']
            rid = d.get('requestId') or m.get('id')
            r = s['requests'].setdefault(rid, {'first': stamp, 'last': stamp, 'usage': None, 'model': m.get('model'),
                                               'tools': [], 'ref': d.get('requestRef')})
            r['last'] = max(r['last'], stamp)
            if m.get('usage') and m.get('model') != '<synthetic>':
                r['usage'] = m['usage']
            for b in m.get('content') or []:
                if b.get('type') == 'tool_use':
                    s['tool_uses'][b['id']] = (b['name'], b.get('input') or {}, stamp, rid)
                    r['tools'].append(b['id'])
        elif t == 'user':
            c = d['message']['content']
            if isinstance(c, list):
                for b in c:
                    if isinstance(b, dict) and b.get('type') == 'tool_result':
                        rc = b.get('content')
                        text = rc if isinstance(rc, str) else ''.join(x.get('text', '') for x in rc or []
                                                                      if isinstance(x, dict))
                        images = sum(1 for x in rc if isinstance(x, dict) and x.get('type') == 'image') \
                            if isinstance(rc, list) else 0
                        s['results'][b['tool_use_id']] = (stamp, result_error(text[:400], b.get('is_error')),
                                                          len(text) + images * IMAGE_CHARS)
            kind = prompt_kind(d)
            if kind:
                text = prompt_text(c)
                s['prompts'].append((stamp, kind, text))
                if s['first_text'] is None:
                    s['first_text'] = text
        if stamp and t in ('assistant', 'user', 'system'):
            s['stamps'].append(stamp)
    return s


def request_cost(u, model):
    r = rate(model)
    return (u.get('input_tokens', 0) * r[0] + u.get('output_tokens', 0) * r[1] +
            u.get('cache_read_input_tokens', 0) * r[2] + u.get('cache_creation_input_tokens', 0) * r[3]) / 1e6


def summarize(s):
    """Raw events -> metrics (shared by a session and a subagent)."""
    reqs = sorted(s['requests'].values(), key=lambda r: r['first'])
    tok = defaultdict(int)
    by_cat = defaultdict(lambda: {'req': 0, 'usd': 0.0, 'api_min': 0.0, 'tool_min': 0.0})
    tools = defaultdict(lambda: [0, 0.0, 0])
    ctx = []
    usd = 0.0
    first_edit = None
    start = {'req': 0, 'usd': 0.0}
    prev_end = None
    for r in reqs:
        u = r['usage'] or {}
        for k in ('input_tokens', 'cache_read_input_tokens', 'cache_creation_input_tokens', 'output_tokens'):
            tok[k] += u.get(k, 0)
        tok['thinking'] += (u.get('output_tokens_details') or {}).get('thinking_tokens', 0)
        c = u.get('input_tokens', 0) + u.get('cache_read_input_tokens', 0) + u.get('cache_creation_input_tokens', 0)
        if u:
            ctx.append(c)
        cost = request_cost(u, r['model']) if u else 0.0
        usd += cost
        began = s['api_ts'].get(r['ref']) or prev_end or r['first']
        api_sec = max(0.0, r['last'] - began) if began else 0.0
        if api_sec > 1800:   # a request started before a long idle: time unknown
            api_sec = 0.0
        prev_end = r['last']
        cats = [category(*s['tool_uses'][tid][:2]) for tid in r['tools']] or ['reply']
        for cat in cats:
            by_cat[cat]['req'] += 1 / len(cats)
            by_cat[cat]['usd'] += cost / len(cats)
            by_cat[cat]['api_min'] += api_sec / 60 / len(cats)
        if first_edit is None:
            if 'edit_code' in cats:
                first_edit = r['first']
            else:
                start['req'] += 1
                start['usd'] += cost
    docs = defaultdict(lambda: [0, 0, 0])   # reads, tokens, carried (tokens in later requests)
    doc_tokens = doc_carried = 0
    start['doc_tokens'] = 0
    req_starts = [r['first'] for r in reqs]
    errors = defaultdict(int)
    ctx_fill = defaultdict(lambda: [0.0, 0.0])
    ask_min = []
    for tid, (name, inp, t0, rid) in s['tool_uses'].items():
        res = s['results'].get(tid)
        sec = max(0.0, res[0] - t0) if res and res[0] and t0 else 0.0
        err = res[1] if res else None
        cat = category(name, inp)
        tools[name][0] += 1
        tools[name][1] += sec
        tools[name][2] += bool(err)
        if err:
            errors['%s:%s' % (name.split('__')[-1], err)] += 1
        if cat == 'ask':
            ask_min.append(sec / 60)
        else:
            by_cat[cat]['tool_min'] += sec / 60
        later = sum(1 for x in req_starts if x > t0)
        if res:   # what fills the context: tokens of the call and its result, carried into later requests
            n = (res[2] + len(json.dumps(inp, ensure_ascii=False))) / \
                (DOC_CHARS_PER_TOKEN if cat == 'docs' else CODE_CHARS_PER_TOKEN)
            ctx_fill[cat][0] += n
            ctx_fill[cat][1] += n * later
        if cat == 'docs' and res and name in ('Read', 'Bash', 'PowerShell'):
            keys = [rel_doc(inp.get('file_path'))] if name == 'Read' else \
                sorted(set(doc_key(x) for x in DOC_PATH.findall(inp.get('command', '')))) or ['(shell)']
            n = int(res[2] / DOC_CHARS_PER_TOKEN)
            carried = n * later
            for key in keys:
                docs[key][0] += 1
                docs[key][1] += n // len(keys)
                docs[key][2] += carried // len(keys)
            doc_tokens += n
            doc_carried += carried
            if first_edit is None or t0 < first_edit:
                start['doc_tokens'] += n
    stamps = sorted(x for x in s['stamps'] if x)
    t_first, t_last = (stamps[0], stamps[-1]) if stamps else (None, None)
    # turns: from a reply to the last event before the next reply; the model's question inside is waiting
    prompts = sorted((p for p in s['prompts'] if p[0]), key=lambda p: p[0])
    active = wait = pause = 0.0
    pauses = 0
    for i, (p_ts, kind, _) in enumerate(prompts):
        nxt = prompts[i + 1][0] if i + 1 < len(prompts) else float('inf')
        inside = [x for x in stamps if p_ts <= x < nxt]
        end = inside[-1] if inside else p_ts
        active += end - p_ts
        if i + 1 < len(prompts) and prompts[i + 1][1] == 'human':
            gap = (nxt - end) / 60
            if gap <= WAIT_CAP_MIN:
                wait += gap
            else:
                pause += gap
                pauses += 1
    if not prompts and stamps:
        active = t_last - t_first
    for m in ask_min:
        active -= m * 60
        if m <= WAIT_CAP_MIN:
            wait += m
        else:
            pause += m
            pauses += 1
    start['min'] = round(((first_edit or t_last or 0) - (t_first or 0)) / 60, 1) if t_first else 0
    start['usd'] = round(start['usd'], 2)
    return {
        'start': iso(t_first), 'end': iso(t_last),
        'wall_min': round((t_last - t_first) / 60, 1) if t_first else 0,
        'active_min': round(active / 60, 1), 'human_wait_min': round(wait, 1),
        'pause_min': round(pause, 1), 'pauses': pauses,
        'prompts_human': sum(1 for p in prompts if p[1] == 'human'),
        'prompts_system': sum(1 for p in prompts if p[1] == 'system'),
        'requests': len(reqs),
        'api_min': round(sum(v['api_min'] for v in by_cat.values()), 1),
        'tool_min': round(sum(v['tool_min'] for v in by_cat.values()), 1),
        'tokens': {'input': tok['input_tokens'], 'cache_read': tok['cache_read_input_tokens'],
                   'cache_write': tok['cache_creation_input_tokens'], 'output': tok['output_tokens'],
                   'thinking': tok['thinking']},
        'ctx_first': ctx[0] if ctx else 0, 'ctx_max': max(ctx) if ctx else 0,
        'ctx_avg': int(sum(ctx) / len(ctx)) if ctx else 0,
        'usd': round(usd, 2),
        'by_cat': {k: {kk: round(vv, 2) for kk, vv in v.items()} for k, v in sorted(by_cat.items())},
        'start_phase': start, 'first_edit': iso(first_edit),
        'tools': {k: [v[0], round(v[1], 1), v[2]] for k, v in sorted(tools.items(), key=lambda kv: -kv[1][1])},
        'docs': {k: v for k, v in sorted(docs.items(), key=lambda kv: -kv[1][1])},
        'doc_tokens': doc_tokens, 'doc_carried_tokens': doc_carried,
        'errors': dict(sorted(errors.items(), key=lambda kv: -kv[1])),
        'ctx_fill': {k: [int(v[0]), int(v[1])] for k, v in sorted(ctx_fill.items(), key=lambda kv: -kv[1][1])},
        'ctx_base_carried': (ctx[0] if ctx else 0) * len(ctx),
        'compactions': s['compactions'],
        'plan_steps': plan_steps(s),
    }


def plan_steps(s):
    """Task list steps (update_plan) of the last list: total and closed."""
    lists = []
    for name, inp, t0, _ in sorted(s['tool_uses'].values(), key=lambda u: u[2] or 0):
        if not name.endswith('update_plan'):
            continue

        if inp.get('items'):
            lists.append([[it.get('content', ''), it.get('status')] for it in inp['items']])
        elif lists:
            items = lists[-1]
            cur = next((i for i, it in enumerate(items) if it[1] == 'in_progress'), None)
            for i in inp.get('done') or []:
                if 0 < i <= len(items):
                    items[i - 1][1] = 'completed'
            if inp.get('advance') and cur is not None:
                items[cur][1] = 'completed'
                nxt = next((i for i, it in enumerate(items) if it[1] == 'pending'), None)
                if nxt is not None:
                    items[nxt][1] = 'in_progress'
            if inp.get('start') and 0 < inp['start'] <= len(items):
                items[inp['start'] - 1][1] = 'in_progress'
    last = lists[-1] if lists else []
    return {'total': len(last), 'closed': sum(1 for it in last if it[1] == 'completed')}


def iso(x):
    return dt.datetime.fromtimestamp(x, dt.timezone.utc).isoformat(timespec='seconds').replace('+00:00', 'Z') if x else None


def agent_category(desc, agent_type):
    low = (desc or '').lower()
    if agent_type == 'cold-read' or re.search(r'cold read|холодн\w* чтени|cold-read|исполнитель дока', low):
        return 'cold_read'

    if re.search(r'compress|сжат|sync .*sections|выжимк|сверка док', low):
        return 'docs'

    if agent_type == 'review' or re.search(r'review|ревью|diff|дифф?|сверк|audit|аудит|verify|провер|inspection|lint',
                                           low):
        return 'review'

    if re.search(r'implement|реализ|fix|исправ|сделай', low):
        return 'impl'

    if agent_type in ('Explore', 'Plan') or re.search(r'research|explore|исслед|изуч|find|search|поиск|resear', low):
        return 'research'

    return 'other'


def session_kind(title, first):
    t = (title or '').lower()
    f = (first or '')[:400].lower()
    if 'управляющ' in t or 'это управляющий чат' in f:
        return 'managing'

    if 'финализ' in t:
        return 'finalization'

    if f.startswith('background (from the session that started this one)') or 'пачк' in t:
        return 'pack'

    if re.search(r'разбор|исслед|анализ|сверк|обдум|план|обсужд|идеи|прототип|примерк', t):
        return 'research'

    return 'direct'


def build(path):
    sid = os.path.basename(path)[:-6]
    s = parse(path)
    row = {'id': sid, 'title': s['title']}
    row.update(summarize(s))
    row['kind'] = session_kind(s['title'], s['first_text'])
    row['continuation'] = bool(re.search(r'продолж|часть \d|конец|остаток|пачка [2-9]|добивк', s['title'] or '', re.I))
    cs = s['cost_state']
    row['cost_state_usd'] = round(cs['totalCostUSD'], 2) if cs else None
    row['lines'] = [cs.get('totalLinesAdded', 0), cs.get('totalLinesRemoved', 0)] if cs else None
    human = [p for p in s['prompts'] if p[1] == 'human']
    row['rework_prompts'] = sum(1 for p in human[1:] if REWORK.search(p[2] or ''))
    # subagents
    agents = []
    sub_dir = os.path.join(os.path.dirname(path), sid, 'subagents')
    for ap in sorted(glob.glob(os.path.join(sub_dir, '**', '*.jsonl'), recursive=True)):
        meta_path = ap[:-6] + '.meta.json'
        meta = json.load(open(meta_path, encoding='utf8')) if os.path.exists(meta_path) else {}
        a = summarize(parse(ap))
        cat = agent_category(meta.get('description'), meta.get('agentType'))
        agents.append({'desc': meta.get('description'), 'type': meta.get('agentType'), 'cat': cat,
                       'usd': a['usd'], 'min': a['active_min'], 'requests': a['requests'],
                       'tokens': sum(a['tokens'].values()) - a['tokens']['thinking'],
                       'doc_tokens': a['doc_tokens'], 'start_usd': a['start_phase']['usd']})
    by = defaultdict(lambda: [0, 0.0, 0.0])
    for a in agents:
        by[a['cat']][0] += 1
        by[a['cat']][1] += a['usd']
        by[a['cat']][2] += a['min']
    row['agents'] = {'n': len(agents), 'usd': round(sum(a['usd'] for a in agents), 2),
                     'min': round(sum(a['min'] for a in agents), 1),
                     'by_cat': {k: [v[0], round(v[1], 2), round(v[2], 1)] for k, v in by.items()},
                     'list': agents}
    row['usd_total'] = round(row['usd'] + row['agents']['usd'], 2)
    return row


def load_rows():
    rows = {}
    if os.path.exists(OUT):
        for line in open(OUT, encoding='utf8'):
            if line.strip():
                r = json.loads(line)
                rows[r['id']] = r
    return rows


def save_rows(rows):
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, 'w', encoding='utf8', newline='\n') as f:
        for r in sorted(rows.values(), key=lambda r: r['start'] or ''):
            f.write(json.dumps(r, ensure_ascii=False, separators=(',', ':')) + '\n')


def fmt_k(n):
    return '%.1fM' % (n / 1e6) if n >= 1e6 else '%dk' % (n / 1e3)


def print_summary(rows):
    rows = sorted(rows, key=lambda r: r['start'] or '')
    if not rows:
        print('нет сессий')
        return

    print('%-16s %-8s %-44s %6s %6s %5s %7s %7s %6s %5s %4s' %
          ('начало', 'id', 'название', 'актив', 'ждёт', 'запр', '$', '$агент', 'пик', 'доки', 'сжат'))
    for r in rows:
        print('%-16s %-8s %-44s %5.0fм %5.0fм %5d %7.1f %7.1f %6s %5s %4d' % (
            (r['start'] or '')[:16].replace('T', ' '), r['id'][:8], (r['title'] or '')[:44], r['active_min'],
            r['human_wait_min'], r['requests'], r['usd'], r['agents']['usd'], fmt_k(r['ctx_max']),
            fmt_k(r['doc_tokens']), len(r['compactions'])))
    n = len(rows)
    tot = lambda f: sum(f(r) for r in rows)
    med = lambda f: statistics.median(f(r) for r in rows)
    print('\nИтого %d сессий: $%.0f (сессии %.0f + субагенты %.0f), активно %.1f ч, ждали человека %.1f ч' % (
        n, tot(lambda r: r['usd_total']), tot(lambda r: r['usd']), tot(lambda r: r['agents']['usd']),
        tot(lambda r: r['active_min']) / 60, tot(lambda r: r['human_wait_min']) / 60))
    over = [r for r in rows if r['ctx_max'] > CTX_LINE]
    print('Медианы: пик контекста %s; выше %s — %d из %d (%.0f%% денег); старт до первой правки $%.2f; '
          'доков на сессию %s' % (
              fmt_k(med(lambda r: r['ctx_max'])), fmt_k(CTX_LINE), len(over), n,
              100 * sum(r['usd_total'] for r in over) / (tot(lambda r: r['usd_total']) or 1),
              med(lambda r: r['start_phase']['usd']), fmt_k(med(lambda r: r['doc_tokens']))))
    cats = defaultdict(lambda: [0.0, 0.0])
    for r in rows:
        for k, v in r['by_cat'].items():
            cats[k][0] += v['usd']
            cats[k][1] += v['api_min'] + v['tool_min']
    total_usd = sum(v[0] for v in cats.values()) or 1
    print('Запросы по виду работы ($ основной нити, время модели + инструментов):')
    for k, v in sorted(cats.items(), key=lambda kv: -kv[1][0]):
        print('  %-10s %6.1f$ %3.0f%%  %6.0f мин' % (k, v[0], 100 * v[0] / total_usd, v[1]))
    start_usd = tot(lambda r: r['start_phase']['usd'])
    print('До первой правки кода: $%.1f (%.0f%% основной нити), чтение доков %s токенов, перенос в контексте %s' % (
        start_usd, 100 * start_usd / total_usd, fmt_k(tot(lambda r: r['doc_tokens'])),
        fmt_k(tot(lambda r: r['doc_carried_tokens']))))
    docs = defaultdict(lambda: [0, 0, 0])
    for r in rows:
        for k, v in r['docs'].items():
            docs[k][0] += v[0]
            docs[k][1] += v[1]
            docs[k][2] += 1
    print('Доки, больше всего токенов на чтение (чтений / сессий / токенов):')
    for k, v in sorted(docs.items(), key=lambda kv: -kv[1][1])[:8]:
        print('  %-44s %4d %4d %8s' % (k, v[0], v[2], fmt_k(v[1])))
    ag = defaultdict(lambda: [0, 0.0])
    for r in rows:
        for k, v in r['agents']['by_cat'].items():
            ag[k][0] += v[0]
            ag[k][1] += v[1]
    if ag:
        print('Субагенты: ' + ', '.join('%s %d шт $%.0f' % (k, v[0], v[1])
                                        for k, v in sorted(ag.items(), key=lambda kv: -kv[1][1])))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('match', nargs='?', help='part of a session title or id')
    ap.add_argument('--since')
    ap.add_argument('--until')
    ap.add_argument('--all', action='store_true')
    ap.add_argument('--json', action='store_true')
    a = ap.parse_args()
    rows = load_rows()
    paths = sorted(glob.glob(os.path.join(TRANSCRIPTS, '*.jsonl')))
    picked = []
    for p in paths:
        sid = os.path.basename(p)[:-6]
        if a.match and a.match.lower() in sid:
            pass
        elif a.match:
            title = None
            for line in open(p, encoding='utf8'):
                if line.startswith('{"type":"custom-title"'):
                    title = json.loads(line)['customTitle']
            if not title or a.match.lower() not in title.lower():
                continue

        old = rows.get(sid)
        if old and old.get('_mtime') == os.path.getmtime(p) and old.get('_v') == VERSION:
            picked.append(old)
            continue

        r = build(p)
        r['_mtime'] = os.path.getmtime(p)
        r['_v'] = VERSION
        rows[sid] = r
        picked.append(r)
    save_rows(rows)
    if a.since or a.until:
        picked = [r for r in picked if (not a.since or (r['start'] or '') >= a.since) and
                  (not a.until or (r['start'] or '') < a.until + 'T99')]
    elif not a.match and not a.all:
        week = (dt.datetime.now(dt.timezone.utc) - dt.timedelta(days=7)).isoformat()
        picked = [r for r in picked if (r['end'] or '') >= week]
    if a.json:
        for r in picked:
            print(json.dumps(r, ensure_ascii=False))
    else:
        print_summary(picked)


if __name__ == '__main__':
    sys.exit(main())
