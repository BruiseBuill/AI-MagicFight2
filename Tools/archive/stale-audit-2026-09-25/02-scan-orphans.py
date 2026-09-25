# -*- coding: utf-8 -*-
"""孤儿资源 / 死代码 / 重复资源 检测（修正版：.meta 不参与引用收集）"""
import os, re, sys, collections, hashlib

ROOT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else \
       r'E:\UnityProject\Unity_AI_CardFight2'
ASSETS = os.path.join(ROOT, 'Assets')

GUID_RE = re.compile(r'\bguid:\s*([0-9a-f]{32})\b')
META_GUID_RE = re.compile(r'guid:\s*([0-9a-f]{32})')

SKIP_DIRS = {'Library', 'Temp', 'obj', '.git', 'Logs', 'UserSettings'}
REF_EXT = {'.cs', '.prefab', '.unity', '.asset', '.mat', '.asmdef', '.json', '.txt',
           '.shadergraph', '.controller', '.anim', '.overrideController', '.mask',
           '.preset', '.renderTexture', '.shader', '.uxml', '.uss'}
PKG_DIRS = ('Assets/Plugins/', 'Assets/TextMesh Pro/', 'Assets/ThirdParty/')

def rel(p): return os.path.relpath(p, ROOT).replace('\\', '/')

# ---------- guid 映射 ----------
guid2asset = {}
for dp, dns, fns in os.walk(ASSETS):
    for fn in fns:
        if not fn.endswith('.meta'): continue
        p = os.path.join(dp, fn)
        try: head = open(p, 'r', encoding='utf-8', errors='ignore').read(600)
        except Exception: continue
        m = META_GUID_RE.search(head)
        if m: guid2asset[m.group(1)] = rel(p[:-5])
asset2guid = {v: k for k, v in guid2asset.items()}

# ---------- 引用收集（排除 .meta） ----------
referenced = set(); ref_by = collections.defaultdict(set); cs_blobs = {}
for sr in [ASSETS, os.path.join(ROOT, 'ProjectSettings'), os.path.join(ROOT, 'Packages')]:
    if not os.path.isdir(sr): continue
    for dp, dns, fns in os.walk(sr):
        dns[:] = [d for d in dns if d not in SKIP_DIRS]
        for fn in fns:
            ext = os.path.splitext(fn)[1].lower()
            if ext not in REF_EXT: continue
            p = os.path.join(dp, fn)
            try:
                if os.path.getsize(p) > 4 * 1024 * 1024: continue
                txt = open(p, 'r', encoding='utf-8', errors='ignore').read()
            except Exception: continue
            r = rel(p)
            if ext == '.cs': cs_blobs[r] = txt
            for g in set(GUID_RE.findall(txt)):
                referenced.add(g); ref_by[g].add(r)

out = []
out.append('扫描: %d 个资产有 guid, 其中 %d 个被引用' % (len(guid2asset), len(set(guid2asset) & referenced)))

# ---------- A. 孤儿 ----------
orphans = [(os.path.splitext(a)[1].lower(), a) for g, a in guid2asset.items() if g not in referenced]
out.append('')
out.append('# ===== A. 孤儿资产 (guid 无任何引用) : %d 个 =====' % len(orphans))
cnt = collections.Counter(e for e, a in orphans)
out.append('  按类型: ' + ', '.join('%s=%d' % (k, v) for k, v in cnt.most_common()))
out.append('')
out.append('  --- 工程自有（排除 Plugins/TextMeshPro/ThirdParty） ---')
for e, a in sorted(orphans):
    if a.startswith(PKG_DIRS): continue
    out.append('  %-8s %s' % (e, a))
out.append('')
out.append('  --- 包/第三方（可能是噪声，仅计数） ---')
for e, a in sorted(orphans):
    if a.startswith(PKG_DIRS): out.append('  %-8s %s' % (e, a))

# ---------- B. Mono 脚本未被 prefab/scene 引用 ----------
scene_refs = set()
for dp, dns, fns in os.walk(ASSETS):
    for fn in fns:
        if fn.endswith(('.prefab', '.unity')):
            try: txt = open(os.path.join(dp, fn), 'r', encoding='utf-8', errors='ignore').read()
            except Exception: continue
            scene_refs |= set(GUID_RE.findall(txt))

mono_scripts = []
for a in guid2asset.values():
    if not a.endswith('.cs') or a.startswith(PKG_DIRS): continue
    try: txt = open(os.path.join(ROOT, a), 'r', encoding='utf-8', errors='ignore').read()
    except Exception: continue
    if ': MonoBehaviour' not in txt: continue
    g = asset2guid.get(a)
    mounted = bool(g and g in scene_refs)
    if not mounted: mono_scripts.append(a)
out.append('')
out.append('# ===== B. MonoBehaviour 脚本：未挂在任何 prefab/scene 上 =====')
out.append('   （不一定是死代码 —— 可能运行时 AddComponent / 由构建器生成）')
for a in sorted(mono_scripts): out.append('  ' + a)

# ---------- C. 空目录 ----------
out.append('')
out.append('# ===== C. 空目录 =====')
for dp, dns, fns in os.walk(ASSETS):
    dns[:] = [d for d in dns if d not in SKIP_DIRS]
    if not fns and not [d for d in dns if not d.startswith('.')]:
        out.append('  ' + rel(dp))

# ---------- D. 重复文件（内容 hash） ----------
out.append('')
out.append('# ===== D. 内容完全相同的重复文件 (>=100KB) =====')
h = collections.defaultdict(list)
for dp, dns, fns in os.walk(ASSETS):
    dns[:] = [d for d in dns if d not in SKIP_DIRS]
    for fn in fns:
        if fn.endswith('.meta'): continue
        p = os.path.join(dp, fn)
        try:
            sz = os.path.getsize(p)
            if sz < 100 * 1024: continue
            h[hashlib.md5(open(p, 'rb').read()).hexdigest()].append((sz, rel(p)))
        except Exception: pass
tot = 0
for k, v in sorted(h.items(), key=lambda kv: -kv[1][0][0]):
    if len(v) < 2: continue
    tot += sum(s for s, _ in v[1:])
    out.append('  %.1f KB x%d' % (v[0][0] / 1024.0, len(v)))
    for s, a in v: out.append('      ' + a)
out.append('  可省: %.1f MB' % (tot / 1048576.0))

sys.stdout.write('\n'.join(out))
