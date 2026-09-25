# -*- coding: utf-8 -*-
"""最终执行：删除临时产物 + 历史截图（回收站）。先做误报校正与保护性转移。"""
import os, re, sys, ctypes, shutil
from ctypes import wintypes
from collections import defaultdict

ROOT = r'E:\UnityProject\Unity_AI_CardFight2'
WORK = r'E:\WebApplicationByAI\WorkBuddy\Unity_CardFight2'
SAFE = r'E:\UnityProject\_tmp_build\safe_delete.txt'
KEEP = r'E:\UnityProject\_CardFight2_Backups'

# ── 1. 保护性转移 ──
# 1a. MCP 桥脚本 ubridge.mjs：不是垃圾，移出 tmp
ub = os.path.join(ROOT, '.workbuddy', 'tmp', 'ubridge.mjs')
if os.path.exists(ub):
    dst = os.path.join(ROOT, '.workbuddy', 'ubridge.mjs')
    shutil.copy2(ub, dst)
    print('[keep] ubridge.mjs -> .workbuddy/ubridge.mjs')

# 1b. BattleCanvas 旧备份
bk = os.path.join(ROOT, '.workbuddy', 'tmp', 'backup_BattleCanvas_20260919.prefab')
if os.path.exists(bk):
    d = os.path.join(KEEP, 'pre-m39-cleanup-2026-09-25')
    os.makedirs(d, exist_ok=True)
    shutil.copy2(bk, os.path.join(d, os.path.basename(bk)))
    print('[keep] backup_BattleCanvas_20260919.prefab -> %s' % d)

# ── 2. 构造最终顶层清单 ──
final = []
def add(p, cat):
    if os.path.exists(p):
        final.append((os.path.abspath(p), cat))

# 临时产物：整目录 + 零散文件
add(os.path.join(ROOT, '.workbuddy', 'tmp'), 'A1 .workbuddy/tmp')
add(os.path.join(ROOT, 'Tools', 'art-audit', '_work'), 'A2 art-audit/_work')
add(os.path.join(ROOT, 'Tools', 'art-audit', '_tmp'), 'A2 art-audit/_tmp')
rd = os.path.join(ROOT, 'Tools', 'RuleSelfTest')
if os.path.isdir(rd):
    for fn in os.listdir(rd):
        if fn.endswith('.log') or re.match(r'trace_\d+\.txt$', fn):
            add(os.path.join(rd, fn), 'A3 自测日志')
for d in ('Assets/Screenshots', 'Assets/_TrashArt'):
    p = os.path.join(ROOT, d.replace('/', os.sep))
    add(p, 'A4 空目录'); add(p + '.meta', 'A4 空目录 meta')
add(os.path.join(WORK, '='), 'A5 工作区误建')
add(os.path.join(WORK, '.workbuddy', 'zoom_marker.png'), 'A5 工作区临时图')

# 历史截图：只取「未被任何文本引用」的那部分（safe_delete.txt 已筛过）
refcrop = os.path.join(ROOT, 'Captures', 'art-review', '_refcrop')
n_b = 0
for line in open(SAFE, encoding='utf-8'):
    p = line.strip()
    if not p:
        continue
    if p.startswith(os.path.join(ROOT, 'Captures')):
        if os.sep + '_refcrop' + os.sep in p:
            continue                      # 整目录统一处理
        add(p, 'B 历史截图（无引用）')
        n_b += 1
add(refcrop, 'B3 _refcrop 整目录')

# ── 3. 归并：被待删目录覆盖的子路径丢弃 ──
dirs = sorted({p for p, c in final if os.path.isdir(p)}, key=len)
merged = []
for p, c in sorted(final, key=lambda x: len(x[0])):
    if any(p != d and p.startswith(d + os.sep) for d in dirs):
        continue
    merged.append((p, c))

def size_of(p):
    if os.path.isfile(p):
        try: return os.path.getsize(p)
        except Exception: return 0
    s = 0
    for dp, dns, fns in os.walk(p):
        for fn in fns:
            try: s += os.path.getsize(os.path.join(dp, fn))
            except Exception: pass
    return s

agg = defaultdict(lambda: [0, 0])
for p, c in merged:
    agg[c][0] += 1; agg[c][1] += size_of(p)
tot = sum(v[1] for v in agg.values())
print('')
print('最终删除：%d 个顶层条目，%.2f MB' % (len(merged), tot / 1048576.0))
for c in sorted(agg):
    print('   %-26s %2d 项 %9.2f MB' % (c, agg[c][0], agg[c][1] / 1048576.0))

if '--confirm' not in sys.argv:
    print('\n[dry-run] 未删除。加 --confirm 执行。')
    for p, c in sorted(merged, key=lambda x: x[1]):
        print('   [%s] %s' % (c, p))
    sys.exit(0)

# ── 4. 回收站删除 ──
FO_DELETE = 3
flag = 0x40 | 0x10 | 0x4 | 0x400        # ALLOWUNDO | NOCONFIRMATION | SILENT | NOERRORUI

class SHFILEOPSTRUCTW(ctypes.Structure):
    _fields_ = [('hwnd', wintypes.HWND), ('wFunc', wintypes.UINT),
                ('pFrom', ctypes.c_wchar_p), ('pTo', ctypes.c_wchar_p),
                ('fFlags', ctypes.c_uint16), ('fAnyOperationsAborted', wintypes.BOOL),
                ('hNameMappings', ctypes.c_void_p), ('lpszProgressTitle', ctypes.c_wchar_p)]

shell32 = ctypes.windll.shell32
shell32.SHFileOperationW.argtypes = [ctypes.POINTER(SHFILEOPSTRUCTW)]
shell32.SHFileOperationW.restype = ctypes.c_int

plist = [p for p, c in merged]
buf = ctypes.create_unicode_buffer('\0'.join(plist) + '\0\0')
op = SHFILEOPSTRUCTW()
op.wFunc = FO_DELETE
op.pFrom = ctypes.cast(buf, ctypes.c_wchar_p)
op.fFlags = flag
rc = shell32.SHFileOperationW(ctypes.byref(op))
print('')
print('SHFileOperationW rc=%d aborted=%s' % (rc, bool(op.fAnyOperationsAborted)))
left = [p for p in plist if os.path.exists(p)]
print('仍存在 %d / %d' % (len(left), len(plist)))
for p in left:
    print('   LEFT ' + p)
